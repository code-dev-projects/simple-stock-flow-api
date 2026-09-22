namespace SimpleStockFlow.Application.Common;

/// <summary>
/// Bounded retry for version conflicts. It replays the operation it is given, so the caller
/// must supply one that reads again from scratch: replaying only the write would reapply a
/// discount computed over a stale stock, which ADR-002 names as the likeliest way to get
/// this wrong.
/// </summary>
public sealed class ConflictRetryPolicy
{
    public const int MaxAttempts = 3;

    private readonly Func<int, TimeSpan> _backoff;

    public ConflictRetryPolicy(Func<int, TimeSpan>? backoff = null) => _backoff = backoff ?? Jittered;

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(ct);
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                await Task.Delay(_backoff(attempt), ct);
            }
        }
    }

    // Irregular on purpose: two sales that collided must not wake up together and collide again.
    private static TimeSpan Jittered(int attempt) =>
        TimeSpan.FromMilliseconds(Random.Shared.Next(10, 40) * attempt);
}
