using SimpleStockFlow.Domain.Common;

namespace SimpleStockFlow.Domain.ValueObjects;

public readonly record struct Money
{
    public const string DefaultCurrency = "COP";

    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency = DefaultCurrency)
    {
        if (amount < 0)
            throw new DomainException("El monto no puede ser negativo.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("La moneda es obligatoria.");

        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = currency.ToUpperInvariant();
    }

    public static Money Zero(string currency = DefaultCurrency) => new(0m, currency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator *(Money money, int factor)
    {
        if (factor < 0)
            throw new DomainException("El factor no puede ser negativo.");
        return new Money(money.Amount * factor, money.Currency);
    }

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!string.Equals(left.Currency, right.Currency, StringComparison.Ordinal))
            throw new DomainException($"No se pueden operar montos en {left.Currency} y {right.Currency}.");
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
