using NSubstitute;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Application.Services;
using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.Sales;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Application.UnitTests;

/// <summary>
/// The projection from the aggregate to the view, with the repository faked. What a double
/// cannot answer -- which rows the range selects and in what order the engine serves them --
/// is asserted against a real Postgres in Adapters.IntegrationTests instead of being faked
/// into agreement here.
/// </summary>
public sealed class GetSaleServiceTests
{
    private static readonly Guid Tools = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset Noon = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly ISaleRepository _sales = Substitute.For<ISaleRepository>();
    private readonly GetSaleService _sut;

    public GetSaleServiceTests() => _sut = new GetSaleService(_sales);

    private static Sale ASale(
        DateTimeOffset soldAt,
        string soldBy,
        params (string Name, decimal Price, int Quantity)[] lines)
    {
        var sale = Sale.Open(soldAt, Guid.NewGuid(), soldBy);

        foreach (var (name, price, quantity) in lines)
            sale.AddItem(Product.Create(name, new Money(price), quantity, Tools), new Quantity(quantity), "Herramientas");

        return sale;
    }

    private void GivenStored(Sale sale) =>
        _sales.FindAsync(sale.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Sale?>(sale));

    private void GivenPage(PagedResult<Sale> page) =>
        _sales
            .SearchAsync(Arg.Any<DateRange>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(page));

    /// <summary>CA-05.1 and E-11: the five fields of the sale and the five of every line.</summary>
    [Fact]
    public async Task Reads_the_date_the_author_the_lines_and_the_total_of_a_sale()
    {
        var sale = ASale(Noon, "ana", ("Martillo", 25_000m, 3), ("Brocha", 7_450m, 2));
        GivenStored(sale);

        var view = await _sut.GetAsync(sale.Id);

        view.Should().NotBeNull();
        view!.Id.Should().Be(sale.Id);
        view.SoldAt.Should().Be(Noon);
        view.SoldBy.Should().Be("ana");
        view.Currency.Should().Be(Money.DefaultCurrency);

        var hammer = view.Items.Single(item => item.ProductName == "Martillo");
        hammer.ProductId.Should().NotBeEmpty();
        hammer.Quantity.Should().Be(3);
        hammer.UnitPrice.Should().Be(25_000m);
        hammer.Subtotal.Should().Be(75_000m);
    }

    /// <summary>
    /// CA-05.4 and RN-12. Asserted against the sum of the lines that travelled in the same
    /// view, never against a number written here: a literal would keep agreeing with a total
    /// that had stopped being computed from the lines, which is the only thing the criterion
    /// forbids. No single line equals the total, so a projection that mapped one of them --
    /// or the first, or the largest -- is caught too.
    /// </summary>
    [Fact]
    public async Task The_total_is_whatever_the_lines_add_up_to_and_never_a_number_of_its_own()
    {
        var sale = ASale(Noon, "ana", ("Martillo", 25_000.55m, 3), ("Brocha", 7_450.25m, 2), ("Cinta", 1_999.99m, 7));
        GivenStored(sale);

        var view = await _sut.GetAsync(sale.Id);

        view!.Total.Should().Be(view.Items.Sum(item => item.Subtotal));
        view.Total.Should().BeGreaterThan(view.Items.Max(item => item.Subtotal));
        view.Items.Should().HaveCount(3);
    }

    /// <summary>
    /// Every line carries what the sale froze, not what the catalogue says now (CA-04.7). The
    /// product is renamed and repriced after the fact, in place, so nothing but a projection
    /// that read the line could still answer the old values.
    /// </summary>
    [Fact]
    public async Task Every_line_keeps_the_name_and_the_price_of_the_moment_it_was_sold()
    {
        var product = Product.Create("Martillo", new Money(25_000m), 10, Tools);
        var sale = Sale.Open(Noon, Guid.NewGuid(), "ana");
        sale.AddItem(product, new Quantity(2), "Herramientas");
        GivenStored(sale);

        product.Rename("Martillo de bola");
        product.ChangePrice(99_000m);

        var view = await _sut.GetAsync(sale.Id);

        var line = view!.Items.Should().ContainSingle().Subject;
        line.ProductName.Should().Be("Martillo");
        line.UnitPrice.Should().Be(25_000m);
        line.Subtotal.Should().Be(50_000m);
    }

