using SimpleStockFlow.Application.Common;

namespace SimpleStockFlow.Application.Ports.Inbound;

/// <summary>Any driving adapter enters through here: REST today, a CLI or a job tomorrow.</summary>
public interface IManageProducts
{
    Task<Guid> CreateAsync(CreateProductCommand command, CancellationToken ct = default);

    /// <summary>False means no product carries that identifier. Whether that is a 404, a blank
    /// screen or a retry is the driving adapter's decision, and the port stays out of it.</summary>
    Task<bool> UpdateAsync(UpdateProductCommand command, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid productId, CancellationToken ct = default);
    Task<ProductView?> GetAsync(Guid productId, CancellationToken ct = default);
    Task<PagedResult<ProductView>> ListAsync(ProductFilter filter, PageRequest page, CancellationToken ct = default);

    /// <summary>The resolved address of the stored image, or null when no product matches.</summary>
    Task<string?> AttachImageAsync(Guid productId, ImageUpload image, CancellationToken ct = default);

    /// <summary>
    /// Reference data, read only and unpaged: no port creates, renames or removes a category, so
    /// there is no collection here that grows past a page.
    /// </summary>
    Task<IReadOnlyList<CategoryView>> ListCategoriesAsync(CancellationToken ct = default);
}

public sealed record CreateProductCommand(string Name, decimal Price, int Stock, Guid CategoryId);

public sealed record UpdateProductCommand(Guid Id, string Name, decimal Price, int Stock, Guid CategoryId);

public sealed record ProductFilter(string? Search = null, Guid? CategoryId = null);

public sealed record ProductView(
    Guid Id,
    string Name,
    decimal Price,
    string Currency,
    int Stock,
    Guid CategoryId,
    string CategoryName,
    string? ImageUrl);

public sealed record CategoryView(Guid Id, string Name);

/// <summary>Stream + metadata. The port does not know IFormFile: that lives in the REST adapter.</summary>
/// <param name="Length">
/// Declared up front so the size limit is met before a single byte is written. Measuring the
/// stream instead would mean either buffering the upload or deleting a half-written file.
/// </param>
public sealed record ImageUpload(Stream Content, string FileName, string ContentType, long Length);
