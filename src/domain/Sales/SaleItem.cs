using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Domain.Sales;

public sealed class SaleItem : Entity<Guid>
{
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = null!;

    /// <summary>
    /// Frozen at the instant of the sale, like the name and the price. The report groups BY this
    /// value instead of resolving it, so recategorising a product cannot rewrite a closed period
    /// (ADR-004) and cannot silently move units under a label the sale never carried.
    /// </summary>
    public string CategoryName { get; private set; } = null!;
    public Quantity Quantity { get; private set; }

    /// <summary>Price frozen at sale time: it does not follow the catalogue.</summary>
    public Money UnitPrice { get; private set; }

    public Money Subtotal => UnitPrice * Quantity.Value;

    private SaleItem() { }

    internal SaleItem(
        Guid productId,
        string productName,
        string categoryName,
        Quantity quantity,
        Money unitPrice)
        : base(Guid.NewGuid())
    {
        if (productId == Guid.Empty)
            throw new DomainException("El producto del ítem es obligatorio.");

        if (string.IsNullOrWhiteSpace(categoryName))
            throw new DomainException("La categoría del ítem es obligatoria.");

        ProductId = productId;
        ProductName = productName;
        CategoryName = categoryName;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }
}
