namespace SimpleStockFlow.Application.Ports.Outbound;

/// <summary>Time is infrastructure. Without this the date-range report is not testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
