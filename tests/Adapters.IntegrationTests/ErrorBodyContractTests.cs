using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Identity;

namespace SimpleStockFlow.Adapters.IntegrationTests;

/// <summary>
/// What a failing request is allowed to say back. Decision D-C9 asks every 400 to carry a
/// <c>detail</c>, and gap H-2 is that it does not; measuring the body rather than reading the
/// configuration is the only way to know, because the body that ships is the framework's default
/// and no line of this repository declares it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ErrorBodyContractTests
{
    /// <summary>
    /// Fragments that only exist inside the server. A body carrying any of them is handing a
    /// stranger the shape of the code: the request type's full name, the JSON path it choked on
    /// and the byte it stopped at.
    /// </summary>
    private static readonly string[] InternalGiveaways =
        ["SimpleStockFlow.", "LineNumber", "BytePositionInLine", "System."];

    private readonly PostgresFixture _postgres;

    public ErrorBodyContractTests(PostgresFixture postgres) => _postgres = postgres;

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

    [Fact]
    public async Task A_type_mismatch_answers_400_with_a_detail_whoever_reads_it_can_act_on()
    {
        using var factory = CreateFactory();
        var client = ClientCarrying(factory, Roles.Admin);

        var response = await client.PostAsync(
            "/api/products",
            new StringContent(
                """{"name":"Taladro","price":"abc","stock":1,"categoryId":"11111111-1111-4111-8111-111111111111"}""",
                System.Text.Encoding.UTF8,
                "application/json"));

        var body = await response.Content.ReadAsStringAsync();
        ((int)response.StatusCode).Should().Be(400, "the body was: {0}", body);

        var root = System.Text.Json.JsonDocument.Parse(body).RootElement;

        // D-C9. Without it the portal has nothing to show but a status code, which is how the
        // user ended up reading "Ocurrió un error inesperado" for a price that was not a number.
        root.TryGetProperty("detail", out var detail).Should().BeTrue("D-C9 requires a detail on every 400");
        detail.GetString().Should().NotBeNullOrWhiteSpace();

        // Article XI: this sentence is read by whoever uses the system, so it is in Spanish.
        // Asserted as "no English from the framework survived" and not as "contains an accent":
        // the first version demanded an accented letter and failed on "Revisa estos campos: price.",
        // which is Spanish and has none. That was the check being wrong, not the code.
        foreach (var english in new[] { "The ", " field", "required", "could not be", "occurred" })
        {
            detail.GetString().Should().NotContain(
                english, "the framework's English must not survive into what a person reads");
        }

        // §2.2 documents traceId and the framework used to add it for free. Replacing the body
        // dropped it, which took away the only thread between a complaint and a log line.
        root.TryGetProperty("traceId", out var trace).Should().BeTrue("§2.2 declares traceId");
        trace.GetString().Should().NotBeNullOrWhiteSpace();

        // The framework invents this entry for the bound parameter, and the portal already carries
        // code to throw it away. An error body that needs the client to filter it is not a contract.
        root.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.TryGetProperty("request", out _)
            .Should().BeFalse("'request' is the name of a C# parameter, not a field the caller sent");
    }

    /// <summary>
    /// The one that matters for publishing: not "is the configuration right" but "does anything
    /// come back that should not". Each request breaks the server a different way -- one fails to
    /// bind, one is not valid JSON at all, and the last one reaches the engine and is refused by a
    /// column width, which is the only path in this system that still ends in a 500.
    /// </summary>
    [Fact]
    public async Task No_hostile_request_leaks_what_the_server_is_made_of()
    {
        using var factory = CreateFactory();
        var client = ClientCarrying(factory, Roles.Admin);

        var hostile = new (string Name, HttpContent Content)[]
        {
            ("a price that is not a number",
                JsonContent.Create(new { name = "Taladro", price = "abc", stock = 1, categoryId = Guid.NewGuid() })),
            ("a body that is not JSON",
                new StringContent("""{"name":""", System.Text.Encoding.UTF8, "application/json")),
            ("a name wider than the column",
                JsonContent.Create(new { name = new string('N', 300), price = 1m, stock = 1, categoryId = Guid.NewGuid() })),
        };

        foreach (var (name, content) in hostile)
        {
            var body = await (await client.PostAsync("/api/products", content)).Content.ReadAsStringAsync();

            foreach (var giveaway in InternalGiveaways)
            {
                body.Should().NotContain(
                    giveaway,
                    "the answer to {0} must not describe the server's insides", name);
            }
        }
    }
    /// <summary>
    /// A name wider than the column used to travel all the way to the engine and come back as a
    /// 500 with an empty body -- nothing leaked, but a person typing a long product name met a
    /// blank failure on their first day, and every document of this project declares that no
    /// endpoint answers 500. Refusing it as what it is, an oversized field, is the 400 the
    /// contract already describes in §2.2.
    /// </summary>
    [Fact]
    public async Task A_name_wider_than_the_column_is_a_400_and_not_the_engine_exploding()
    {
        using var factory = CreateFactory();
        var client = ClientCarrying(factory, Roles.Admin);

        // A real category, or the request is refused for that before the name can reach the engine.
        var categories = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/categories");
        var categoryId = categories[0].GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync(
            "/api/products",
            new { name = new string('N', 201), price = 1m, stock = 1, categoryId });

        var body = await response.Content.ReadAsStringAsync();
        ((int)response.StatusCode).Should().Be(400, "the body was: {0}", body);

        var root = System.Text.Json.JsonDocument.Parse(body).RootElement;
        // camelCase, like §1 says every field name travels, whichever check refused it: this one
        // comes from a validation attribute and reports the CLR property, the type-mismatch one
        // comes from the deserialiser and reports the JSON path.
        root.GetProperty("detail").GetString().Should().Contain("name").And.NotContain("Name");
        root.GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();

        // The boundary itself, so the limit is the column's and not one somebody rounded down.
        var atTheLimit = await client.PostAsJsonAsync(
            "/api/products",
            new { name = new string('N', 200), price = 1m, stock = 1, categoryId });
        ((int)atTheLimit.StatusCode).Should().Be(201, "200 characters is exactly what the column holds");
    }

}
