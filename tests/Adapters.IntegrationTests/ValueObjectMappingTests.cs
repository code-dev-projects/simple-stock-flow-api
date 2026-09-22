using Microsoft.EntityFrameworkCore;
using SimpleStockFlow.Adapters.Persistence;
using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.Identity;
using SimpleStockFlow.Domain.Sales;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Adapters.IntegrationTests;

/// <summary>
/// Value object constructors validate and throw, while the ORM rebuilds them through a
/// converter. If that pairing is broken every repository is broken with it, so this runs
/// before any repository exists.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ValueObjectMappingTests
{
    private readonly PostgresFixture _postgres;

    public ValueObjectMappingTests(PostgresFixture postgres) => _postgres = postgres;

    private SalesDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SalesDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options);

    /// <summary>
    /// The schema arrives through migrations, the same way the host creates it. EnsureCreated
    /// would be a second, silently incompatible path: it does nothing once the database exists,
    /// so whichever mechanism ran first would decide whether the tables are there.
    /// </summary>
    private async Task<SalesDbContext> SchemaReadyContextAsync()
    {
        var context = NewContext();
        await context.Database.MigrateAsync();
        return context;
    }

    /// <summary>
    /// Uses a seeded category instead of inventing one: category names are unique, so a test
    /// that creates its own eventually collides with the reference data.
    /// </summary>
    private static Task<Guid> AnyCategoryIdAsync(SalesDbContext context) =>
        context.Categories.Select(category => category.Id).FirstAsync();

    [Fact]
    public async Task Product_price_survives_a_round_trip_through_the_database()
    {
        Product product;

        await using (var write = await SchemaReadyContextAsync())
        {
            product = Product.Create(
                "Taladro percutor", new Money(1500.10m), 7, await AnyCategoryIdAsync(write));

            write.Products.Add(product);
            await write.SaveChangesAsync();
        }

        // A separate context forces materialization instead of returning the tracked instance.
        await using var read = NewContext();
        var stored = await read.Products.SingleAsync(persisted => persisted.Id == product.Id);

        stored.Price.Amount.Should().Be(1500.10m);
        stored.Price.Currency.Should().Be(Money.DefaultCurrency);
        stored.Stock.Should().Be(7);
    }

    [Fact]
    public async Task Sale_items_rebuild_quantity_and_the_price_frozen_at_sale_time()
    {
        Sale sale;

        await using (var write = await SchemaReadyContextAsync())
        {
            var product = Product.Create(
                "Esmalte sintético", new Money(89.99m), 10, await AnyCategoryIdAsync(write));

            sale = Sale.Open(DateTimeOffset.UtcNow, await ASellerAsync(write), "vendedora");
            sale.AddItem(product, new Quantity(3), "Herramientas");

            write.Products.Add(product);
            write.Sales.Add(sale);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();
        var stored = await read.Sales.SingleAsync(persisted => persisted.Id == sale.Id);

        var item = stored.Items.Single();
        item.Quantity.Value.Should().Be(3);
        item.UnitPrice.Amount.Should().Be(89.99m);
        item.ProductName.Should().Be("Esmalte sintético");
        stored.Total.Amount.Should().Be(269.97m);
    }

    [Fact]
    public async Task A_product_without_an_image_comes_back_without_one()
    {
        Product product;

        await using (var write = await SchemaReadyContextAsync())
        {
            product = Product.Create("Llave inglesa", new Money(45m), 2, await AnyCategoryIdAsync(write));

            write.Products.Add(product);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();
        var stored = await read.Products.SingleAsync(persisted => persisted.Id == product.Id);

        stored.ImageKey.Should().BeNull();
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
