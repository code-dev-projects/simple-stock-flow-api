using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Domain.Catalog;

namespace SimpleStockFlow.Application.Ports.Outbound;

/// <summary>
/// The three reads are not interchangeable, and ADR-003 fixes which of them sees a product
/// that has been taken down. Whoever adds a fourth has to decide the same question.
/// </summary>
public interface IProductRepository
{
    /// <summary>
    /// Serves products that have been taken down as well: the detail of an old sale has to be
    /// able to resolve its product, and the takedown itself reads through here.
    /// </summary>
    Task<Product?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>The load that precedes a sale. It never serves a product that was taken down.</summary>
    Task<IReadOnlyList<Product>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    /// <summary>The catalogue. It never serves a product that was taken down.</summary>
    Task<PagedResult<Product>> SearchAsync(string? search, Guid? categoryId, PageRequest page, CancellationToken ct = default);

    Task AddAsync(Product product, CancellationToken ct = default);

    /// <summary>
    /// Takes the product out of the catalogue. Whether that is a row destroyed or a row marked
    /// is the adapter's business and not this port's; what the port promises is that the sales
    /// already made keep resolving it.
    /// </summary>
    void Delete(Product product);
}
