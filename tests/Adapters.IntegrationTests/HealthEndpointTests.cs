using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SimpleStockFlow.Adapters.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class HealthEndpointTests
{
    private readonly PostgresFixture _postgres;

    public HealthEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>
    /// Settings travel as environment variables, not as in-memory configuration: under the
    /// minimal hosting model Program.cs runs and registers the DbContext before the factory
    /// can add its own sources, so anything added there arrives too late and the host falls
    /// back to the appsettings connection string.
    /// </summary>
    private WebApplicationFactory<Program> CreateFactory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgres.ConnectionString);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "test-signing-key-with-at-least-32-chars!!");
        Environment.SetEnvironmentVariable(
            "Storage__RootPath",
            Path.Combine(Path.GetTempPath(), "simple-stock-flow-tests"));

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    /// <summary>Smoke test of the whole hexagon: a real host with every port resolved.</summary>
    [Fact]
    public async Task The_host_starts_with_every_port_resolved()
    {
        await using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync("/health");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the host answered with: {0}", body);
    }

    [Fact]
    public async Task Business_endpoints_demand_a_token()
    {
        await using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync("/api/products");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
