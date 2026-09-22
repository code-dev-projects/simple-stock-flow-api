using Microsoft.EntityFrameworkCore;
using SimpleStockFlow.Adapters.Persistence.Configurations;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Catalog;

namespace SimpleStockFlow.Adapters.Persistence.Repositories;

internal sealed class EfProductRepository : IProductRepository
{
    private readonly SalesDbContext _context;

    public EfProductRepository(SalesDbContext context) => _context = context;

    /// <summary>
    /// The one read of the three that lifts the global filter, and the only place in the
    /// repository where IgnoreQueryFilters may appear. Without it a sale of a withdrawn product
    /// could not resolve its line, and the takedown could not be repeated: the row would be
    /// invisible to the very use case that has to update it.
    /// </summary>
    public Task<Product?> FindAsync(Guid id, CancellationToken ct = default) =>
        _context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(product => product.Id == id, ct);

    public async Task<IReadOnlyList<Product>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        await _context.Products.Where(product => ids.Contains(product.Id)).ToListAsync(ct);

    public async Task<PagedResult<Product>> SearchAsync(
        string? search,
        Guid? categoryId,
        PageRequest page,
        CancellationToken ct = default)
    {
        var query = _context.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(product => EF.Functions.ILike(product.Name, $"%{search.Trim()}%"));

        if (categoryId is { } category)
            query = query.Where(product => product.CategoryId == category);

        var total = await query.LongCountAsync(ct);

        var items = await query
            .OrderBy(product => product.Name)
            .Skip(page.Skip)
            .Take(page.Size)
            .ToListAsync(ct);

        return new PagedResult<Product>(items, page.Page, page.Size, total);
    }

    public async Task AddAsync(Product product, CancellationToken ct = default) =>
        await _context.Products.AddAsync(product, ct);

    /// <summary>
    /// An UPDATE, never a DELETE: the sale lines that reference the product must keep resolving
    /// it, and the restrictive foreign key from sale_item would refuse the delete anyway for
    /// exactly the products that matter -- the ones that were sold.
    ///
    /// The instant is read straight from the wall clock and not from IClock. The clock port
    /// exists so that the rules of the application are testable, and this value is not one: it
    /// crosses no port, appears in no response and no contract asserts it. Injecting the port
    /// here would also make the persistence adapter unresolvable on its own, which is a real
    /// cost paid for an observability nobody asked for.
    /// </summary>
    public void Delete(Product product) =>
        _context.Entry(product).Property<DateTimeOffset?>(ProductConfiguration.DeletedAt).CurrentValue =
            DateTimeOffset.UtcNow;
}
