namespace SimpleStockFlow.Application.Ports.Outbound;

/// <summary>
/// Transactional boundary. Repositories do not persist on their own: the application service
/// decides when the work is committed.
/// </summary>
public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Throws away everything pending so the next read starts from the engine again. A retry
    /// that skips this would keep the losing attempt's changes and discount twice (ADR-002).
    /// </summary>
    void DiscardChanges();
}
