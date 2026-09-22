using NSubstitute;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Application.Services;
using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.Sales;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Application.UnitTests;

/// <summary>
/// Faked outbound ports, zero infrastructure: no database, no HTTP, no real clock. The day
/// these tests need Docker, the hexagon is broken.
/// </summary>
public sealed class PlaceSaleServiceTests
{
    private static readonly Guid Tools = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly ISaleRepository _sales = Substitute.For<ISaleRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private readonly PlaceSaleService _sut;

    public PlaceSaleServiceTests()
    {
        _categories.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>([new Category(Tools, "Herramientas")]));

        _clock.UtcNow.Returns(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));
        _sut = new PlaceSaleService(_products, _categories, _sales, _unitOfWork, _clock, new ConflictRetryPolicy(_ => TimeSpan.Zero));
    }

    private void GivenCatalog(params Product[] products) =>
        _products
            .FindManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Product>>(products));

    private static Product AProduct(string name, decimal price, int stock) =>
        Product.Create(name, new Money(price), stock, Tools);

    [Fact]
    public async Task Records_the_sale_and_commits_exactly_once()
    {
        var product = AProduct("Mouse", 50_000m, stock: 5);
        GivenCatalog(product);

        var id = await _sut.HandleAsync(
            new PlaceSaleCommand(Guid.NewGuid(), "ariel", [new SaleLine(product.Id, 2)]));

        id.Should().NotBeEmpty();
        product.Stock.Should().Be(3);
        await _sales.Received(1).AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fails_without_committing_when_the_product_does_not_exist()
    {
        GivenCatalog();

        var act = async () => await _sut.HandleAsync(
            new PlaceSaleCommand(Guid.NewGuid(), "ariel", [new SaleLine(Guid.NewGuid(), 1)]));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*no existe*");
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fails_without_committing_when_the_stock_falls_short()
    {
        var product = AProduct("Mouse", 50_000m, stock: 1);
        GivenCatalog(product);

        var act = async () => await _sut.HandleAsync(
            new PlaceSaleCommand(Guid.NewGuid(), "ariel", [new SaleLine(product.Id, 5)]));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*Stock insuficiente*");
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_a_repeated_line()
    {
        var product = AProduct("Mouse", 50_000m, stock: 5);
        GivenCatalog(product);

        var act = async () => await _sut.HandleAsync(new PlaceSaleCommand(
            Guid.NewGuid(),
            "ariel",
            [new SaleLine(product.Id, 1), new SaleLine(product.Id, 1)]));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*repetidos*");
    }

    [Fact]
    public async Task Rejects_a_sale_with_no_lines()
    {
        var act = async () => await _sut.HandleAsync(new PlaceSaleCommand(Guid.NewGuid(), "ariel", []));

        await act.Should().ThrowAsync<DomainException>();
        await _products.DidNotReceive().FindManyAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }
}
