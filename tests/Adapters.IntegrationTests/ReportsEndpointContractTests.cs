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
/// The shape of E-13 over the wire, asserted on the JSON and not on the C# record. The field
/// names are camelCase by omission -- nothing in the project configures the serializer -- so
/// they are exactly as fragile as D-C11 says, and the empty report is the response the portal
/// opens the screen on before anybody picks a range.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportsEndpointContractTests
{
    private readonly PostgresFixture _postgres;

    public ReportsEndpointContractTests(PostgresFixture postgres) => _postgres = postgres;

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

    /// <summary>A year nobody else in this database sells in, so the report belongs to the test.</summary>
    private static (string Query, DateTimeOffset From) EmptyYear()
    {
        var from = new DateTimeOffset(2400, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return (Range(from, from.AddYears(1)), from);
    }

    private static string Range(DateTimeOffset from, DateTimeOffset to) =>
        $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";

    /// <summary>E-13: authenticated, so no token is the empty 401 of §2.3.</summary>
    [Fact]
    public async Task The_report_refuses_a_request_without_a_token()
    {
        using var factory = CreateFactory();
        var (query, _) = EmptyYear();

        var response = await factory.CreateClient().GetAsync($"/api/reports/sales?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// D-C10 and CA-06.2 on the wire, which is the only place the defect they guard against
    /// shows up: rows must be [] and currency must be the string "COP", because the portal
    /// calls currency.toUpperCase() on it and a null would take the screen down.
    /// </summary>
    [Fact]
    public async Task An_empty_report_carries_an_empty_array_and_the_currency_all_the_same()
    {
        using var factory = CreateFactory();
        var client = ClientCarrying(factory, Roles.Seller);
        var (query, _) = EmptyYear();

        var report = await BodyOf(await client.GetAsync($"/api/reports/sales?{query}"), HttpStatusCode.OK);

        report.GetProperty("rows").ValueKind.Should().Be(JsonValueKind.Array);
        report.GetProperty("rows").GetArrayLength().Should().Be(0);
        report.GetProperty("currency").GetString().Should().Be("COP");
        report.GetProperty("salesCount").GetInt32().Should().Be(0);
        report.GetProperty("grandTotal").GetDecimal().Should().Be(0m);
        report.GetProperty("from").ValueKind.Should().Be(JsonValueKind.String);
        report.GetProperty("to").ValueKind.Should().Be(JsonValueKind.String);
    }

    /// <summary>
    /// The six fields of SalesReport and the five of every row, spelled as the portal's DTO
    /// spells them. A rename on the C# side would keep compiling and keep every other test
    /// green while the portal read undefined.
    /// </summary>
    [Fact]
    public async Task A_report_with_sales_names_its_six_fields_and_the_five_of_every_row()
    {
        using var factory = CreateFactory();
        var admin = ClientCarrying(factory, Roles.Admin);

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

        var productId = (await BodyOf(created, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var placed = await admin.PostAsJsonAsync(
            "/api/sales",
            new { lines = new[] { new { productId, quantity = 3 } } });
        await BodyOf(placed, HttpStatusCode.Created);

        var query = Range(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var report = await BodyOf(await admin.GetAsync($"/api/reports/sales?{query}"), HttpStatusCode.OK);

        report.GetProperty("salesCount").GetInt32().Should().BeGreaterThan(0);
        report.GetProperty("currency").GetString().Should().Be("COP");

        var row = report.GetProperty("rows").EnumerateArray()
            .Single(candidate => candidate.GetProperty("productId").GetGuid() == productId);

        row.GetProperty("productName").ValueKind.Should().Be(JsonValueKind.String);
        row.GetProperty("categoryName").ValueKind.Should().Be(JsonValueKind.String);
        row.GetProperty("unitsSold").GetInt32().Should().Be(3);
        row.GetProperty("revenue").GetDecimal().Should().Be(599_701.65m);
    }

    /// <summary>
    /// D-C4, where its absence bites hardest. A missing edge binds to DateTimeOffset.MinValue
    /// without a word, and now that the aggregation answers, a request that asked for nothing
    /// would come back as a report with no rows -- which the caller reads as "nothing was sold"
    /// instead of "you sent no range". Both edges are named, so a caller who omitted both is
    /// not refused twice.
    /// </summary>
    [Fact]
    public async Task A_report_without_its_edges_is_refused_with_both_of_them_named()
    {
        using var factory = CreateFactory();

        var body = await BodyOf(
            await ClientCarrying(factory, Roles.Seller).GetAsync("/api/reports/sales"),
            HttpStatusCode.BadRequest);

        var errors = body.GetProperty("errors");
        errors.TryGetProperty("from", out _).Should().BeTrue();
        errors.TryGetProperty("to", out _).Should().BeTrue();
    }

    /// <summary>
    /// D-C4 on the symptom the contract calls the most misleading of all: with only from sent,
    /// to fell to MinValue and the answer accused the final date of preceding the initial one
    /// -- a 422 about a field that was never sent. The status is asserted as well as the body,
    /// because blaming the range instead of the absence is exactly the failure being fixed.
    /// </summary>
    [Fact]
    public async Task A_report_missing_only_its_final_edge_is_refused_for_that_edge_and_not_for_the_range()
    {
        using var factory = CreateFactory();

        var response = await ClientCarrying(factory, Roles.Seller)
            .GetAsync("/api/reports/sales?from=2400-01-01T00:00:00Z");

        response.StatusCode.Should().NotBe(HttpStatusCode.UnprocessableEntity);

        var body = await BodyOf(response, HttpStatusCode.BadRequest);
        body.GetProperty("errors").TryGetProperty("to", out _).Should().BeTrue();
    }

    /// <summary>The refused edge travels alone: the other one is always a valid instant.</summary>
    private static string RangeWith(string field, string value) =>
        field == "from"
            ? $"from={Uri.EscapeDataString(value)}&to=2026-07-01T00:00:00Z"
            : $"from=2026-01-01T00:00:00Z&to={Uri.EscapeDataString(value)}";

    /// <summary>
    /// D-C3. A bare date is accepted today only because the container happens to run in UTC, so
    /// the same report would answer differently on a host with a local zone; and a date written
    /// the European way is read as another month altogether -- 01/06/2026 becomes the 6th of
    /// January. Neither can be allowed to produce a report of a period nobody asked for.
    ///
    /// The last three rows are the half of D-C3 the offset alone cannot defend: they all carry
    /// an explicit offset and none of them is ISO 8601, so a reader that only looks for the
    /// offset lets them through and answers for a period the caller never wrote.
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
    public async Task An_edge_that_is_not_ISO_8601_with_an_explicit_offset_is_refused_before_it_reports_another_period(
        string field,
        string value)
    {
        using var factory = CreateFactory();

        var body = await BodyOf(
            await ClientCarrying(factory, Roles.Seller).GetAsync($"/api/reports/sales?{RangeWith(field, value)}"),
            HttpStatusCode.BadRequest);

        body.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    /// <summary>
    /// The other half of the same decision: tightening the reader must not start refusing the
    /// spellings D-C3 names as valid, nor the seven-digit one every ToString("O") produces.
    /// </summary>
    [Theory]
    [InlineData("2026-01-01T00:00:00Z")]
    [InlineData("2026-01-01T00:00:00.0000000Z")]
    [InlineData("2026-01-01T02:00:00+02:00")]
    [InlineData("2026-01-01T02:00:00.123-05:00")]
    public async Task An_edge_in_ISO_8601_with_an_explicit_offset_is_accepted(string value)
    {
        using var factory = CreateFactory();

        var answered = await ClientCarrying(factory, Roles.Seller)
            .GetAsync($"/api/reports/sales?{RangeWith("from", value)}");

        answered.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// E-13 returns the two edges as they were received. Reading them from text rather than
    /// binding them is what makes normalising to UTC a one-line temptation, and the offset the
    /// caller wrote is the only place where giving in to it would show.
    /// </summary>
    [Fact]
    public async Task The_report_gives_the_two_edges_back_with_the_offset_they_arrived_with()
    {
        using var factory = CreateFactory();
        var bogota = new DateTimeOffset(2400, 1, 1, 0, 0, 0, TimeSpan.FromHours(-5));

        var report = await BodyOf(
            await ClientCarrying(factory, Roles.Seller)
                .GetAsync($"/api/reports/sales?{Range(bogota, bogota.AddDays(30))}"),
            HttpStatusCode.OK);

        report.GetProperty("from").GetString().Should().Be("2400-01-01T00:00:00-05:00");
        report.GetProperty("to").GetString().Should().Be("2400-01-31T00:00:00-05:00");
    }

    /// <summary>
    /// §1.3 and §2.1: a final edge before the initial one is the 422 the domain worded, and the
    /// message travels in detail because that is the field the portal's interceptor reads.
    /// </summary>
    [Fact]
    public async Task A_range_that_ends_before_it_starts_is_refused_with_the_message_of_the_domain()
    {
        using var factory = CreateFactory();
        var client = ClientCarrying(factory, Roles.Seller);
        var backwards = Range(
            new DateTimeOffset(2400, 12, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2400, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var problem = await BodyOf(
            await client.GetAsync($"/api/reports/sales?{backwards}"),
            HttpStatusCode.UnprocessableEntity);

        problem.GetProperty("detail").GetString().Should().Be("La fecha final no puede ser anterior a la inicial.");
    }
}
