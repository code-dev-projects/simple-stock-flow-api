using SimpleStockFlow.Application.Ports.Outbound;

namespace SimpleStockFlow.Bootstrap.Composition;

/// <summary>
/// Trivial outbound adapter. It lives in bootstrap because of its size: should it grow
/// (time zones, a working calendar) it moves to src/adapters/outbound/time/ with its own csproj.
/// </summary>
internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
