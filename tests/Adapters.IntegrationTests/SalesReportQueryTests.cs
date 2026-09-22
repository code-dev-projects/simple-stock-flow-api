using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimpleStockFlow.Adapters.Persistence;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Application.Services;
using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.Identity;
using SimpleStockFlow.Domain.Sales;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Adapters.IntegrationTests;

/// <summary>
/// HU-06 against the engine, which is the only place it can be answered. CA-06.5 makes the
/// aggregation itself the requirement, so a faked read port would assert nothing: it would
/// return whatever rows the test handed it, already grouped, already ordered and already
/// carrying the name DP-01 argues about.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SalesReportQueryTests
{
    private static int _windows;

    private readonly PostgresFixture _postgres;

    public SalesReportQueryTests(PostgresFixture postgres) => _postgres = postgres;

    private ServiceProvider NewContainer() =>
        new ServiceCollection()
            .AddPersistenceAdapter(_postgres.ConnectionString)
            .BuildServiceProvider();

    private static SalesReportService ReportOf(IServiceProvider services) =>
        new(services.GetRequiredService<ISalesReportQuery>());

    private static async Task<SalesDbContext> MigratedContextAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<SalesDbContext>();
        await context.Database.MigrateAsync();
        return context;
    }

    /// <summary>
    /// The report adds up a whole range with no window of its own, so every other suite's sales
    /// would land in it. Each test takes a private day, far from the year the sale suites use,
    /// and the totals it asserts belong to it alone.
    /// </summary>
    private static DateTimeOffset NextWindow() =>
        new DateTimeOffset(2300, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(Interlocked.Increment(ref _windows));

    private static async Task<Guid> AnyCategoryAsync(SalesDbContext context) =>
        await context.Categories.OrderBy(category => category.Name).Select(category => category.Id).FirstAsync();

    private static async Task<Product> SeedProductAsync(
        SalesDbContext context,
        Guid categoryId,
        decimal price,
        int stock)
    {
        var product = Product.Create($"Martillo {Guid.NewGuid():N}", new Money(price), stock, categoryId);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product;
    }

    private static async Task SeedSaleAsync(
        SalesDbContext context,
        DateTimeOffset soldAt,
        params (Product Product, int Quantity)[] lines)
    {
        var sale = Sale.Open(soldAt, await ASellerAsync(context), "ana");

        foreach (var (product, quantity) in lines)
        {
            // The label the product carries AT THIS MOMENT, resolved exactly as the use case does.
            // A constant would make the two-rows test pass for the wrong reason.
            var categoryName = await context.Categories
                .Where(category => category.Id == product.CategoryId)
                .Select(category => category.Name)
                .SingleAsync();

            sale.AddItem(product, new Quantity(quantity), categoryName);
        }

        context.Sales.Add(sale);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// CA-06.1 against a set of sales whose every number is different from every other: two
    /// products, three sales, five units of one and four of the other. Counting lines instead
    /// of sales, adding units instead of amounts or grouping by sale instead of by product each
    /// give a number that appears nowhere in the assertions.
    /// </summary>
    [Fact]
    public async Task The_report_of_a_known_set_of_sales_adds_units_and_amounts_up_per_product()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        var context = await MigratedContextAsync(scope.ServiceProvider);
        var category = await AnyCategoryAsync(context);

        var from = NextWindow();
        var hammer = await SeedProductAsync(context, category, price: 1_000m, stock: 10);
        var brush = await SeedProductAsync(context, category, price: 250.50m, stock: 10);

        await SeedSaleAsync(context, from.AddHours(1), (hammer, 2), (brush, 4));
        await SeedSaleAsync(context, from.AddHours(2), (hammer, 3));

        var report = await ReportOf(scope.ServiceProvider).HandleAsync(new DateRange(from, from.AddHours(10)));

        report.SalesCount.Should().Be(2);
        report.Rows.Should().HaveCount(2);

        var hammerRow = report.Rows.Single(row => row.ProductId == hammer.Id);
        hammerRow.UnitsSold.Should().Be(5);
        hammerRow.Revenue.Should().Be(5_000m);

        var brushRow = report.Rows.Single(row => row.ProductId == brush.Id);
        brushRow.UnitsSold.Should().Be(4);
        brushRow.Revenue.Should().Be(1_002m);

        report.GrandTotal.Should().Be(6_002m);
        report.GrandTotal.Should().Be(report.Rows.Sum(row => row.Revenue));
        report.Currency.Should().Be(Money.DefaultCurrency);
    }

    /// <summary>
    /// CA-06.2 and D-C10 over the engine: an untouched range answers a report, not an error and
    /// not a null, and it still names its currency. A day nobody sold in is the case the portal
    /// opens the screen on.
    /// </summary>
    [Fact]
    public async Task A_range_without_sales_is_an_empty_report_and_not_an_error()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        await MigratedContextAsync(scope.ServiceProvider);

        var from = NextWindow();
        var report = await ReportOf(scope.ServiceProvider).HandleAsync(new DateRange(from, from.AddHours(10)));

        report.Rows.Should().NotBeNull().And.BeEmpty();
        report.SalesCount.Should().Be(0);
        report.GrandTotal.Should().Be(0m);
        report.Currency.Should().Be(Money.DefaultCurrency);
    }

    /// <summary>
    /// DP-01, in the direction that catches every shortcut at once. The product is sold, then
    /// renamed, then sold again -- but the second sale is dated <em>earlier</em> in the range,
    /// so the most recent sale of the range is the one that froze the <em>old</em> name while
    /// the catalogue, the last row written and the newest frozen value all say the new one.
    /// Only ordering the window by sold_at answers "Martillo".
    /// </summary>
    [Fact]
    public async Task A_product_renamed_inside_the_range_arrives_as_one_row_per_frozen_name()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        var context = await MigratedContextAsync(scope.ServiceProvider);
        var category = await AnyCategoryAsync(context);

        var from = NextWindow();
        var product = await SeedProductAsync(context, category, price: 1_000m, stock: 10);
        var nameWhenSoldLast = product.Name;

        await SeedSaleAsync(context, from.AddHours(5), (product, 2));

        product.Rename($"Renombrado {Guid.NewGuid():N}");
        await context.SaveChangesAsync();
        await SeedSaleAsync(context, from.AddHours(1), (product, 3));

        var report = await ReportOf(scope.ServiceProvider).HandleAsync(new DateRange(from, from.AddHours(10)));

        // Owner's decision, 2026-09-21: the name follows the same rule the category already did.
        // Nothing picks a winner, so the two frozen names are two rows and the units stay where the
        // sale put them. Picking the most recent let a NEW sale change what a closed period already
        // said, which is what ADR-004 exists to prevent -- entering through the sale, not the
        // catalogue. Neither row may carry the live name.
        report.Rows.Should().HaveCount(2, "two frozen names are two rows");
        report.Rows.Select(row => row.ProductName)
            .Should().BeEquivalentTo(new[] { nameWhenSoldLast, product.Name });
        report.Rows.Single(row => row.ProductName == nameWhenSoldLast).UnitsSold.Should().Be(2);
        report.Rows.Single(row => row.ProductName == product.Name).UnitsSold.Should().Be(3);
        report.Rows.Sum(row => row.Revenue).Should().Be(5_000m, "splitting the label loses no money");
        report.Rows.Should().OnlyContain(row => row.CategoryName != null);
    }

    /// <summary>
    /// The category the report shows must be the one the line froze, never the one the
    /// catalogue holds now: recategorising a product would otherwise rewrite the report of a
    /// closed period, which CA-06.4 and RNF-02 forbid and ADR-004 rejected by name. That half
    /// of the rule is permanent and holds today.
    ///
    /// The second half does not. sale_item has no category_name column -- that is T-11, still
    /// pending -- so the line has frozen nothing and the query answers the empty string that
    /// invents no category. The contract promises a frozen name here, so this assertion pins a
    /// gap, not a behaviour: **when T-11 lands it goes red, and the expected value becomes the
    /// name the line froze.** Deleting the assertion instead would close a real defect in
    /// silence, which is how it survived this long.
    /// </summary>
    [Fact]
    public async Task The_report_shows_the_category_the_sale_froze_and_never_the_one_the_catalogue_shows_now()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        var context = await MigratedContextAsync(scope.ServiceProvider);

        var categoryWhenSold = Category.Create($"Cuando se vendio {Guid.NewGuid():N}");
        var categoryAfterwards = Category.Create($"Despues de venderse {Guid.NewGuid():N}");
        context.Categories.AddRange(categoryWhenSold, categoryAfterwards);
        await context.SaveChangesAsync();

        var from = NextWindow();
        var product = await SeedProductAsync(context, categoryWhenSold.Id, price: 1_000m, stock: 10);
        await SeedSaleAsync(context, from.AddHours(1), (product, 2));

        product.SetCategory(categoryAfterwards.Id);
        await context.SaveChangesAsync();

        var report = await ReportOf(scope.ServiceProvider).HandleAsync(new DateRange(from, from.AddHours(10)));

        var row = report.Rows.Should().ContainSingle().Subject;
        row.CategoryName.Should().NotBe(
            categoryAfterwards.Name,
            "a report of a closed period cannot follow a recategorisation (ADR-004, CA-06.4, RNF-02)");
        row.CategoryName.Should().Be(
            categoryWhenSold.Name,
            "T-11 freezes the category on the line, so the report shows the label the sale carried "
            + "and never the one the catalogue shows now");
    }

    /// <summary>
    /// The owner's rule, decided 2026-09-20: faced with a recategorisation inside the range the
    /// report does NOT pick a winner -- it groups BY the frozen label, so the same product arrives
    /// as two rows. Picking the most recent would let a new sale rewrite what a closed period
    /// already said, which is the very thing ADR-004 exists to prevent, entering through the sale
    /// instead of through the live catalogue. Collapsing the two into one would be deciding they
    /// are the same thing, and that decision belonged to whoever renamed.
    /// </summary>
    [Fact]
    public async Task A_product_recategorised_inside_the_range_arrives_as_one_row_per_frozen_label()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        var context = await MigratedContextAsync(scope.ServiceProvider);

        var first = Category.Create($"Herramientas {Guid.NewGuid():N}");
        var second = Category.Create($"Ferreteria {Guid.NewGuid():N}");
        context.Categories.AddRange(first, second);
        await context.SaveChangesAsync();

        var from = NextWindow();
        var product = await SeedProductAsync(context, first.Id, price: 1_000m, stock: 10);
        await SeedSaleAsync(context, from.AddHours(1), (product, 2));

        product.SetCategory(second.Id);
        await context.SaveChangesAsync();
        await SeedSaleAsync(context, from.AddHours(2), (product, 3));

        var report = await ReportOf(scope.ServiceProvider).HandleAsync(new DateRange(from, from.AddHours(10)));

        report.Rows.Should().HaveCount(2, "two frozen labels are two rows, not one");
        report.Rows.Select(row => row.CategoryName)
            .Should().BeEquivalentTo(new[] { first.Name, second.Name });
        report.Rows.Sum(row => row.UnitsSold).Should().Be(5, "splitting the label must not lose a unit");
        report.GrandTotal.Should().Be(5_000m);
    }

    /// <summary>
    /// DP-01 in the other direction, so the rule cannot be satisfied by always picking the
    /// oldest frozen name. Here the rename happens before the last sale of the range and the
    /// report has to move with it.
    /// </summary>
    [Fact]
    public async Task A_rename_before_the_last_sale_of_the_range_still_leaves_both_names_standing()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        var context = await MigratedContextAsync(scope.ServiceProvider);
        var category = await AnyCategoryAsync(context);

        var from = NextWindow();
        var product = await SeedProductAsync(context, category, price: 1_000m, stock: 10);
        var nameBefore = product.Name;

        await SeedSaleAsync(context, from.AddHours(1), (product, 2));

        product.Rename($"Renombrado {Guid.NewGuid():N}");
        await context.SaveChangesAsync();
        await SeedSaleAsync(context, from.AddHours(5), (product, 3));

        var report = await ReportOf(scope.ServiceProvider).HandleAsync(new DateRange(from, from.AddHours(10)));

        // The same rule from the other direction, so it cannot be satisfied by always keeping the
        // oldest name either: here the rename happens BEFORE the last sale and both labels still
        // stand on their own row, each with the units its own sale carried.
        report.Rows.Should().HaveCount(2);
        report.Rows.Single(row => row.ProductName == nameBefore).UnitsSold.Should().Be(2);
        report.Rows.Single(row => row.ProductName == product.Name).UnitsSold.Should().Be(3);
    }

    /// <summary>
    /// E-13 states the order as contract: the highest billing on top. Seeded in an order no
    /// accident reproduces -- neither insertion order nor name order matches the answer -- so
    /// a query with no ORDER BY cannot pass by luck.
    /// </summary>
    [Fact]
    public async Task The_rows_arrive_ordered_by_revenue_descending()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        var context = await MigratedContextAsync(scope.ServiceProvider);
        var category = await AnyCategoryAsync(context);

        var from = NextWindow();
        var cheapest = await SeedProductAsync(context, category, price: 1_000m, stock: 10);
        var dearest = await SeedProductAsync(context, category, price: 5_000m, stock: 10);
        var middle = await SeedProductAsync(context, category, price: 3_000m, stock: 10);

        await SeedSaleAsync(context, from.AddHours(1), (cheapest, 1), (dearest, 1), (middle, 1));

        var report = await ReportOf(scope.ServiceProvider).HandleAsync(new DateRange(from, from.AddHours(10)));

        report.Rows.Select(row => row.ProductId).Should().Equal(dearest.Id, middle.Id, cheapest.Id);
        report.Rows.Select(row => row.Revenue).Should().Equal(5_000m, 3_000m, 1_000m);
    }

    /// <summary>
    /// D-C2 applied to the report, which is where it was argued from: two contiguous ranges add
    /// up to the period. The sale standing on the final edge belongs to the next report, the
    /// one on the initial edge to this one, and neither is counted twice.
    /// </summary>
    [Fact]
    public async Task The_final_edge_of_the_range_is_left_out_and_the_next_report_picks_it_up()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        var context = await MigratedContextAsync(scope.ServiceProvider);
        var category = await AnyCategoryAsync(context);

        var from = NextWindow();
        var boundary = from.AddHours(10);
        var product = await SeedProductAsync(context, category, price: 1_000m, stock: 10);

        await SeedSaleAsync(context, from, (product, 1));
        await SeedSaleAsync(context, boundary, (product, 4));

        var report = ReportOf(scope.ServiceProvider);
        var before = await report.HandleAsync(new DateRange(from, boundary));
        var after = await report.HandleAsync(new DateRange(boundary, boundary.AddHours(10)));
        var whole = await report.HandleAsync(new DateRange(from, boundary.AddHours(10)));

        before.SalesCount.Should().Be(1);
        before.Rows.Should().ContainSingle().Which.UnitsSold.Should().Be(1);

        after.SalesCount.Should().Be(1);
        after.Rows.Should().ContainSingle().Which.UnitsSold.Should().Be(4);

        whole.SalesCount.Should().Be(2);
        whole.GrandTotal.Should().Be(before.GrandTotal + after.GrandTotal);
    }

    /// <summary>
    /// CA-06.4 and RNF-02: a closed period does not move because the catalogue did. The product
    /// is renamed and repriced between the two calls, which is the whole of what "the catalogue
    /// changed" can mean here, and the second report has to repeat the first one field by field.
    /// </summary>
    [Fact]
    public async Task Repeating_the_report_of_a_closed_period_returns_exactly_the_same_after_the_catalogue_changes()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        var context = await MigratedContextAsync(scope.ServiceProvider);
        var category = await AnyCategoryAsync(context);

        var from = NextWindow();
        var range = new DateRange(from, from.AddHours(10));
        var product = await SeedProductAsync(context, category, price: 1_000m, stock: 10);

        await SeedSaleAsync(context, from.AddHours(1), (product, 2));

        var report = ReportOf(scope.ServiceProvider);
        var first = await report.HandleAsync(range);

        // The four things "the catalogue changed" can mean, in one go. T-11 added the last two, and
        // they are a GUARD, not a driver: with the category frozen they passed the moment they were
        // written. They stay because the cheapest way to reopen this hole is to resolve the label in
        // the query, and that would turn this green test red on the spot.
        var otherCategory = Category.Create($"Otra {Guid.NewGuid():N}");
        context.Categories.Add(otherCategory);
        await context.SaveChangesAsync();

        product.Rename($"Renombrado {Guid.NewGuid():N}");
        product.ChangePrice(99_000m);
        product.SetCategory(otherCategory.Id);
        context.Entry(product).Property("deleted_at").CurrentValue = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();

        var second = await report.HandleAsync(range);

        second.Should().BeEquivalentTo(first);
        second.Rows.Should().ContainSingle().Which.Revenue.Should().Be(2_000m);
    }

    /// <summary>
    /// A sale outside the range contributes nothing, not even to the count. Without it, a query
    /// whose WHERE clause never reached the engine would still pass every assertion above,
    /// because each of those tests owns its window and sees only its own rows.
    /// </summary>
    [Fact]
    public async Task Sales_outside_the_range_are_not_counted_and_not_billed()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        var context = await MigratedContextAsync(scope.ServiceProvider);
        var category = await AnyCategoryAsync(context);

        var from = NextWindow();
        var product = await SeedProductAsync(context, category, price: 1_000m, stock: 10);

        await SeedSaleAsync(context, from.AddHours(1), (product, 2));
        await SeedSaleAsync(context, from.AddDays(200), (product, 7));

        var report = await ReportOf(scope.ServiceProvider).HandleAsync(new DateRange(from, from.AddHours(10)));

        report.SalesCount.Should().Be(1);
        report.Rows.Should().ContainSingle().Which.UnitsSold.Should().Be(2);
        report.GrandTotal.Should().Be(2_000m);
    }
    /// <summary>
    /// A persisted operator. Since T-12 a sale points at its author with a foreign key, so a seeder
    /// that invented an identifier is refused by the engine -- which is the whole point of FK-4.
    /// </summary>
    private static async Task<Guid> ASellerAsync(SalesDbContext context)
    {
        var seller = User.Create($"vendedora{Guid.NewGuid():N}", "hash-irrelevante", Roles.Seller);
        context.Set<User>().Add(seller);
        await context.SaveChangesAsync();
        return seller.Id;
    }

}
