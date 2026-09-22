using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Domain.UnitTests;

public sealed class MoneyTests
{
    [Fact]
    public void Rejects_a_negative_amount()
    {
        var act = () => new Money(-1m);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Refuses_to_operate_on_two_currencies()
    {
        var act = () => new Money(10m, "COP") + new Money(10m, "USD");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Rounds_the_amount_to_two_decimals()
    {
        new Money(10.005m).Amount.Should().Be(10.01m);
    }
}
