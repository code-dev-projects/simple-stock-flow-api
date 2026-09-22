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
/// The shape of HU-05 over the wire. The two range edges are the reason this suite exists: the
/// framework binds a missing DateTimeOffset to MinValue without a word, so a request that asked
/// for nothing would answer 200 with an empty page and the caller would read it as "no sales"
/// (D-C4); and it accepts 2026-06-01 and 01/06/2026 by resolving them against the machine's own
/// zone, so the same request answers differently on another host (D-C3).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SalesEndpointContractTests
{
    private readonly PostgresFixture _postgres;

    public SalesEndpointContractTests(PostgresFixture postgres) => _postgres = postgres;

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

    /// <summary>A whole day around now, so the sale the test registers falls inside it.</summary>
    private static string Today =>
        $"from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}" +
        $"&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}";

    private static async Task<Guid> ASoldProductAsync(HttpClient admin)
    {
        var categories = await BodyOf(await admin.GetAsync("/api/categories"), HttpStatusCode.OK);
        var created = await admin.PostAsJsonAsync(
            "/api/products",
            new
            {
                name = $"Taladro {Guid.NewGuid():N}",
                price = 199_900.55m,
                stock = 7,
                categoryId = categories[0].GetProperty("id").GetGuid(),
            });

        var body = await created.Content.ReadAsStringAsync();
        created.StatusCode.Should().Be(HttpStatusCode.Created, "the host answered: {0}", body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> ASaleAsync(HttpClient client, Guid productId, int quantity = 3)
    {
        var placed = await client.PostAsJsonAsync(
            "/api/sales",
            new { lines = new[] { new { productId, quantity } } });

        var body = await placed.Content.ReadAsStringAsync();
        placed.StatusCode.Should().Be(HttpStatusCode.Created, "the host answered: {0}", body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task The_sales_are_refused_without_a_token()
    {
        await using var factory = CreateFactory();

        var listed = await factory.CreateClient().GetAsync($"/api/sales?{Today}");
        var read = await factory.CreateClient().GetAsync($"/api/sales/{Guid.NewGuid()}");

        listed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        read.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// D-C4. Both edges are named, not just the first one to fail: a caller that omitted both
    /// gets told about both instead of fixing one and being refused again.
    /// </summary>
    [Fact]
    public async Task A_range_without_its_edges_is_refused_with_both_of_them_named()
    {
        await using var factory = CreateFactory();

        var body = await BodyOf(
            await ClientCarrying(factory, Roles.Seller).GetAsync("/api/sales"),
            HttpStatusCode.BadRequest);

        var errors = body.GetProperty("errors");
        errors.TryGetProperty("from", out _).Should().BeTrue();
        errors.TryGetProperty("to", out _).Should().BeTrue();
    }

    /// <summary>
    /// D-C4 again, on the edge that is the most misleading today: with only from sent, to fell
    /// to MinValue and the caller was told the final date was before the initial one -- a 422
    /// blaming a field that was never sent.
    /// </summary>
    [Fact]
    public async Task A_range_missing_only_its_final_edge_is_refused_for_that_edge()
    {
        await using var factory = CreateFactory();

        var body = await BodyOf(
            await ClientCarrying(factory, Roles.Seller).GetAsync("/api/sales?from=2026-01-01T00:00:00Z"),
            HttpStatusCode.BadRequest);

        body.GetProperty("errors").TryGetProperty("to", out _).Should().BeTrue();
    }

    /// <summary>The refused edge travels alone: the other one is always a valid instant.</summary>
    private static string RangeWith(string field, string value) =>
        field == "from"
            ? $"from={Uri.EscapeDataString(value)}&to=2026-07-01T00:00:00Z"
            : $"from=2026-01-01T00:00:00Z&to={Uri.EscapeDataString(value)}";

    /// <summary>
    /// D-C3. A bare date binds today only because the container happens to run in UTC, and a
    /// date written the European way binds as a different month altogether -- 01/06/2026 is
    /// read as the 6th of January. Both are refused before they can produce a wrong page.
    ///
    /// The last three rows are the half of D-C3 the offset alone cannot defend: they all carry
    /// an explicit offset and none of them is ISO 8601, so a reader that only looks for the
    /// offset lets them through and serves a page of a period the caller never wrote.
    /// </summary>
    [Theory]
    [InlineData("from", "2026-01-01")]
    [InlineData("from", "2026-06-01T10:30")]
    [InlineData("from", "01/06/2026")]
    [InlineData("to", "1767225600")]
    [InlineData("from", "")]
    [InlineData("from", "01/06/2026 00:00:00Z")]
    [InlineData("to", "6/1/2026 10:30:00 AM +02:00")]
    [InlineData("from", "Sat, 06 Jun 2026 00:00:00 GMT")]
    public async Task A_date_that_is_not_ISO_8601_with_an_explicit_offset_is_refused(
        string field,
        string value)
    {
        await using var factory = CreateFactory();

        var body = await BodyOf(
            await ClientCarrying(factory, Roles.Seller).GetAsync($"/api/sales?{RangeWith(field, value)}"),
            HttpStatusCode.BadRequest);

        body.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    /// <summary>Both spellings of an explicit offset are the accepted ones, and they are equal.</summary>
    [Fact]
    public async Task An_explicit_offset_is_accepted_in_both_of_its_spellings()
    {
        await using var factory = CreateFactory();
        var client = ClientCarrying(factory, Roles.Seller);

        var zulu = await client.GetAsync("/api/sales?from=2026-01-01T00:00:00Z&to=2026-02-01T00:00:00Z");
        var offset = await client.GetAsync(
            "/api/sales?from=" + Uri.EscapeDataString("2026-01-01T02:00:00+02:00") +
            "&to=" + Uri.EscapeDataString("2026-02-01T02:00:00+02:00"));

        zulu.StatusCode.Should().Be(HttpStatusCode.OK);
        offset.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// The other half of the same decision: tightening the reader must not start refusing the
    /// fractional second that every ToString("O") writes, which is how the portal's own dates
    /// and this suite's own ranges travel.
    /// </summary>
    [Theory]
    [InlineData("2026-01-01T00:00:00.0000000Z")]
    [InlineData("2026-01-01T02:00:00.123-05:00")]
    public async Task An_edge_with_a_fractional_second_is_accepted(string value)
    {
        await using var factory = CreateFactory();

        var answered = await ClientCarrying(factory, Roles.Seller)
            .GetAsync($"/api/sales?{RangeWith("from", value)}");

        answered.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>CA-05.3 and E-11: a broken rule, with the message the person reads.</summary>
    [Fact]
    public async Task A_final_edge_before_the_initial_one_is_refused_as_a_broken_rule()
    {
        await using var factory = CreateFactory();

        var body = await BodyOf(
            await ClientCarrying(factory, Roles.Seller)
                .GetAsync("/api/sales?from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z"),
            HttpStatusCode.UnprocessableEntity);

        body.GetProperty("detail").GetString().Should().Be("La fecha final no puede ser anterior a la inicial.");
    }

    /// <summary>
    /// D-C5 and D-C11 on the second paged collection. totalPages is a computed read-only
    /// property that nothing declares: a [JsonIgnore] or a projection would erase it from the
    /// body without breaking compilation, and the portal's paginator would quietly stop paging.
    /// </summary>
    [Fact]
    public async Task A_page_of_sales_reports_the_size_served_and_carries_its_page_count()
    {
        await using var factory = CreateFactory();

        var body = await BodyOf(
            await ClientCarrying(factory, Roles.Seller).GetAsync($"/api/sales?{Today}&page=1&size=500"),
            HttpStatusCode.OK);

        body.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("items", "page", "size", "total", "totalPages");
        body.GetProperty("size").GetInt32().Should().Be(100);
        body.GetProperty("page").GetInt32().Should().Be(1);
        body.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);

        var total = body.GetProperty("total").GetInt64();
        body.GetProperty("totalPages").GetInt32().Should().Be((int)Math.Ceiling(total / 100d));
    }

    /// <summary>
    /// E-11 and E-12 field by field. A field that creeps in here is a field the portal has to
    /// be taught to ignore, and the line carries no currency of its own on purpose: the front
    /// reads the sale's, and a second copy would be a second source for the same value.
    /// </summary>
    [Fact]
    public async Task A_sale_reads_back_with_exactly_the_fields_of_the_contract()
    {
        await using var factory = CreateFactory();
        var admin = ClientCarrying(factory, Roles.Admin);
        var productId = await ASoldProductAsync(admin);
        var saleId = await ASaleAsync(admin, productId);

        var sale = await BodyOf(await admin.GetAsync($"/api/sales/{saleId}"), HttpStatusCode.OK);

        sale.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("id", "soldAt", "soldBy", "total", "currency", "items");
        sale.GetProperty("currency").GetString().Should().Be("COP");
        sale.GetProperty("soldBy").GetString().Should().NotBeNullOrWhiteSpace();
        sale.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);

        var line = sale.GetProperty("items")[0];
        line.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("productId", "productName", "quantity", "unitPrice", "subtotal");
        line.GetProperty("productId").GetGuid().Should().Be(productId);
        line.GetProperty("quantity").GetInt32().Should().Be(3);
        line.GetProperty("unitPrice").GetDecimal().Should().Be(199_900.55m);

        // CA-05.4 over the wire, against the lines that travelled in this same body and never
        // against a number written here: a literal would keep agreeing with a total that had
        // stopped being computed from the lines.
        sale.GetProperty("total").GetDecimal().Should().Be(
            sale.GetProperty("items").EnumerateArray().Sum(item => item.GetProperty("subtotal").GetDecimal()));
    }

    /// <summary>The same sale, reached through the range: one shape, two doors (E-11, E-12).</summary>
    [Fact]
    public async Task The_sale_that_the_range_serves_is_the_same_one_its_identifier_serves()
    {
        await using var factory = CreateFactory();
        var admin = ClientCarrying(factory, Roles.Admin);
        var saleId = await ASaleAsync(admin, await ASoldProductAsync(admin));

        var page = await BodyOf(await admin.GetAsync($"/api/sales?{Today}&size=100"), HttpStatusCode.OK);
        var byId = await BodyOf(await admin.GetAsync($"/api/sales/{saleId}"), HttpStatusCode.OK);

        var listed = page.GetProperty("items").EnumerateArray()
            .Single(sale => sale.GetProperty("id").GetGuid() == saleId);

        listed.GetRawText().Should().Be(byId.GetRawText());
    }

    /// <summary>
    /// D-C7: the route constraint makes "not an identifier" and "does not exist" one answer,
    /// and E-12 keeps both without a body.
    /// </summary>
    [Fact]
    public async Task A_sale_that_does_not_exist_and_an_identifier_that_is_not_a_uuid_are_the_same_404()
    {
        await using var factory = CreateFactory();
        var client = ClientCarrying(factory, Roles.Seller);

        var absent = await client.GetAsync($"/api/sales/{Guid.NewGuid()}");
        var malformed = await client.GetAsync("/api/sales/no-es-guid");

        absent.StatusCode.Should().Be(HttpStatusCode.NotFound);
        malformed.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await absent.Content.ReadAsStringAsync()).Should().BeEmpty();
        (await malformed.Content.ReadAsStringAsync()).Should().BeEmpty();
    }
}
