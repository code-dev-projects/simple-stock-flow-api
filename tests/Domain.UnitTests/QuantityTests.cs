using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Domain.UnitTests;

/// <summary>
/// RN-03 lived in a constructor no test ever called, so nothing held the guard in place. The
/// message is asserted literally because the contract publishes it: E-10 turns it into the 422
/// detail a seller reads when a line asks for zero units (CA-04.5).
/// </summary>
public sealed class QuantityTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Rejects_anything_at_or_below_zero(int value)
    {
        var act = () => new Quantity(value);

        act.Should().Throw<DomainException>().WithMessage("La cantidad debe ser mayor a cero.");
    }

    /// <summary>
    /// One unit is the smallest sale there is, so it pins the boundary from the other side: a
    /// guard that refused it would be over-broad and no rejection test would notice.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void Keeps_the_units_it_was_given(int value)
    {
        new Quantity(value).Value.Should().Be(value);
    }
}
