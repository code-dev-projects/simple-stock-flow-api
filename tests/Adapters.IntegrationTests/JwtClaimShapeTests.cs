using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Identity;

namespace SimpleStockFlow.Adapters.IntegrationTests;

/// <summary>
/// E-01 enumerates the claims the access token carries: sub, unique_name, role and jti. The
/// payload is read raw, straight off the second segment of the JWT, because every handler in
/// the stack runs the inbound claim map and would show a short "role" whether or not one was
/// ever written -- which is exactly how a previous report concluded the claim was there when
/// the wire said otherwise.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class JwtClaimShapeTests
{
    private const string LongRoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

    private readonly PostgresFixture _postgres;

    public JwtClaimShapeTests(PostgresFixture postgres) => _postgres = postgres;

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

    private static string MintToken(WebApplicationFactory<Program> factory, string username, string role)
    {
        var tokens = factory.Services.GetRequiredService<ITokenGenerator>();
        var (token, _) = tokens.Generate(User.Create(username, "hash-irrelevante", role));
        return token;
    }

    private static JsonElement PayloadOf(string token)
    {
        var segment = token.Split('.')[1];
        var padded = segment.Replace('-', '+').Replace('_', '/').PadRight((segment.Length + 3) / 4 * 4, '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(padded))).RootElement.Clone();
    }

    [Fact]
    public async Task Token_payload_carries_the_four_claims_E01_declares()
    {
        await using var factory = CreateFactory();
        var username = $"prueba{Guid.NewGuid():N}";

        var payload = PayloadOf(MintToken(factory, username, Roles.Admin));

        payload.TryGetProperty("sub", out var sub).Should().BeTrue();
        sub.GetString().Should().NotBeNullOrWhiteSpace();
        payload.GetProperty("unique_name").GetString().Should().Be(username);
        payload.GetProperty("role").GetString().Should().Be(Roles.Admin);
        payload.TryGetProperty("jti", out var jti).Should().BeTrue();
        jti.GetString().Should().NotBeNullOrWhiteSpace();

        payload.TryGetProperty(LongRoleClaim, out _).Should().BeFalse(
            "E-01 names the claim 'role'; the WS-* URI is a second spelling nobody declared");
    }

    /// <summary>
    /// Changing what the generator writes can break authorization without breaking a single
    /// assertion about the payload, so the shape is only half the test: an admin-only endpoint
    /// has to keep telling admin and seller apart with the token as it is now emitted.
    /// </summary>
    [Fact]
    public async Task Admin_only_endpoint_still_reads_the_role_from_the_emitted_token()
    {
        await using var factory = CreateFactory();

        var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(factory, $"jefa{Guid.NewGuid():N}", Roles.Admin));

        var seller = factory.CreateClient();
        seller.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(factory, $"vende{Guid.NewGuid():N}", Roles.Seller));

        var granted = await admin.PostAsJsonAsync("/api/auth/register", NewRegistration());
        var refused = await seller.PostAsJsonAsync("/api/auth/register", NewRegistration());

        granted.StatusCode.Should().Be(
            HttpStatusCode.Created,
            "the host answered: {0}",
            await granted.Content.ReadAsStringAsync());
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static object NewRegistration() =>
        new { username = $"nueva{Guid.NewGuid():N}", password = "una-clave-larga-de-verdad", role = Roles.Seller };
}
