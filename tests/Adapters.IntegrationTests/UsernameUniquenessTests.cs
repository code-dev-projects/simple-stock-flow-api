using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimpleStockFlow.Adapters.Persistence;
using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Application.Services;
using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.Identity;

namespace SimpleStockFlow.Adapters.IntegrationTests;

/// <summary>
/// CA-07.6 says two users cannot share a username "not even if they register at the same
/// time". The service checks before inserting, and that check is worthless on its own: both
/// writers read "free" before either writes. Only the engine closes that window, so the test
/// puts both writers past the check on purpose — a test that lets the check do the work would
/// pass without the unique index and prove nothing.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UsernameUniquenessTests
{
    private readonly PostgresFixture _postgres;

    public UsernameUniquenessTests(PostgresFixture postgres) => _postgres = postgres;

    private ServiceProvider NewContainer() =>
        new ServiceCollection()
            .AddPersistenceAdapter(_postgres.ConnectionString)
            .BuildServiceProvider();

    private static AuthenticationService AuthenticationOver(IServiceProvider services) =>
        new(services.GetRequiredService<IUserRepository>(),
            new PassThroughHasher(),
            new UnusedTokenGenerator(),
            services.GetRequiredService<IUnitOfWork>());

    [Fact]
    public async Task Two_writers_that_both_saw_the_username_free_still_leave_a_single_row()
    {
        var username = $"ana{Guid.NewGuid():N}";

        await using var first = NewContainer();
        await using var second = NewContainer();
        await first.GetRequiredService<SalesDbContext>().Database.MigrateAsync();

        // Both read before either writes: this is the window, reproduced rather than hoped for.
        (await first.GetRequiredService<IUserRepository>().FindByUsernameAsync(username)).Should().BeNull();
        (await second.GetRequiredService<IUserRepository>().FindByUsernameAsync(username)).Should().BeNull();

        var outcomes = await Task.WhenAll(
            CommitUser(first, username),
            CommitUser(second, username));

        outcomes.Count(failure => failure is null).Should().Be(1, "exactly one writer may win");

        await using var reader = NewContainer();
        var rows = await reader.GetRequiredService<SalesDbContext>()
            .Set<User>()
            .CountAsync(user => user.Username == username);

        rows.Should().Be(1);
    }

    [Fact]
    public async Task The_writer_that_loses_the_race_gets_a_business_rule_not_a_database_error()
    {
        var username = $"ana{Guid.NewGuid():N}";

        await using var winner = NewContainer();
        await using var loser = NewContainer();
        await winner.GetRequiredService<SalesDbContext>().Database.MigrateAsync();

        // The loser reads first, so its check says "free" — and stays wrong from here on.
        (await loser.GetRequiredService<IUserRepository>().FindByUsernameAsync(username)).Should().BeNull();
        (await CommitUser(winner, username)).Should().BeNull();

        var failure = await CommitUser(loser, username);

        // A DbUpdateException reaching the caller would surface as a 500 and would put an ORM
        // type in the application's catch clauses: the dependency rule of the hexagon, broken.
        failure.Should().BeOfType<DomainException>();
    }

    [Fact]
    public async Task Registering_a_username_that_already_exists_is_refused_before_touching_the_engine()
    {
        var username = $"ana{Guid.NewGuid():N}";

        await using var services = NewContainer();
        await services.GetRequiredService<SalesDbContext>().Database.MigrateAsync();

        await AuthenticationOver(services).RegisterAsync(new Credentials(username, "secreta"), Roles.Seller);

        var failure = await Record.ExceptionAsync(() =>
            AuthenticationOver(NewContainer()).RegisterAsync(new Credentials(username, "secreta"), Roles.Seller));

        failure.Should().BeOfType<DomainException>();
    }

    /// <summary>
    /// Adds and commits through the registered adapter, skipping the service's prior check:
    /// the point of these tests is what the engine does once the check has already passed.
    /// </summary>
    private static async Task<Exception?> CommitUser(IServiceProvider services, string username)
    {
        try
        {
            await services.GetRequiredService<IUserRepository>()
                .AddAsync(User.Create(username, "hashed:secreta", Roles.Seller));
            await services.GetRequiredService<IUnitOfWork>().CommitAsync();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class PassThroughHasher : IPasswordHasher
    {
        public string DecoyHash => "decoy.decoy.decoy";
        public string Hash(string plainPassword) => $"hashed:{plainPassword}";
        public bool Verify(string plainPassword, string storedHash) => storedHash == $"hashed:{plainPassword}";
    }

    private sealed class UnusedTokenGenerator : ITokenGenerator
    {
        public (string Token, DateTimeOffset ExpiresAt) Generate(User user) =>
            throw new NotSupportedException("Registration must not mint a token.");
    }
}
