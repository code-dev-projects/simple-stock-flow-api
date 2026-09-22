using Microsoft.EntityFrameworkCore;
using SimpleStockFlow.Adapters.Persistence;

namespace SimpleStockFlow.Adapters.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class InitialSchemaTests
{
    private readonly PostgresFixture _postgres;

    public InitialSchemaTests(PostgresFixture postgres) => _postgres = postgres;

    private SalesDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SalesDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options);

    /// <summary>
    /// Without reference categories the product CRUD cannot be exercised at all: the category
    /// of a product is mandatory and no port creates one. The seed is a hard dependency of the
    /// deliverable, so it is asserted rather than assumed.
    /// </summary>
    [Fact]
    public async Task Migrating_leaves_the_reference_categories_in_place()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        var names = await context.Categories.Select(category => category.Name).ToListAsync();

        names.Should().Contain(["General", "Herramientas", "Electricidad", "Fontanería", "Pinturas"]);
    }

    [Fact]
    public async Task Migrating_twice_is_harmless()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync();

        var count = await context.Categories.CountAsync();

        count.Should().Be(5);
    }
}
