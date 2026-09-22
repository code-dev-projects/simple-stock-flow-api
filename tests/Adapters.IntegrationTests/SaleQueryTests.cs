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
/// The half of HU-05 that no double can answer: which rows a range selects, the order the
/// engine serves them in, and whether the lines travel with their sale at all. A faked
/// repository would agree with whatever the use case asked for, including an inclusive final
/// edge that D-C2 forbids.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SaleQueryTests
{
    private static int _windows;

    private readonly PostgresFixture _postgres;

    public SaleQueryTests(PostgresFixture postgres) => _postgres = postgres;

    private ServiceProvider NewContainer() =>
        new ServiceCollection()
            .AddPersistenceAdapter(_postgres.ConnectionString)
            .BuildServiceProvider();

    private static GetSaleService SalesOf(IServiceProvider services) =>
        new(services.GetRequiredService<ISaleRepository>());

    private static async Task<SalesDbContext> MigratedContextAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<SalesDbContext>();
        await context.Database.MigrateAsync();
        return context;
    }

    /// <summary>
    /// Other suites share this database and register sales of their own, dated by the clock.
    /// Each test takes a private day far outside any of that, so the totals it asserts belong
    /// to it alone and do not depend on which tests ran first.
    /// </summary>
    private static DateTimeOffset NextWindow() =>
        new DateTimeOffset(2200, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(Interlocked.Increment(ref _windows));

    private static async Task<Guid> AnyCategoryAsync(SalesDbContext context) =>
        await context.Categories.OrderBy(category => category.Name).Select(category => category.Id).FirstAsync();

    private static async Task<Guid> SeedSaleAsync(
        SalesDbContext context,
        DateTimeOffset soldAt,
        Guid categoryId,
        string soldBy = "ana",
        decimal price = 1_000m,
        int quantity = 1)
    {
        var product = Product.Create($"Martillo {Guid.NewGuid():N}", new Money(price), quantity, categoryId);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var sale = Sale.Open(soldAt, await ASellerAsync(context), soldBy);
        sale.AddItem(product, new Quantity(quantity), "Herramientas");
        context.Sales.Add(sale);
        await context.SaveChangesAsync();

        return sale.Id;
    }

    /// <summary>
    /// D-C2, the decision that makes two contiguous ranges add up to the whole period. A sale
    /// standing exactly on the final edge belongs to the next range, not to this one, and a
    /// sale standing exactly on the initial edge belongs to this one.
    /// </summary>
    [Fact]
    public async Task The_final_edge_of_the_range_is_left_out_and_the_initial_one_is_kept()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        var context = await MigratedContextAsync(scope.ServiceProvider);
        var category = await AnyCategoryAsync(context);

        var from = NextWindow();
        var to = from.AddHours(10);
        var onTheInitialEdge = await SeedSaleAsync(context, from, category);
        var inside = await SeedSaleAsync(context, from.AddHours(5), category);
        var onTheFinalEdge = await SeedSaleAsync(context, to, category);

        var page = await SalesOf(scope.ServiceProvider).ListAsync(new DateRange(from, to), new PageRequest(1, 20));

        page.Total.Should().Be(2);
        page.Items.Select(sale => sale.Id).Should().BeEquivalentTo([onTheInitialEdge, inside]);
        page.Items.Should().NotContain(sale => sale.Id == onTheFinalEdge);
    }

    /// <summary>
    /// The complement of the edge test: asked for the next range, the sale that was left out
    /// appears exactly once. Without it, an implementation that dropped both edges would still
    /// pass the test above and lose a sale between two reports.
    /// </summary>
    [Fact]
    public async Task The_sale_left_out_by_one_range_is_served_by_the_next_one()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        var context = await MigratedContextAsync(scope.ServiceProvider);
        var category = await AnyCategoryAsync(context);

        var from = NextWindow();
        var boundary = from.AddHours(10);
        var onTheBoundary = await SeedSaleAsync(context, boundary, category);

        var sales = SalesOf(scope.ServiceProvider);
        var before = await sales.ListAsync(new DateRange(from, boundary), new PageRequest(1, 20));
        var after = await sales.ListAsync(new DateRange(boundary, boundary.AddHours(10)), new PageRequest(1, 20));

        before.Total.Should().Be(0);
        after.Items.Should().ContainSingle().Which.Id.Should().Be(onTheBoundary);
    }

    /// <summary>
    /// E-11 and pattern Q7: most recent first. Seeded out of order on purpose -- with the rows
    /// inserted in the answer's own order the engine could return them by chance and the
    /// assertion would hold with no ORDER BY at all.
    /// </summary>
    [Fact]
    public async Task The_page_serves_the_most_recent_sale_first()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        var context = await MigratedContextAsync(scope.ServiceProvider);
        var category = await AnyCategoryAsync(context);

        var from = NextWindow();
        var middle = await SeedSaleAsync(context, from.AddHours(5), category);
        var latest = await SeedSaleAsync(context, from.AddHours(9), category);
        var earliest = await SeedSaleAsync(context, from.AddHours(1), category);

        var page = await SalesOf(scope.ServiceProvider)
            .ListAsync(new DateRange(from, from.AddHours(10)), new PageRequest(1, 20));

        page.Items.Select(sale => sale.Id).Should().Equal(latest, middle, earliest);
    }

    /// <summary>
    /// CA-05.2: the window is the engine's and the count is the whole range, not the page. A
    /// count taken over the same window would report 2 here and the portal would paint one page.
    /// </summary>
    [Fact]
    public async Task A_page_of_a_range_counts_the_range_and_serves_only_its_window()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        var context = await MigratedContextAsync(scope.ServiceProvider);
        var category = await AnyCategoryAsync(context);

        var from = NextWindow();
        for (var hour = 1; hour <= 5; hour++)
            await SeedSaleAsync(context, from.AddHours(hour), category);

        var page = await SalesOf(scope.ServiceProvider)
            .ListAsync(new DateRange(from, from.AddHours(10)), new PageRequest(2, 2));

        page.Total.Should().Be(5);
        page.Page.Should().Be(2);
        page.Size.Should().Be(2);
        page.TotalPages.Should().Be(3);
        page.Items.Select(sale => sale.SoldAt).Should().Equal(from.AddHours(3), from.AddHours(2));
    }

    /// <summary>
    /// CA-05.1 against the engine: read from a context that never saw the sale written, so the
    /// lines can only come from the database. The identity map of the writing context would
    /// answer with them whether or not the navigation is loaded at all.
    /// </summary>
    [Fact]
    public async Task A_sale_read_by_its_identifier_brings_its_lines_and_its_total_with_it()
    {
        Guid id;
        await using (var writer = NewContainer())
        {
            using var writing = writer.CreateScope();
            var context = await MigratedContextAsync(writing.ServiceProvider);
            id = await SeedSaleAsync(context, NextWindow(), await AnyCategoryAsync(context), "bea", 12_345.67m, 3);
        }

        await using var container = NewContainer();
        using var scope = container.CreateScope();
        await MigratedContextAsync(scope.ServiceProvider);

        var view = await SalesOf(scope.ServiceProvider).GetAsync(id);

        view.Should().NotBeNull();
        view!.SoldBy.Should().Be("bea");
        view.Currency.Should().Be(Money.DefaultCurrency);

        var line = view.Items.Should().ContainSingle().Subject;
        line.Quantity.Should().Be(3);
        line.UnitPrice.Should().Be(12_345.67m);
        line.Subtotal.Should().Be(37_037.01m);
        view.Total.Should().Be(view.Items.Sum(item => item.Subtotal));
    }

    /// <summary>The 404 of E-12 starts here: an identifier nobody wrote is nothing, not a throw.</summary>
    [Fact]
    public async Task A_sale_that_was_never_registered_comes_back_as_nothing()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        await MigratedContextAsync(scope.ServiceProvider);

        (await SalesOf(scope.ServiceProvider).GetAsync(Guid.NewGuid())).Should().BeNull();
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