    /// <summary>A sale that is not there is the 404 of E-12, not an exception.</summary>
    [Fact]
    public async Task A_sale_that_is_not_stored_comes_back_as_nothing()
    {
        _sales.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Sale?>(null));

        (await _sut.GetAsync(Guid.NewGuid())).Should().BeNull();
    }

    /// <summary>
    /// D-C10: currency is a constant of the domain and never travels null, because the portal
    /// calls currency.toUpperCase() on it without a guard. The empty sale is the edge where a
    /// projection that read the currency off the first line would hand over null instead.
    /// </summary>
    [Fact]
    public async Task A_sale_without_lines_still_names_its_currency_and_carries_an_empty_list()
    {
        var sale = Sale.Open(Noon, Guid.NewGuid(), "ana");
        GivenStored(sale);

        var view = await _sut.GetAsync(sale.Id);

        view!.Currency.Should().Be(Money.DefaultCurrency);
        view.Total.Should().Be(0m);
        view.Items.Should().NotBeNull().And.BeEmpty();
    }

    /// <summary>
    /// The range and the window are the engine's job (Q7). Handing them over untouched is what
    /// keeps the count and the page from being computed over rows brought into memory.
    /// </summary>
    [Fact]
    public async Task The_range_and_the_window_reach_the_repository_untouched()
    {
        var range = new DateRange(Noon, Noon.AddDays(1));
        var page = new PageRequest(3, 25);
        GivenPage(PagedResult<Sale>.Empty(page));

        await _sut.ListAsync(range, page);

        await _sales.Received(1).SearchAsync(range, page, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// CA-05.2. The three paging numbers are the ones the repository served, so totalPages --
    /// which nothing declares and the portal needs -- stays derived from them (D-C11).
    /// </summary>
    [Fact]
    public async Task The_page_reports_the_numbers_the_repository_served()
    {
        var page = new PageRequest(2, 20);
        GivenPage(new PagedResult<Sale>([ASale(Noon, "ana", ("Martillo", 25_000m, 1))], 2, 20, 57));

        var view = await _sut.ListAsync(new DateRange(Noon, Noon.AddDays(1)), page);

        view.Page.Should().Be(2);
        view.Size.Should().Be(20);
        view.Total.Should().Be(57);
        view.TotalPages.Should().Be(3);
        view.Items.Should().ContainSingle();
    }

    /// <summary>
    /// E-11 orders the page by soldAt descending, and the engine is what applies it. The
    /// projection must not reorder: the rows arrive already sorted and a second sort here --
    /// or a set-based projection that lost the order -- would show through only over a page
    /// boundary, where it is hardest to see.
    /// </summary>
    [Fact]
    public async Task The_page_hands_the_sales_over_in_the_order_it_received_them()
    {
        var latest = ASale(Noon.AddHours(2), "ana", ("Martillo", 25_000m, 1));
        var middle = ASale(Noon.AddHours(1), "bea", ("Brocha", 7_450m, 1));
        var earliest = ASale(Noon, "ana", ("Cinta", 1_999m, 1));
        GivenPage(new PagedResult<Sale>([latest, middle, earliest], 1, 20, 3));

        var view = await _sut.ListAsync(new DateRange(Noon, Noon.AddDays(1)), new PageRequest(1, 20));

        view.Items.Select(sale => sale.Id).Should().Equal(latest.Id, middle.Id, earliest.Id);
    }

    /// <summary>A range with no sales is an empty page, never a null list (D-C10).</summary>
    [Fact]
    public async Task A_range_with_no_sales_is_an_empty_page()
    {
        var page = new PageRequest(1, 20);
        GivenPage(PagedResult<Sale>.Empty(page));

        var view = await _sut.ListAsync(new DateRange(Noon, Noon.AddDays(1)), page);

        view.Items.Should().NotBeNull().And.BeEmpty();
        view.Total.Should().Be(0);
        view.TotalPages.Should().Be(0);
    }
}
