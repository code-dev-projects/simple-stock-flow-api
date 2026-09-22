using SimpleStockFlow.Domain.Catalog;

namespace SimpleStockFlow.Application.Ports.Outbound;

public interface ICategoryRepository
{
    Task<Category?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct = default);
}
