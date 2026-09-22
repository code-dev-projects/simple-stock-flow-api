using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Domain.Sales;

public sealed class Sale : AggregateRoot<Guid>
{
    private readonly List<SaleItem> _items = [];

    public DateTimeOffset SoldAt { get; private set; }
    /// <summary>
    /// Who registered it, as an identifier the engine can hold to (T-12, FK-4 RESTRICT). Neither
    /// field replaces the other: the identifier gives integrity and survives a rename, the name
    /// gives fidelity to what was written at the time.
    /// </summary>
    public Guid SoldByUserId { get; private set; }

    public string SoldByUsername { get; private set; } = null!;
    public IReadOnlyCollection<SaleItem> Items => _items.AsReadOnly();

    public Money Total => _items.Count == 0
        ? Money.Zero()
        : _items.Select(item => item.Subtotal).Aggregate(static (left, right) => left + right);

    private Sale() { }

    private Sale(Guid id, DateTimeOffset soldAt, Guid soldByUserId, string soldByUsername) : base(id)
    {
        if (soldByUserId == Guid.Empty)
            throw new DomainException("La venta debe registrar quién la realiza.");
        if (string.IsNullOrWhiteSpace(soldByUsername))
            throw new DomainException("La venta debe registrar quién la realiza.");

        SoldAt = soldAt;
        SoldByUserId = soldByUserId;
        SoldByUsername = soldByUsername;
    }

    public static Sale Open(DateTimeOffset soldAt, Guid soldByUserId, string soldByUsername) =>
        new(Guid.NewGuid(), soldAt, soldByUserId, soldByUsername);

    /// <summary>
    /// Withdraws from the product and adds the item in the same operation: the stock discount
    /// and the sale line cannot exist without each other.
    /// </summary>
    public void AddItem(Product product, Quantity quantity, string categoryName)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (_items.Any(item => item.ProductId == product.Id))
            throw new DomainException($"El producto '{product.Name}' ya está en la venta.");

        product.Withdraw(quantity);
        _items.Add(new SaleItem(product.Id, product.Name, categoryName, quantity, product.Price));
    }

    /// <summary>Closing invariant: a sale without lines is never persisted.</summary>
    public void EnsureConfirmable()
    {
        if (_items.Count == 0)
            throw new DomainException("La venta debe tener al menos un ítem.");
    }
}
