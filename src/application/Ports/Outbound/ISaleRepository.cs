using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Domain.Sales;

namespace SimpleStockFlow.Application.Ports.Outbound;

public interface ISaleRepository
{
    Task<Sale?> FindAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Sale>> SearchAsync(DateRange range, PageRequest page, CancellationToken ct = default);
    Task<IReadOnlyList<Sale>> ListByRangeAsync(DateRange range, CancellationToken ct = default);
    Task AddAsync(Sale sale, CancellationToken ct = default);
}
