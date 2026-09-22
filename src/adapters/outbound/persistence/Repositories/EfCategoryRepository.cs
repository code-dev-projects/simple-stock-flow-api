using Microsoft.EntityFrameworkCore;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Catalog;

namespace SimpleStockFlow.Adapters.Persistence.Repositories;

internal sealed class EfCategoryRepository : ICategoryRepository
{
    private readonly SalesDbContext _context;

    public EfCategoryRepository(SalesDbContext context) => _context = context;

    public Task<Category?> FindAsync(Guid id, CancellationToken ct = default) =>
        _context.Categories.FirstOrDefaultAsync(category => category.Id == id, ct);

    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct = default) =>
        await _context.Categories.AsNoTracking().OrderBy(category => category.Name).ToListAsync(ct);
}
