using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.Sales;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Application.Services;

/// <summary>
/// Reference use case: it orchestrates outbound ports and delegates EVERY business rule to
/// the aggregate. A business "if" showing up here belongs in the domain instead.
/// </summary>
public sealed class PlaceSaleService : IPlaceSale
{
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;
    private readonly ISaleRepository _sales;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ConflictRetryPolicy _retry;

    public PlaceSaleService(
        IProductRepository products,
        ICategoryRepository categories,
        ISaleRepository sales,
        IUnitOfWork unitOfWork,
        IClock clock,
        ConflictRetryPolicy retry)
    {
        _products = products;
        _categories = categories;
        _sales = sales;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _retry = retry;
    }

    public Task<Guid> HandleAsync(PlaceSaleCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return _retry.ExecuteAsync(token => AttemptAsync(command, token), ct);
    }

    /// <summary>
    /// One full attempt, read included. The retry replays this whole method and never just the
    /// commit, which is the distinction ADR-002 spells out.
    /// </summary>
    private async Task<Guid> AttemptAsync(PlaceSaleCommand command, CancellationToken ct)
    {
        // A no-op on the first attempt; on the later ones it is what makes the read real.
        _unitOfWork.DiscardChanges();

        if (command.Lines.Count == 0)
            throw new DomainException("La venta debe tener al menos un ítem.");

        var ids = command.Lines.Select(line => line.ProductId).Distinct().ToArray();
        if (ids.Length != command.Lines.Count)
            throw new DomainException("La venta tiene productos repetidos.");

        var loaded = await _products.FindManyAsync(ids, ct);
        var byId = loaded.ToDictionary(product => product.Id);

        // The whole catalogue of categories, because it is five rows and a closed set (D-10). The
        // domain only knows the category's identifier, so the label it freezes has to be resolved
        // here -- once, before the loop, and never inside the report (ADR-004).
        var categoryNames = (await _categories.ListAsync(ct))
            .ToDictionary(category => category.Id, category => category.Name);

        var sale = Sale.Open(_clock.UtcNow, command.SoldByUserId, command.SoldByUsername);

        foreach (var line in command.Lines)
        {
            if (!byId.TryGetValue(line.ProductId, out var product))
                throw new DomainException($"El producto {line.ProductId} no existe.");

            if (!categoryNames.TryGetValue(product.CategoryId, out var categoryName))
                throw new DomainException($"La categoría {product.CategoryId} no existe.");

            sale.AddItem(product, new Quantity(line.Quantity), categoryName);
        }

        sale.EnsureConfirmable();

        await _sales.AddAsync(sale, ct);
        await _unitOfWork.CommitAsync(ct);

        return sale.Id;
    }
}
