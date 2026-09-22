using NSubstitute;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Application.Services;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Application.UnitTests;

/// <summary>
/// The half of HU-06 a double can answer: that the use case hands the range over, projects the
/// rows the engine already aggregated and adds the one field the engine cannot know, the
/// currency (D-C10). Which rows the range selects, in what order and under which frozen name
/// is asserted against a real Postgres in Adapters.IntegrationTests: faking it here would only
/// agree with whatever the query was asked for.
/// </summary>
public sealed class SalesReportServiceTests
{
    private static readonly Guid Hammer = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Brush = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Tape = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private static readonly DateTimeOffset June = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset July = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ISalesReportQuery _query = Substitute.For<ISalesReportQuery>();
    private readonly SalesReportService _sut;

    public SalesReportServiceTests() => _sut = new SalesReportService(_query);

    private void GivenAggregated(AggregatedSales aggregate) =>
        _query
            .AggregateAsync(Arg.Any<DateRange>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(aggregate));

    private static AggregatedSales Nothing() => new(0, 0m, []);

    /// <summary>
    /// CA-06.5, asserted on the shape of the use case instead of on a query plan. The defect
    /// the criterion forbids -- loading the sales of the range to add them up here -- needs a
    /// repository of sales to be reachable at all, so its absence is what the test pins. It
    /// fails the moment anyone injects one back, which is how the defect would return.
    /// </summary>
    [Fact]
    public void The_use_case_never_takes_a_repository_of_sales()
    {
        var dependencies = typeof(SalesReportService)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToList();

        dependencies.Should().NotContain(typeof(ISaleRepository));
        dependencies.Should().Contain(typeof(ISalesReportQuery));
    }

    /// <summary>
    /// The range is the engine's job. Narrowing, widening or re-zoning it here would move the
    /// D-C2 edges without a word, so it travels untouched.
    /// </summary>
    [Fact]
    public async Task The_range_reaches_the_read_port_untouched()
    {
        var range = new DateRange(June, July);
        GivenAggregated(Nothing());

        await _sut.HandleAsync(range);

        await _query.Received(1).AggregateAsync(range, Arg.Any<CancellationToken>());
    }

    /// <summary>E-13: the two edges come back as they were received, not as they were resolved.</summary>
    [Fact]
    public async Task The_report_echoes_the_range_it_was_asked_for()
    {
        var bogota = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(-5));
        GivenAggregated(Nothing());

        var report = await _sut.HandleAsync(new DateRange(bogota, bogota.AddDays(30)));

        report.From.Should().Be(bogota);
        report.To.Should().Be(bogota.AddDays(30));
    }

    /// <summary>CA-06.1: the five fields of every row survive the projection.</summary>
    [Fact]
    public async Task Every_aggregated_row_reaches_the_report_with_its_five_fields()
    {
        GivenAggregated(new AggregatedSales(
            2,
            81_002m,
            [new AggregatedProduct(Hammer, "Martillo de bola", "Herramientas", 3, 75_000m)]));

        var report = await _sut.HandleAsync(new DateRange(June, July));

        var row = report.Rows.Should().ContainSingle().Subject;
        row.ProductId.Should().Be(Hammer);
        row.ProductName.Should().Be("Martillo de bola");
        row.CategoryName.Should().Be("Herramientas");
        row.UnitsSold.Should().Be(3);
        row.Revenue.Should().Be(75_000m);
    }

    /// <summary>
    /// E-13 orders by revenue descending and the engine is what applies it. The rows arrive
    /// sorted; a second sort here -- or a set-based projection that lost the order -- would
    /// show through only when some other column disagrees, so the names and the identifiers
    /// are seeded in an order no accident reproduces.
    /// </summary>
    [Fact]
    public async Task The_rows_keep_the_order_the_engine_served_them_in()
    {
        GivenAggregated(new AggregatedSales(
            4,
            90_000m,
            [
                new AggregatedProduct(Tape, "Cinta", "Pinturas", 30, 60_000m),
                new AggregatedProduct(Hammer, "Martillo", "Herramientas", 1, 25_000m),
                new AggregatedProduct(Brush, "Brocha", "Pinturas", 1, 5_000m),
            ]));

        var report = await _sut.HandleAsync(new DateRange(June, July));

        report.Rows.Select(row => row.ProductId).Should().Equal(Tape, Hammer, Brush);
    }

    /// <summary>
    /// E-13: salesCount is the number of sales of the range, not of rows nor of units. Three
    /// sales over two products is the only arrangement where counting the rows, adding the
    /// units and reading the count apart give three different answers.
    /// </summary>
    [Fact]
    public async Task The_report_counts_sales_and_never_rows_or_units()
    {
        GivenAggregated(new AggregatedSales(
            3,
            30_000m,
            [
                new AggregatedProduct(Hammer, "Martillo", "Herramientas", 7, 25_000m),
                new AggregatedProduct(Brush, "Brocha", "Pinturas", 2, 5_000m),
            ]));

        var report = await _sut.HandleAsync(new DateRange(June, July));

        report.SalesCount.Should().Be(3);
        report.Rows.Should().HaveCount(2);
        report.GrandTotal.Should().Be(30_000m);
    }

    /// <summary>
    /// CA-06.2 and D-C10 together, which is the pair that breaks the screen when only one of
    /// them is met: a range with no sales answers a report, not an error, and that report still
    /// names its currency because the portal calls currency.toUpperCase() without a guard. An
    /// empty list of rows is the exact edge where a currency projected off the rows is null.
    /// </summary>
    [Fact]
    public async Task A_range_without_sales_is_an_empty_report_that_still_names_its_currency()
    {
        GivenAggregated(Nothing());

        var report = await _sut.HandleAsync(new DateRange(June, July));

        report.Rows.Should().NotBeNull().And.BeEmpty();
        report.SalesCount.Should().Be(0);
        report.GrandTotal.Should().Be(0m);
        report.Currency.Should().Be(Money.DefaultCurrency);
    }

    /// <summary>
    /// D-05 and D-C10: the currency is a constant of the domain, never a column and never a
    /// value the read port supplies. Pinned on a populated report too, so the guarantee does
    /// not rest on the empty case alone.
    /// </summary>
    [Fact]
    public async Task The_currency_is_the_constant_of_the_domain_and_not_something_the_engine_returns()
    {
        GivenAggregated(new AggregatedSales(
            1,
            25_000m,
            [new AggregatedProduct(Hammer, "Martillo", "Herramientas", 1, 25_000m)]));

        var report = await _sut.HandleAsync(new DateRange(June, July));

        report.Currency.Should().Be("COP");
        report.Currency.Should().Be(Money.DefaultCurrency);
    }

    /// <summary>The token reaches the engine: a cancelled report must stop at the query.</summary>
    [Fact]
    public async Task The_cancellation_token_reaches_the_read_port()
    {
        using var source = new CancellationTokenSource();
        GivenAggregated(Nothing());

        await _sut.HandleAsync(new DateRange(June, July), source.Token);

        await _query.Received(1).AggregateAsync(Arg.Any<DateRange>(), source.Token);
    }
}
