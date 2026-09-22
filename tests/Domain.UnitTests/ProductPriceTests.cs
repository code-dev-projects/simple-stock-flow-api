using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Domain.UnitTests;

/// <summary>
/// RN-02 is "greater than zero", and Money's own floor is "not negative": two different rules
/// that meet on the same number. A price arriving as a plain amount has to be judged by the
/// product's rule, or a negative one is refused by the wrong guard and the caller is told the
/// amount cannot be negative instead of that the price must be above zero (CA-02.2, E-05).
/// </summary>
public sealed class ProductPriceTests
{
    private static Product AProduct() =>
        Product.Create("Teclado", new Money(120_000m), 10, Guid.NewGuid());

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-120_000)]
    public void ChangePrice_rejects_anything_at_or_below_zero_with_the_rule_of_the_product(decimal price)
    {
        var product = AProduct();

        var act = () => product.ChangePrice(price);

        act.Should().Throw<DomainException>().WithMessage("El precio debe ser mayor a cero.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Creation_rejects_the_same_prices_with_the_same_message(decimal price)
    {
        var act = () => Product.Create("Teclado", price, 10, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("El precio debe ser mayor a cero.");
    }

    [Fact]
    public void A_price_above_zero_is_kept_as_money_in_the_single_currency()
    {
        var product = Product.Create("Teclado", 120_000.456m, 10, Guid.NewGuid());

        product.Price.Amount.Should().Be(120_000.46m);
        product.Price.Currency.Should().Be(Money.DefaultCurrency);
    }
    [Fact]
    public void Refuses_a_price_in_a_currency_the_system_does_not_keep()
    {
        // RN-09, at the only door a foreign currency can arrive through. D-05 makes this system
        // monocurrency and the column stores an amount with no currency beside it, so accepting a
        // Money in USD and writing it as COP is a conversion nobody asked for and nobody is told
        // about. Refusing out loud costs the same and loses nothing.
        var act = () => Product.Create("Teclado", new Money(120_000m, "USD"), 5, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*USD*");
    }

    [Fact]
    public void Changing_the_price_to_another_currency_is_refused_the_same_way()
    {
        var product = Product.Create("Mouse", new Money(50_000m), 5, Guid.NewGuid());

        var act = () => product.ChangePrice(new Money(60_000m, "EUR"));

        act.Should().Throw<DomainException>().WithMessage("*EUR*");
        product.Price.Amount.Should().Be(50_000m, "a refused change must not have been applied");
    }

}
