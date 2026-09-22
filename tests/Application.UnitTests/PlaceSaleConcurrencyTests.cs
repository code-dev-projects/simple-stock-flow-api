using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Application.Services;
using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Application.UnitTests;

/// <summary>
/// ADR-002 names the likeliest way to get this wrong: retrying the write alone, which would
/// reapply a discount computed over the stock that just lost the race. These tests assert the
/// catalog is read again on every attempt, which is the part that makes the retry correct.
/// </summary>
public sealed class PlaceSaleConcurrencyTests
{
    private static readonly Guid Tools = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly ISaleRepository _sales = Substitute.For<ISaleRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private readonly PlaceSaleService _sut;
    private readonly Product _product;

    public PlaceSaleConcurrencyTests()
    {
        _categories.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>([new Category(Tools, "Herramientas")]));

        _clock.UtcNow.Returns(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));
        _product = Product.Create("Rotomartillo", new Money(320m), 5, Tools);

        // A fresh copy per read, the way a real repository behaves once the tracker is cleared.
        _products
            .FindManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<Product>>([_product]));

        _sut = new PlaceSaleService(_products, _categories, _sales, _unitOfWork, _clock, new ConflictRetryPolicy(_ => TimeSpan.Zero));
    }

    private PlaceSaleCommand ASaleOfOneUnit() => new(Guid.NewGuid(), "vendedora", [new SaleLine(_product.Id, 1)]);

    private static ConcurrencyConflictException AConflict() =>
        new("Otra operación modificó los mismos datos al mismo tiempo. Vuelve a intentarlo.");

    [Fact]
    public async Task Reads_the_catalog_again_on_every_attempt_after_a_conflict()
    {
        _unitOfWork.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(_ => throw AConflict(), _ => Task.FromResult(1));

        await _sut.HandleAsync(ASaleOfOneUnit());

        await _products.Received(2).FindManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Without discarding, the second attempt would still be carrying the first attempt's
    /// withdrawal and would discount the same unit twice.
    /// </summary>
    [Fact]
    public async Task Discards_the_failed_attempt_before_reading_again()
    {
        _unitOfWork.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(_ => throw AConflict(), _ => Task.FromResult(1));

        await _sut.HandleAsync(ASaleOfOneUnit());

        _unitOfWork.Received(2).DiscardChanges();
    }

    [Fact]
    public async Task Surfaces_the_conflict_once_the_attempts_are_spent()
    {
        _unitOfWork.CommitAsync(Arg.Any<CancellationToken>()).ThrowsAsync(_ => AConflict());

        var act = () => _sut.HandleAsync(ASaleOfOneUnit());

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        await _products.Received(ConflictRetryPolicy.MaxAttempts)
            .FindManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }
}
