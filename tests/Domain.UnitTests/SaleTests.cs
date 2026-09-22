using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.Sales;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Domain.UnitTests;

public sealed class SaleTests
{
    private static Product AProduct(string name, decimal price, int stock) =>
        Product.Create(name, new Money(price), stock, Guid.NewGuid());

    [Fact]
    public void AddItem_takes_the_stock_and_freezes_the_price()
    {
        var product = AProduct("Mouse", 50_000m, stock: 5);
        var sale = Sale.Open(DateTimeOffset.UtcNow, Guid.NewGuid(), "ariel");

        sale.AddItem(product, new Quantity(2), "Herramientas");
        product.ChangePrice(new Money(90_000m)); // the catalogue rises after the sale

        product.Stock.Should().Be(3);
        sale.Items.Single().UnitPrice.Amount.Should().Be(50_000m);
        sale.Total.Amount.Should().Be(100_000m);
    }

    [Fact]
    public void AddItem_rejects_the_same_product_twice()
    {
        var product = AProduct("Mouse", 50_000m, stock: 5);
        var sale = Sale.Open(DateTimeOffset.UtcNow, Guid.NewGuid(), "ariel");
        sale.AddItem(product, new Quantity(1), "Herramientas");

        var act = () => sale.AddItem(product, new Quantity(1), "Herramientas");

        act.Should().Throw<DomainException>().WithMessage("*ya está en la venta*");
    }

    [Fact]
    public void EnsureConfirmable_rejects_a_sale_with_no_lines()
    {
        var sale = Sale.Open(DateTimeOffset.UtcNow, Guid.NewGuid(), "ariel");

        var act = sale.EnsureConfirmable;

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Total_adds_up_every_line()
    {
        var sale = Sale.Open(DateTimeOffset.UtcNow, Guid.NewGuid(), "ariel");
        sale.AddItem(AProduct("Mouse", 50_000m, 5), new Quantity(2), "Herramientas");
        sale.AddItem(AProduct("Teclado", 120_000m, 5), new Quantity(1), "Herramientas");

        sale.Total.Amount.Should().Be(220_000m);
    }
}
