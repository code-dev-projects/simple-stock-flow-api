using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Domain.Catalog;

public sealed class Product : AggregateRoot<Guid>
{
    public string Name { get; private set; } = null!;
    public Money Price { get; private set; }
    public int Stock { get; private set; }
    public Guid CategoryId { get; private set; }

    /// <summary>Opaque <c>IFileStorage</c> key: neither a disk path nor the bytes themselves.</summary>
    public string? ImageKey { get; private set; }

    private Product() { }

    private Product(Guid id, string name, decimal price, int stock, Guid categoryId)
        : base(id)
    {
        Rename(name);
        ChangePrice(price);
        SetCategory(categoryId);
        SetStock(stock);
    }

    public static Product Create(string name, decimal price, int stock, Guid categoryId) =>
        new(Guid.NewGuid(), name, price, stock, categoryId);

    public static Product Create(string name, Money price, int stock, Guid categoryId)
    {
        // The guard has to stand here too: taking the amount out of the Money and handing it to the
        // decimal factory skips ChangePrice's check entirely, so a foreign currency would walk in
        // through the front door while the side door stayed locked.
        EnsureSystemCurrency(price);
        return Create(name, price.Amount, stock, categoryId);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del producto es obligatorio.");
        Name = name.Trim();
    }

    /// <summary>
    /// Judges the amount before it becomes Money, because Money's own floor is zero and not
    /// one: a negative price wrapped first would be refused for not being a valid amount, and
    /// the caller would never hear the rule that actually governs a product's price.
    /// </summary>
    public void ChangePrice(decimal price)
    {
        if (price <= 0)
            throw new DomainException("El precio debe ser mayor a cero.");
        Price = new Money(price);
    }

    /// <summary>
    /// RN-09, at the only door a foreign currency can arrive through. This used to read
    /// <c>ChangePrice(price.Amount)</c>, which took the currency and dropped it: a Money in USD
    /// became a price in COP, silently, because the column stores an amount with no currency
    /// beside it (D-05). That is a conversion nobody asked for and nobody was told about.
    /// Refusing costs the same and invents nothing.
    /// </summary>
    public void ChangePrice(Money price)
    {
        EnsureSystemCurrency(price);
        ChangePrice(price.Amount);
    }

    private static void EnsureSystemCurrency(Money price)
    {
        if (price.Currency != Money.DefaultCurrency)
            throw new DomainException(
                $"El sistema solo maneja {Money.DefaultCurrency} y el precio llegó en {price.Currency}.");
    }

    public void SetCategory(Guid categoryId)
    {
        if (categoryId == Guid.Empty)
            throw new DomainException("La categoría es obligatoria.");
        CategoryId = categoryId;
    }

    /// <summary>
    /// States the units on hand, which is what a full replacement of the product does. Moving
    /// the stock by the difference instead would put the arithmetic in the use case and would
    /// make a correction downwards fail as if the shelf were short.
    /// </summary>
    public void SetStock(int stock)
    {
        if (stock < 0)
            throw new DomainException("El stock inicial no puede ser negativo.");
        Stock = stock;
    }

    public void AttachImage(string? imageKey) => ImageKey = string.IsNullOrWhiteSpace(imageKey) ? null : imageKey;

    public void Restock(Quantity quantity) => Stock += quantity.Value;

    /// <summary>Central inventory invariant: stock never ends up negative.</summary>
    public void Withdraw(Quantity quantity)
    {
        if (quantity.Value > Stock)
            throw new DomainException($"Stock insuficiente para '{Name}': disponible {Stock}, solicitado {quantity.Value}.");
        Stock -= quantity.Value;
    }
}
