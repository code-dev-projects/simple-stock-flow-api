using SimpleStockFlow.Application.Common;

namespace SimpleStockFlow.Application.Ports.Inbound;

public interface IGetSale
{
    Task<SaleView?> GetAsync(Guid saleId, CancellationToken ct = default);
    Task<PagedResult<SaleView>> ListAsync(DateRange range, PageRequest page, CancellationToken ct = default);
}

public sealed record SaleView(
    Guid Id,
    DateTimeOffset SoldAt,
    string SoldBy,
    decimal Total,
    string Currency,
    IReadOnlyList<SaleItemView> Items);

public sealed record SaleItemView(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal Subtotal);
