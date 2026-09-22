using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Domain.UnitTests;

public sealed class ProductTests
{
    private static Product AProduct(int stock = 10) =>
        Product.Create("Teclado", new Money(120_000m), stock, Guid.NewGuid());

    [Fact]
    public void Withdraw_takes_the_units_out_of_stock()
    {
        var product = AProduct(stock: 10);

        product.Withdraw(new Quantity(3));

        product.Stock.Should().Be(7);
    }

    [Fact]
    public void Withdraw_never_leaves_the_stock_negative()
    {
        var product = AProduct(stock: 2);

        var act = () => product.Withdraw(new Quantity(3));

        act.Should().Throw<DomainException>().WithMessage("*Stock insuficiente*");
    }

    [Fact]
    public void ChangePrice_rejects_a_price_of_zero()
    {
        var product = AProduct();

        var act = () => product.ChangePrice(Money.Zero());

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_demands_a_name(string name)
    {
        var product = AProduct();

        var act = () => product.Rename(name);

        act.Should().Throw<DomainException>();
    }
}
