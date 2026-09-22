using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimpleStockFlow.Adapters.Persistence;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Identity;

namespace SimpleStockFlow.Adapters.IntegrationTests;

/// <summary>
/// RN-08 over the wire, which had no test at all: taking a product down is an UPDATE and never
/// a DELETE. Every case here starts by selling the product, because that is the one the
/// physical delete cannot serve -- the restrictive foreign key from sale_item refuses it -- and
/// because "it was taken down" and "it was deleted and the query no longer fails" are
/// indistinguishable from the catalogue and radically different for the history.
/// The row is counted with SQL rather than through the context on purpose: reading it back with
/// EF would ask the very mapping under test whether the row is there.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProductSoftDeleteTests
{
    private readonly PostgresFixture _postgres;

    public ProductSoftDeleteTests(PostgresFixture postgres) => _postgres = postgres;

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

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, "the host answered: {0}", body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>
    /// Other suites share this database, so the catalogue is never counted whole: the marker
    /// is what scopes a listing to the rows this test wrote.
    /// </summary>
    private static string NewMarker() => $"mk{Guid.NewGuid():N}";

    private static async Task<Guid> AProductAsync(HttpClient admin, string marker)
    {
        var categories = await BodyOf(await admin.GetAsync("/api/categories"), HttpStatusCode.OK);
        var created = await admin.PostAsJsonAsync(
            "/api/products",
            new
            {
                name = $"Taladro {marker}",
                price = 199_900.55m,
                stock = 7,
                categoryId = categories[0].GetProperty("id").GetGuid(),
            });

        return (await BodyOf(created, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> ASaleOfAsync(HttpClient client, Guid productId, int quantity = 3)
    {
        var placed = await client.PostAsJsonAsync(
            "/api/sales",
            new { lines = new[] { new { productId, quantity } } });

        return (await BodyOf(placed, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    }

    private static async Task<int> RowsInProductWithIdAsync(WebApplicationFactory<Program> factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        return await context.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM sales.product WHERE id = {id}")
            .SingleAsync();
    }

    /// <summary>
    /// CA-02.5 and RN-08. The assertion that carries the rule is the row count: a catalogue that
    /// no longer lists the product proves nothing on its own, because a deleted row does not get
    /// listed either.
    /// </summary>
    [Fact]
    public async Task Taking_down_a_product_that_was_sold_answers_204_and_leaves_its_row_in_place()
    {
        await using var factory = CreateFactory();
        var admin = ClientCarrying(factory, Roles.Admin);
        var productId = await AProductAsync(admin, NewMarker());
        await ASaleOfAsync(admin, productId);

        var response = await admin.DeleteAsync($"/api/products/{productId}");

        response.StatusCode.Should().Be(
            HttpStatusCode.NoContent,
            "the host answered: {0}",
            await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
        (await RowsInProductWithIdAsync(factory, productId)).Should().Be(1);
    }

    /// <summary>
    /// CA-02.6, and the reason the filter has to be global: this is the load that happens before
    /// selling, not the catalogue query, and a filter written query by query is exactly the one
    /// that gets forgotten here. E-10 prescribes the wording, so it is pinned and not paraphrased.
    /// </summary>
    [Fact]
    public async Task A_product_that_was_taken_down_can_no_longer_be_sold()
    {
        await using var factory = CreateFactory();
        var admin = ClientCarrying(factory, Roles.Admin);
        var productId = await AProductAsync(admin, NewMarker());
        await ASaleOfAsync(admin, productId);

        var taken = await admin.DeleteAsync($"/api/products/{productId}");
        taken.StatusCode.Should().Be(
            HttpStatusCode.NoContent,
            "the host answered: {0}",
            await taken.Content.ReadAsStringAsync());

        var refused = await admin.PostAsJsonAsync(
            "/api/sales",
            new { lines = new[] { new { productId, quantity = 1 } } });

        var problem = await BodyOf(refused, HttpStatusCode.UnprocessableEntity);
        problem.GetProperty("detail").GetString().Should().Be($"El producto {productId} no existe.");
    }

    /// <summary>
    /// CA-02.5. The line keeps the name and the price it froze, which is what tells apart a row
    /// that survived from a report that merely does not crash.
    /// </summary>
    [Fact]
    public async Task The_sale_of_a_product_taken_down_is_still_readable_with_its_frozen_line()
    {
        await using var factory = CreateFactory();
        var admin = ClientCarrying(factory, Roles.Admin);
        var marker = NewMarker();
        var productId = await AProductAsync(admin, marker);
        var saleId = await ASaleOfAsync(admin, productId);

        var taken = await admin.DeleteAsync($"/api/products/{productId}");
        taken.StatusCode.Should().Be(
            HttpStatusCode.NoContent,
            "the host answered: {0}",
            await taken.Content.ReadAsStringAsync());

        var sale = await BodyOf(await admin.GetAsync($"/api/sales/{saleId}"), HttpStatusCode.OK);

        var line = sale.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("productId").GetGuid() == productId);
        line.GetProperty("productName").GetString().Should().Be($"Taladro {marker}");
        line.GetProperty("unitPrice").GetDecimal().Should().Be(199_900.55m);
        line.GetProperty("quantity").GetInt32().Should().Be(3);
    }

    /// <summary>
    /// E-07. The aggregate does not know the mark -- it is a shadow property -- so it cannot
    /// tell the two calls apart, and the contract chose to answer both the same rather than
    /// invent a state the domain does not hold.
    /// </summary>
    [Fact]
    public async Task Taking_the_same_product_down_twice_answers_204_both_times()
    {
        await using var factory = CreateFactory();
        var admin = ClientCarrying(factory, Roles.Admin);
        var productId = await AProductAsync(admin, NewMarker());
        await ASaleOfAsync(admin, productId);

        var first = await admin.DeleteAsync($"/api/products/{productId}");
        var second = await admin.DeleteAsync($"/api/products/{productId}");

        first.StatusCode.Should().Be(
            HttpStatusCode.NoContent,
            "the host answered: {0}",
            await first.Content.ReadAsStringAsync());
        second.StatusCode.Should().Be(
            HttpStatusCode.NoContent,
            "the host answered: {0}",
            await second.Content.ReadAsStringAsync());
        (await RowsInProductWithIdAsync(factory, productId)).Should().Be(1);
    }

    /// <summary>
    /// E-04: the load by identifier is the one operation of the three that does NOT filter, so
    /// the detail of an old sale can still resolve its product.
    /// </summary>
    [Fact]
    public async Task A_product_taken_down_is_still_served_by_its_identifier()
    {
        await using var factory = CreateFactory();
        var admin = ClientCarrying(factory, Roles.Admin);
        var marker = NewMarker();
        var productId = await AProductAsync(admin, marker);
        await ASaleOfAsync(admin, productId);

        var taken = await admin.DeleteAsync($"/api/products/{productId}");
        taken.StatusCode.Should().Be(
            HttpStatusCode.NoContent,
            "the host answered: {0}",
            await taken.Content.ReadAsStringAsync());

        var product = await BodyOf(await admin.GetAsync($"/api/products/{productId}"), HttpStatusCode.OK);

        product.GetProperty("id").GetGuid().Should().Be(productId);
        product.GetProperty("name").GetString().Should().Be($"Taladro {marker}");
    }

    /// <summary>CA-01.4: and the catalogue, which is the other side of the same filter, no longer offers it.</summary>
    [Fact]
    public async Task A_product_taken_down_is_gone_from_the_catalog()
    {
        await using var factory = CreateFactory();
        var admin = ClientCarrying(factory, Roles.Admin);
        var marker = NewMarker();
        var productId = await AProductAsync(admin, marker);
        await ASaleOfAsync(admin, productId);

        var before = await BodyOf(await admin.GetAsync($"/api/products?search={marker}"), HttpStatusCode.OK);
        before.GetProperty("total").GetInt64().Should().Be(1);

        var taken = await admin.DeleteAsync($"/api/products/{productId}");
        taken.StatusCode.Should().Be(
            HttpStatusCode.NoContent,
            "the host answered: {0}",
            await taken.Content.ReadAsStringAsync());

        var after = await BodyOf(await admin.GetAsync($"/api/products?search={marker}"), HttpStatusCode.OK);

        after.GetProperty("total").GetInt64().Should().Be(0);
        after.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// data-model.md section 3 defines the column with no margin: timestamptz, nullable, and no
    /// default like every other column in this schema. The nullability is not cosmetic -- a
    /// non-null column with a sentinel could not serve as the predicate of the partial indexes
    /// T-13 adds -- and a default would be a second source of truth for a value the domain owns.
    /// </summary>
    [Fact]
    public async Task The_mark_of_the_takedown_is_a_nullable_timestamptz_without_a_default()
    {
        await using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var definition = await context.Database
            .SqlQuery<string>(
                $"""
                 SELECT data_type || '|' || is_nullable || '|' || coalesce(column_default, 'none') AS "Value"
                 FROM information_schema.columns
                 WHERE table_schema = 'sales' AND table_name = 'product' AND column_name = 'deleted_at'
                 """)
            .ToListAsync();

        definition.Should().ContainSingle().Which.Should().Be("timestamp with time zone|YES|none");
    }
}
