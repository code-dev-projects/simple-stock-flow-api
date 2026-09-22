using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Identity;

namespace SimpleStockFlow.Adapters.IntegrationTests;

/// <summary>
/// Defect A-1. A class-level [AllowAnonymous] on AuthController overrode the
/// [Authorize(Roles = "admin")] that the Register action declares, because AllowAnonymous
/// always wins in ASP.NET Core: anyone reaching the port could mint an administrator. The
/// override is only observable over HTTP, so it is pinned down here and not in a unit test.
/// Also covers D-C6: register announces no Location, since /api/users/{id} does not exist.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RegisterAuthorizationTests
{
    private readonly PostgresFixture _postgres;

    public RegisterAuthorizationTests(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>
    /// Settings travel as environment variables, not as in-memory configuration: under the
    /// minimal hosting model Program.cs runs and registers the DbContext before the factory
    /// can add its own sources.
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

    /// <summary>
    /// The token is minted through the host's own generator so issuer, audience and signing
    /// key match what the host validates. No row is needed in sales.user: authorization reads
    /// the role claim, never the database.
    /// </summary>
    private static HttpClient ClientCarrying(WebApplicationFactory<Program> factory, string role)
    {
        var tokens = factory.Services.GetRequiredService<ITokenGenerator>();
        // Persisted, not just minted: since T-12 a sale points at its author with a foreign key,
        // so a token for a user the database has never heard of can read but cannot register one.
        var operator_ = User.Create($"prueba{Guid.NewGuid():N}", "hash-irrelevante", role);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IUserRepository>()
                .AddAsync(operator_).GetAwaiter().GetResult();
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
                .CommitAsync().GetAwaiter().GetResult();
        }

        var (token, _) = tokens.Generate(operator_);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object NewRegistration(string role) =>
        new { username = $"nueva{Guid.NewGuid():N}", password = "una-clave-larga-de-verdad", role };

    [Fact]
    public async Task Register_without_a_token_is_refused()
    {
        await using var factory = CreateFactory();

        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/register", NewRegistration(Roles.Admin));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "an anonymous caller must never reach the business rule; the host answered: {0}",
            body);
    }

    [Fact]
    public async Task Register_with_a_seller_token_is_refused()
    {
        await using var factory = CreateFactory();

        var response = await ClientCarrying(factory, Roles.Seller)
            .PostAsJsonAsync("/api/auth/register", NewRegistration(Roles.Admin));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "a seller is authenticated but not entitled; the host answered: {0}",
            body);
    }

    [Fact]
    public async Task Register_with_an_administrator_token_creates_the_user_without_announcing_a_location()
    {
        await using var factory = CreateFactory();

        var response = await ClientCarrying(factory, Roles.Admin)
            .PostAsJsonAsync("/api/auth/register", NewRegistration(Roles.Seller));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, "the host answered: {0}", body);

        // D-C6: a Location pointing at /api/users/{id} answers 404, and a client that follows
        // it fails far from the cause. Better to announce nothing.
        response.Headers.Location.Should().BeNull();
        response.Content.Headers.ContentLocation.Should().BeNull();
        body.Should().Contain("\"id\"");
    }

    /// <summary>
    /// Login is the one action that stays anonymous: without it the portal cannot get past its
    /// first screen. A 422 proves the request reached the business rule, so authorization let
    /// it through.
    /// </summary>
    [Fact]
    public async Task Login_stays_anonymous()
    {
        await using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login",
            new { username = $"nadie{Guid.NewGuid():N}", password = "una-clave-larga-de-verdad" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
