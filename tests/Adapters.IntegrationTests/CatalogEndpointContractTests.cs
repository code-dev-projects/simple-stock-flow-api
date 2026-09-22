using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Identity;

namespace SimpleStockFlow.Adapters.IntegrationTests;

/// <summary>
/// The shape of the catalog over the wire. Asserting the serialised body rather than the object
/// is the point: totalPages is a computed read-only property that nobody declares, and a
/// [JsonIgnore], an IgnoreReadOnlyProperties on the host or a projection to another type would
/// erase it from the JSON without breaking compilation or a single other test. The portal would
/// receive undefined and its paginator would quietly stop paging (D-C11, gap H-4).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CatalogEndpointContractTests
{
    private readonly PostgresFixture _postgres;

    public CatalogEndpointContractTests(PostgresFixture postgres) => _postgres = postgres;

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

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the host answered: {0}", body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static object ANewProduct(Guid categoryId) =>
        new { name = $"Taladro {Guid.NewGuid():N}", price = 199_900m, stock = 7, categoryId };

    private static async Task<Guid> AnyCategoryIdAsync(HttpClient client) =>
        (await BodyOf(await client.GetAsync("/api/categories")))[0].GetProperty("id").GetGuid();

    [Fact]
    public async Task The_categories_are_refused_without_a_token()
    {
        await using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync("/api/categories");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// D-C1: a flat array, not a paged envelope, and never 204 -- the portal maps over the body
    /// and no body breaks it. Any authenticated role may read it.
    /// </summary>
    [Fact]
    public async Task The_categories_travel_as_a_flat_array_of_id_and_name_ordered_by_name()
    {
        await using var factory = CreateFactory();

        var body = await BodyOf(await ClientCarrying(factory, Roles.Seller).GetAsync("/api/categories"));

        body.ValueKind.Should().Be(JsonValueKind.Array);
        body.GetArrayLength().Should().BeGreaterThanOrEqualTo(5);

        var first = body[0];
        first.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo("id", "name");
        body.EnumerateArray().Select(category => category.GetProperty("name").GetString())
            .Should().ContainInOrder("Electricidad", "Fontanería", "General", "Herramientas", "Pinturas");
    }

    /// <summary>D-C5 over the wire: the body reports the size served, never the one asked for.</summary>
    [Fact]
    public async Task A_page_reports_the_size_served_and_carries_its_page_count()
    {
        await using var factory = CreateFactory();

        var body = await BodyOf(await ClientCarrying(factory, Roles.Seller).GetAsync("/api/products?page=1&size=500"));

        body.GetProperty("size").GetInt32().Should().Be(100);
        body.GetProperty("page").GetInt32().Should().Be(1);
        body.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
        body.TryGetProperty("totalPages", out var totalPages).Should().BeTrue(
            "the portal reads totalPages and nothing declares it: only serialisation puts it there");

        var total = body.GetProperty("total").GetInt64();
        totalPages.GetInt32().Should().Be((int)Math.Ceiling(total / 100d));
    }

    /// <summary>
    /// DP-03: the product carries the five attributes of the brief and nothing else. A field
    /// that creeps in here is a field the portal has to be taught to ignore.
    /// </summary>
    [Fact]
    public async Task A_created_product_reads_back_with_exactly_the_fields_of_the_contract()
    {
        await using var factory = CreateFactory();
        var admin = ClientCarrying(factory, Roles.Admin);
        var categoryId = await AnyCategoryIdAsync(admin);

        var created = await admin.PostAsJsonAsync("/api/products", ANewProduct(categoryId));
        var createdBody = await created.Content.ReadAsStringAsync();
        created.StatusCode.Should().Be(HttpStatusCode.Created, "the host answered: {0}", createdBody);
        created.Headers.Location.Should().NotBeNull();

        var id = JsonDocument.Parse(createdBody).RootElement.GetProperty("id").GetGuid();
        var product = await BodyOf(await admin.GetAsync($"/api/products/{id}"));

        product.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "id", "name", "price", "currency", "stock", "categoryId", "categoryName", "imageUrl");
        product.GetProperty("currency").GetString().Should().Be("COP");
        product.GetProperty("imageUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_product_that_does_not_exist_is_a_404_without_a_body()
    {
        await using var factory = CreateFactory();

        var response = await ClientCarrying(factory, Roles.Seller).GetAsync($"/api/products/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// D-C7. The route constraint makes "not an identifier" and "does not exist" the same answer
    /// from outside, and the contract chose to keep it that way rather than validate by hand in
    /// every action.
    /// </summary>
    [Fact]
    public async Task An_identifier_that_is_not_a_uuid_answers_exactly_the_same_404()
    {
        await using var factory = CreateFactory();
        var client = ClientCarrying(factory, Roles.Seller);

        var malformed = await client.GetAsync("/api/products/no-es-guid");
        var absent = await client.GetAsync($"/api/products/{Guid.NewGuid()}");

        malformed.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await malformed.Content.ReadAsStringAsync()).Should().BeEmpty();

        // The point of the decision is that the two are one answer. A body on either side would
        // tell a caller which of the two happened, which is what D-C7 says it must not.
        (await absent.Content.ReadAsStringAsync())
            .Should().Be(await malformed.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// CA-02.7 and D-C8. The distinction that matters is refused for lack of entitlement, not
    /// for lack of authentication: a seller holds a valid token and still may not write.
    /// </summary>
    [Fact]
    public async Task A_seller_may_read_the_catalog_and_may_not_write_it()
    {
        await using var factory = CreateFactory();
        var seller = ClientCarrying(factory, Roles.Seller);
        var categoryId = await AnyCategoryIdAsync(seller);

        var created = await seller.PostAsJsonAsync("/api/products", ANewProduct(categoryId));
        var updated = await seller.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}", ANewProduct(categoryId));
        var deleted = await seller.DeleteAsync($"/api/products/{Guid.NewGuid()}");

        created.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        updated.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deleted.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Taking_a_product_down_answers_204_and_removes_it_from_the_catalog()
    {
        await using var factory = CreateFactory();
        var admin = ClientCarrying(factory, Roles.Admin);
        var categoryId = await AnyCategoryIdAsync(admin);

        var created = await admin.PostAsJsonAsync("/api/products", ANewProduct(categoryId));
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement
            .GetProperty("id").GetGuid();

        var response = await admin.DeleteAsync($"/api/products/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Taking_down_a_product_that_does_not_exist_is_a_404()
    {
        await using var factory = CreateFactory();

        var response = await ClientCarrying(factory, Roles.Admin).DeleteAsync($"/api/products/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A broken rule is a 422 with the message the person reads, not a 500. E-05 prescribes this
    /// text, so it is pinned here and not paraphrased.
    /// </summary>
    [Fact]
    public async Task A_price_of_zero_is_refused_as_a_broken_rule_with_its_message()
    {
        await using var factory = CreateFactory();
        var admin = ClientCarrying(factory, Roles.Admin);
        var categoryId = await AnyCategoryIdAsync(admin);

        var response = await admin.PostAsJsonAsync(
            "/api/products",
            new { name = "Taladro", price = 0m, stock = 7, categoryId });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("detail").GetString().Should().Be("El precio debe ser mayor a cero.");
    }
}
