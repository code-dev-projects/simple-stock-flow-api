using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimpleStockFlow.Adapters.Persistence;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Application.Services;
using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Adapters.IntegrationTests;

/// <summary>
/// The half of the catalog that no double can answer: case-insensitive matching, the row order
/// and the collation behind it are the engine's decisions, and a faked repository would agree
/// with whatever the use case asked for. The service is resolved over the registered adapters,
/// so the test also proves the wiring that ships is the one being exercised.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProductCatalogQueryTests
{
    private readonly PostgresFixture _postgres;

    public ProductCatalogQueryTests(PostgresFixture postgres) => _postgres = postgres;

    private ServiceProvider NewContainer() =>
        new ServiceCollection()
            .AddPersistenceAdapter(_postgres.ConnectionString)
            .BuildServiceProvider();

    private static ProductCatalogService CatalogOf(IServiceProvider services) =>
        new(
            services.GetRequiredService<IProductRepository>(),
            services.GetRequiredService<ICategoryRepository>(),
            new StorageStub(),
            services.GetRequiredService<IUnitOfWork>());

    private static async Task<SalesDbContext> MigratedContextAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<SalesDbContext>();
        await context.Database.MigrateAsync();
        return context;
    }

    /// <summary>
    /// Other suites share this database and seed products of their own, so every assertion here
    /// is scoped by a marker that only this run writes. Counting every row instead would make
    /// the result depend on which tests ran first.
    /// </summary>
    private static string NewMarker() => $"mk{Guid.NewGuid():N}";

    private static async Task SeedAsync(SalesDbContext context, string name, Guid categoryId)
    {
        context.Products.Add(Product.Create(name, new Money(1_000m), 5, categoryId));
        await context.SaveChangesAsync();
    }

    private static async Task<(Guid First, Guid Second)> TwoCategoriesAsync(SalesDbContext context)
    {
        var ids = await context.Categories.OrderBy(category => category.Name).Select(category => category.Id).Take(2).ToListAsync();
        return (ids[0], ids[1]);
    }

    /// <summary>
    /// D-C1. The order is the database's, not the client's: the portal paints the list as it
    /// arrives. Changing the collation of the database would change this contract.
    /// </summary>
    [Fact]
    public async Task The_reference_categories_come_back_ordered_by_the_collation_of_the_database()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        await MigratedContextAsync(scope.ServiceProvider);

        var categories = await CatalogOf(scope.ServiceProvider).ListCategoriesAsync();

        categories.Select(category => category.Name).Should().ContainInOrder(
            "Electricidad", "Fontanería", "General", "Herramientas", "Pinturas");
        categories.Should().OnlyContain(category => category.Id != Guid.Empty);
    }

    /// <summary>CA-01.2. Neither the case nor a whole word: ILIKE is what decides, not the use case.</summary>
    [Fact]
    public async Task The_search_matches_part_of_the_name_whatever_the_case()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        var context = await MigratedContextAsync(scope.ServiceProvider);
        var (tools, _) = await TwoCategoriesAsync(context);
        var marker = NewMarker();

        await SeedAsync(context, $"Martillo {marker}", tools);
        await SeedAsync(context, $"Alicate {marker}", tools);
        await SeedAsync(context, $"Destornillador {marker}", tools);

        var page = await CatalogOf(scope.ServiceProvider)
            .ListAsync(new ProductFilter(marker.ToUpperInvariant()), new PageRequest(1, 20));

        page.Total.Should().Be(3);
        page.Items.Select(product => product.Name).Should().Equal(
            $"Alicate {marker}", $"Destornillador {marker}", $"Martillo {marker}");
    }

    [Fact]
    public async Task The_category_filter_leaves_out_everything_else()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        var context = await MigratedContextAsync(scope.ServiceProvider);
        var (tools, paints) = await TwoCategoriesAsync(context);
        var marker = NewMarker();

        await SeedAsync(context, $"Martillo {marker}", tools);
        await SeedAsync(context, $"Brocha {marker}", paints);

        var page = await CatalogOf(scope.ServiceProvider)
            .ListAsync(new ProductFilter(marker, paints), new PageRequest(1, 20));

        page.Total.Should().Be(1);
        page.Items.Should().ContainSingle().Which.CategoryId.Should().Be(paints);
    }

    /// <summary>
    /// CA-01.5 end to end: the window the engine reads and the size the caller is told must be
    /// the same number, or the client's page count does not match the rows it receives.
    /// </summary>
    [Fact]
    public async Task A_size_over_the_maximum_serves_the_maximum_and_reports_it()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        await MigratedContextAsync(scope.ServiceProvider);

        var page = await CatalogOf(scope.ServiceProvider)
            .ListAsync(new ProductFilter(NewMarker()), new PageRequest(1, 500));

        page.Size.Should().Be(PageRequest.MaxSize);
        page.Page.Should().Be(1);
    }

    /// <summary>
    /// The category name is read live from its own row, not copied into the product. The
    /// restrictive foreign key is what makes that read always resolve.
    /// </summary>
    [Fact]
    public async Task A_product_created_through_the_use_case_reads_back_with_the_live_category_name()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        var context = await MigratedContextAsync(scope.ServiceProvider);
        var (tools, _) = await TwoCategoriesAsync(context);
        var expected = await context.Categories.Where(category => category.Id == tools)
            .Select(category => category.Name).SingleAsync();
        var catalog = CatalogOf(scope.ServiceProvider);
        var marker = NewMarker();

        var id = await catalog.CreateAsync(new CreateProductCommand($"Taladro {marker}", 199_900m, 7, tools));
        var view = await catalog.GetAsync(id);

        view.Should().NotBeNull();
        view!.Name.Should().Be($"Taladro {marker}");
        view.Price.Should().Be(199_900m);
        view.Currency.Should().Be(Money.DefaultCurrency);
        view.Stock.Should().Be(7);
        view.CategoryId.Should().Be(tools);
        view.CategoryName.Should().Be(expected);
        view.ImageUrl.Should().BeNull();
    }

    /// <summary>
    /// CA-02.4 against the engine. Without the check in the use case the row would still be
    /// refused -- by the foreign key -- but as an unrecognised failure, which the translation
    /// filter reports as a 500 instead of the 422 the contract prescribes.
    /// </summary>
    [Fact]
    public async Task Creating_against_a_category_that_does_not_exist_is_refused_as_a_broken_rule()
    {
        await using var container = NewContainer();
        using var scope = container.CreateScope();
        await MigratedContextAsync(scope.ServiceProvider);
        var unknown = Guid.NewGuid();

        var act = async () => await CatalogOf(scope.ServiceProvider)
            .CreateAsync(new CreateProductCommand($"Taladro {NewMarker()}", 199_900m, 7, unknown));

        await act.Should().ThrowAsync<DomainException>().WithMessage($"La categoría {unknown} no existe.");
    }

    /// <summary>The binary side has its own tests against doubles; here it only has to exist.</summary>
    private sealed class StorageStub : IFileStorage
    {
        public Task<string> SaveAsync(ImageUpload upload, CancellationToken ct = default) =>
            Task.FromResult($"{Guid.NewGuid():N}.jpg");

        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

        public string? ResolveUrl(string? key) => string.IsNullOrWhiteSpace(key) ? null : $"/media/{key}";
    }
}
