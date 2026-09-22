using Microsoft.EntityFrameworkCore;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Sales;

namespace SimpleStockFlow.Adapters.Persistence.Repositories;

internal sealed class EfSaleRepository : ISaleRepository
{
    private readonly SalesDbContext _context;

    public EfSaleRepository(SalesDbContext context) => _context = context;

    public Task<Sale?> FindAsync(Guid id, CancellationToken ct = default) =>
        _context.Sales.FirstOrDefaultAsync(sale => sale.Id == id, ct);

    /// <summary>
    /// D-C2: the initial edge is in and the final one is out, which is the only reading under
    /// which two contiguous ranges cover the period without counting a sale twice. The count is
    /// taken over the range and not over the window, so the caller's page count is the range's.
    /// </summary>
    public async Task<PagedResult<Sale>> SearchAsync(DateRange range, PageRequest page, CancellationToken ct = default)
    {
        // Npgsql refuses to compare a timestamptz against a DateTimeOffset whose offset is not
        // zero, and D-C3 accepts any explicit offset, so "+02:00" would be a 500 rather than a
        // range. Normalising picks the same instant written another way; nothing moves.
        var from = range.From.ToUniversalTime();
        var to = range.To.ToUniversalTime();

        var query = _context.Sales
            .AsNoTracking()
            .Where(sale => sale.SoldAt >= from && sale.SoldAt < to);

        var total = await query.LongCountAsync(ct);

        var items = await query
            .OrderByDescending(sale => sale.SoldAt)
            .Skip(page.Skip)
            .Take(page.Size)
            .ToListAsync(ct);

        return new PagedResult<Sale>(items, page.Page, page.Size, total);
    }

    public async Task<IReadOnlyList<Sale>> ListByRangeAsync(DateRange range, CancellationToken ct = default) =>
        await _context.Sales
            .AsNoTracking()
            .Where(sale => sale.SoldAt >= range.From && sale.SoldAt <= range.To)
            .OrderBy(sale => sale.SoldAt)
            .ToListAsync(ct);

    public async Task AddAsync(Sale sale, CancellationToken ct = default) =>
        await _context.Sales.AddAsync(sale, ct);
}
