using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.Common;

namespace SimpleStockFlow.Application.Services;

/// <summary>
/// Orchestrates ports and delegates every rule about a product to the aggregate. The one thing
/// it decides for itself is whether a referenced row exists, because that is the single
/// question Product cannot answer: it holds no repository.
/// </summary>
public sealed class ProductCatalogService : IManageProducts
{
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;
    private readonly IFileStorage _storage;
    private readonly IUnitOfWork _unitOfWork;

    public ProductCatalogService(
        IProductRepository products,
        ICategoryRepository categories,
        IFileStorage storage,
        IUnitOfWork unitOfWork)
    {
        _products = products;
        _categories = categories;
        _storage = storage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> CreateAsync(CreateProductCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = Product.Create(command.Name, command.Price, command.Stock, command.CategoryId);
        await EnsureCategoryExistsAsync(command.CategoryId, ct);

        await _products.AddAsync(product, ct);
        await _unitOfWork.CommitAsync(ct);

        return product.Id;
    }

    public async Task<bool> UpdateAsync(UpdateProductCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await _products.FindAsync(command.Id, ct);
        if (product is null)
            return false;

        // Same order as creation, so the message a caller gets for a request with more than one
        // bad field does not depend on which verb it used.
        product.Rename(command.Name);
        product.ChangePrice(command.Price);
        product.SetCategory(command.CategoryId);
        product.SetStock(command.Stock);
        await EnsureCategoryExistsAsync(command.CategoryId, ct);

        await _unitOfWork.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid productId, CancellationToken ct = default)
    {
        var product = await _products.FindAsync(productId, ct);
        if (product is null)
            return false;

        var abandonedImage = product.ImageKey;
        product.AttachImage(null);
        _products.Delete(product);
        await _unitOfWork.CommitAsync(ct);

        // D-08: the binary goes last, and only once the database has agreed. The storage takes
        // no part in the transaction, so the two orders are not equivalent -- an orphaned file
        // is harmless, while a key pointing at a file that is gone is a broken image for good.
        await DeleteBinaryAsync(abandonedImage, ct);
        return true;
    }

    public async Task<ProductView?> GetAsync(Guid productId, CancellationToken ct = default)
    {
        var product = await _products.FindAsync(productId, ct);
        if (product is null)
            return null;

        return ToView(product, await CategoryNamesAsync(ct));
    }

    public async Task<PagedResult<ProductView>> ListAsync(
        ProductFilter filter,
        PageRequest page,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var found = await _products.SearchAsync(filter.Search, filter.CategoryId, page, ct);
        var names = await CategoryNamesAsync(ct);

        return new PagedResult<ProductView>(
            [.. found.Items.Select(product => ToView(product, names))],
            found.Page,
            found.Size,
            found.Total);
    }

    public async Task<string?> AttachImageAsync(
        Guid productId,
        ImageUpload image,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        // Looked up before a byte is written: saving first would leave a file on disk that no
        // row will ever reference, and nothing would ever delete it.
        var product = await _products.FindAsync(productId, ct);
        if (product is null)
            return null;

        var replacedImage = product.ImageKey;
        var key = await _storage.SaveAsync(image, ct);

        product.AttachImage(key);
        await _unitOfWork.CommitAsync(ct);

        await DeleteBinaryAsync(replacedImage, ct);
        return _storage.ResolveUrl(key);
    }

    public async Task<IReadOnlyList<CategoryView>> ListCategoriesAsync(CancellationToken ct = default) =>
        [.. (await _categories.ListAsync(ct)).Select(category => new CategoryView(category.Id, category.Name))];

    /// <summary>
    /// Prescribed by the API contract, including the wording. It runs after the aggregate has
    /// had its say so an absent identifier is still reported as missing rather than as unknown:
    /// asking the repository first would answer "no existe" for an empty one and the rule about
    /// a mandatory category could never be heard.
    /// </summary>
    private async Task EnsureCategoryExistsAsync(Guid categoryId, CancellationToken ct)
    {
        if (await _categories.FindAsync(categoryId, ct) is null)
            throw new DomainException($"La categoría {categoryId} no existe.");
    }

    /// <summary>
    /// Read whole rather than one row at a time: categories are five rows of reference data that
    /// no port can grow (D-10), and resolving them per product would be an N+1 over a page.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> CategoryNamesAsync(CancellationToken ct) =>
        (await _categories.ListAsync(ct)).ToDictionary(category => category.Id, category => category.Name);

    private Task DeleteBinaryAsync(string? key, CancellationToken ct) =>
        key is null ? Task.CompletedTask : _storage.DeleteAsync(key, ct);

    /// <summary>
    /// The category name is read live from its own row and never copied into the product; the
    /// sale line is where it gets frozen instead (ADR-004). The lookup is indexed and not probed
    /// on purpose: the restrictive foreign key means a miss is a lost row, and a blank name
    /// would hide that behind a product that merely looks uncategorised.
    /// </summary>
    private ProductView ToView(Product product, IReadOnlyDictionary<Guid, string> categoryNames) =>
        new(
            product.Id,
            product.Name,
            product.Price.Amount,
            product.Price.Currency,
            product.Stock,
            product.CategoryId,
            categoryNames[product.CategoryId],
            _storage.ResolveUrl(product.ImageKey));
}
