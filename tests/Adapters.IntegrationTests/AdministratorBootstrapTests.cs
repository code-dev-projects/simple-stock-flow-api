using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SimpleStockFlow.Adapters.Persistence;
using SimpleStockFlow.Adapters.Security;
using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Application.Services;
using SimpleStockFlow.Bootstrap.Composition;
using SimpleStockFlow.Domain.Identity;

namespace SimpleStockFlow.Adapters.IntegrationTests;

/// <summary>
/// CA-07.5 and article IX: the first administrator comes from the environment, never from a
/// versioned seed. Without it the system ships with no way in, because registering demands an
/// administrator token and there is none to mint it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdministratorBootstrapTests
{
    private readonly PostgresFixture _postgres;

    public AdministratorBootstrapTests(PostgresFixture postgres) => _postgres = postgres;

    private ServiceProvider NewContainer(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddPersistenceAdapter(_postgres.ConnectionString)
            .AddSecurityAdapter(options => options.SigningKey = new string('k', 48))
            .AddScoped<IAuthenticate, AuthenticationService>()
            .AddScoped<IProvisionAdministrator, AuthenticationService>()
            .BuildServiceProvider();
    }

    [Fact]
    public async Task Creates_the_administrator_declared_in_the_environment()
    {
        var username = $"jefe{Guid.NewGuid():N}";
        await using var services = NewContainer(
            ("Bootstrap:AdminUsername", username),
            ("Bootstrap:AdminPassword", "una-clave-larga-de-verdad"));
        await services.GetRequiredService<SalesDbContext>().Database.MigrateAsync();

        await services.EnsureAdministratorAsync();

        var admin = await services.GetRequiredService<IUserRepository>().FindByUsernameAsync(username);
        admin.Should().NotBeNull();
        admin!.Role.Should().Be(Roles.Admin);
        admin.PasswordHash.Should().NotContain("una-clave-larga-de-verdad", "the plain password is never stored");
    }

    [Fact]
    public async Task Running_twice_leaves_one_administrator()
    {
        var username = $"jefe{Guid.NewGuid():N}";
        await using var services = NewContainer(
            ("Bootstrap:AdminUsername", username),
            ("Bootstrap:AdminPassword", "una-clave-larga-de-verdad"));
        await services.GetRequiredService<SalesDbContext>().Database.MigrateAsync();

        await services.EnsureAdministratorAsync();
        await services.EnsureAdministratorAsync();

        var rows = await services.GetRequiredService<SalesDbContext>()
            .Set<User>()
            .CountAsync(user => user.Username == username);

        rows.Should().Be(1);
    }

    [Fact]
    public async Task Creates_nobody_when_the_environment_declares_no_administrator()
    {
        await using var services = NewContainer();
        await services.GetRequiredService<SalesDbContext>().Database.MigrateAsync();

        var before = await CountUsers(services);
        await services.EnsureAdministratorAsync();

        // Article IX: no default credentials. A system with no way in is safer than one with
        // a way in that everybody knows.
        (await CountUsers(services)).Should().Be(before);
    }

    private static Task<int> CountUsers(IServiceProvider services) =>
        services.GetRequiredService<SalesDbContext>().Set<User>().CountAsync();
}
