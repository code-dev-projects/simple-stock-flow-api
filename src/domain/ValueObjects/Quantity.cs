using SimpleStockFlow.Domain.Common;

namespace SimpleStockFlow.Domain.ValueObjects;

public readonly record struct Quantity
{
    public int Value { get; }

    public Quantity(int value)
    {
        if (value <= 0)
            throw new DomainException("La cantidad debe ser mayor a cero.");
        Value = value;
    }

    public static implicit operator int(Quantity quantity) => quantity.Value;

    public override string ToString() => Value.ToString();
}
