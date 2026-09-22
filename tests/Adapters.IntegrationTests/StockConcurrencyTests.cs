using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SimpleStockFlow.Adapters.Persistence;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Adapters.IntegrationTests;

/// <summary>
/// Product.Withdraw keeps stock non-negative in memory, which says nothing about two writers
/// that read the same row. This is the window ADR-002 closes, so it is exercised against a
/// real engine through the registered adapter rather than against a double.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StockConcurrencyTests
{
    private readonly PostgresFixture _postgres;

    public StockConcurrencyTests(PostgresFixture postgres) => _postgres = postgres;

    // Resolved from the container, so the test also proves the registration is the one shipped.
    private ServiceProvider NewContainer() =>
        new ServiceCollection()
            .AddPersistenceAdapter(_postgres.ConnectionString)
            .BuildServiceProvider();

    private static async Task<Guid> ASeededProductAsync(IServiceProvider services, int stock)
    {
        var context = services.GetRequiredService<SalesDbContext>();
        await context.Database.MigrateAsync();

        var categoryId = await context.Categories.Select(category => category.Id).FirstAsync();
        var product = Product.Create($"Rotomartillo {Guid.NewGuid():N}", new Money(320m), stock, categoryId);

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product.Id;
    }

    /// <summary>
    /// The losing writer must see an application-level conflict. If the ORM's own exception
    /// surfaced here instead, the dependency rule of the hexagon would already be broken.
    /// </summary>
    [Fact]
    public async Task The_second_writer_over_a_stale_product_gets_an_application_level_conflict()
    {
        await using var services = NewContainer();
        Guid productId;

        using (var seeding = services.CreateScope())
            productId = await ASeededProductAsync(seeding.ServiceProvider, stock: 1);

        using var first = services.CreateScope();
        using var second = services.CreateScope();

        // Both read before either writes: this is the window, not a contrived one.
        var readByFirst = await first.ServiceProvider.GetRequiredService<SalesDbContext>()
            .Products.SingleAsync(product => product.Id == productId);
        var readBySecond = await second.ServiceProvider.GetRequiredService<SalesDbContext>()
            .Products.SingleAsync(product => product.Id == productId);

        readByFirst.Withdraw(new Quantity(1));
        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();

        // The domain is satisfied on this copy too: its stock was still 1 when it was read.
        readBySecond.Withdraw(new Quantity(1));
        var losing = () => second.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();

        await losing.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    /// <summary>
    /// The quantities differ on purpose. With equal ones a lost update lands on the same stock
    /// as a correct run and the test proves nothing; here the stale writer would store 2 units
    /// that were already sold, so only a rejected write leaves 0.
    /// </summary>
    [Fact]
    public async Task A_lost_update_cannot_resurrect_stock_that_was_already_sold()
    {
        await using var services = NewContainer();
        Guid productId;

        using (var seeding = services.CreateScope())
            productId = await ASeededProductAsync(services: seeding.ServiceProvider, stock: 5);

        using var first = services.CreateScope();
        using var second = services.CreateScope();

        var readByFirst = await first.ServiceProvider.GetRequiredService<SalesDbContext>()
            .Products.SingleAsync(product => product.Id == productId);
        var readBySecond = await second.ServiceProvider.GetRequiredService<SalesDbContext>()
            .Products.SingleAsync(product => product.Id == productId);

        readByFirst.Withdraw(new Quantity(5));
        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();

        readBySecond.Withdraw(new Quantity(3));
        try
        {
            await second.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }
        catch (ConcurrencyConflictException)
        {
            // Expected. What this test asserts is the stock left behind, below.
        }

        using var audit = services.CreateScope();
        var stored = await audit.ServiceProvider.GetRequiredService<SalesDbContext>()
            .Products.SingleAsync(product => product.Id == productId);

        stored.Stock.Should().Be(0);
    }

    /// <summary>
    /// The optimistic token guards the path through the adapter. This asserts the barrier
    /// underneath it: if anything ever writes stock directly, ADR-002 wants the engine to
    /// refuse loudly rather than let a negative stock exist and be discovered later.
    /// </summary>
    [Fact]
    public async Task The_engine_refuses_a_negative_stock_written_around_the_adapter()
    {
        await using var services = NewContainer();

        using var scope = services.CreateScope();
        var productId = await ASeededProductAsync(scope.ServiceProvider, stock: 1);
        var context = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var writingBehindTheAdapter = () => context.Database.ExecuteSqlRawAsync(
            "update sales.product set stock = -1 where id = {0}", productId);

        await writingBehindTheAdapter.Should().ThrowAsync<PostgresException>();
    }
}
