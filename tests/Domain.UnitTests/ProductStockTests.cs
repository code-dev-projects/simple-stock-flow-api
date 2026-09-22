using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Domain.UnitTests;

/// <summary>
/// A full replacement of the product (E-06) states the units on hand instead of moving them,
/// and that operation carries the same invariant creation does. Without it the use case would
/// have to work out the difference itself, which is the inventory rule leaking out of the
/// aggregate, and subtracting through Withdraw would blame the stock for being insufficient.
/// </summary>
public sealed class ProductStockTests
{
    private static Product AProduct(int stock = 10) =>
        Product.Create("Teclado", new Money(120_000m), stock, Guid.NewGuid());

    [Fact]
    public void SetStock_states_the_units_on_hand()
    {
        var product = AProduct(stock: 10);

        product.SetStock(3);

        product.Stock.Should().Be(3);
    }

    [Fact]
    public void SetStock_accepts_an_empty_shelf()
    {
        var product = AProduct(stock: 10);

        product.SetStock(0);

        product.Stock.Should().Be(0);
    }

    [Fact]
    public void SetStock_rejects_a_negative_amount()
    {
        var product = AProduct();

        var act = () => product.SetStock(-1);

        act.Should().Throw<DomainException>().WithMessage("El stock inicial no puede ser negativo.");
    }

    /// <summary>
    /// Creation goes through the same guard, so the message the contract prescribes for a
    /// negative stock cannot drift between creating and replacing.
    /// </summary>
    [Fact]
    public void Creation_rejects_a_negative_initial_stock_with_the_same_message()
    {
        var act = () => Product.Create("Teclado", new Money(120_000m), -1, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("El stock inicial no puede ser negativo.");
    }
}
