using SimpleStockFlow.Domain.Common;

namespace SimpleStockFlow.Application.Common;

public readonly record struct DateRange
{
    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }

    public DateRange(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
            throw new DomainException("La fecha final no puede ser anterior a la inicial.");
        From = from;
        To = to;
    }
}
