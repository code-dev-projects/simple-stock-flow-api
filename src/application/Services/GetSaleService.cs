using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Sales;

namespace SimpleStockFlow.Application.Services;

/// <summary>
/// Reads the sales aggregate and projects it. It holds no rule of its own: which rows a range
/// selects, in what order and how many there are are the engine's answers, and the total is the
/// aggregate's. Anything computed here would be a second source for a value that already exists.
/// </summary>
public sealed class GetSaleService : IGetSale
{
    private readonly ISaleRepository _sales;

    public GetSaleService(ISaleRepository sales) => _sales = sales;

    public async Task<SaleView?> GetAsync(Guid saleId, CancellationToken ct = default)
    {
        var sale = await _sales.FindAsync(saleId, ct);
        return sale is null ? null : ToView(sale);
    }

    /// <summary>
    /// The range, the window and the count travel to the repository untouched: filtering or
    /// counting here would mean bringing the whole range into memory to serve one page of it.
    /// The order is the one the rows arrive in, never re-applied.
    /// </summary>
    public async Task<PagedResult<SaleView>> ListAsync(
        DateRange range,
        PageRequest page,
        CancellationToken ct = default)
    {
        var found = await _sales.SearchAsync(range, page, ct);

        return new PagedResult<SaleView>([.. found.Items.Select(ToView)], found.Page, found.Size, found.Total);
    }

    /// <summary>
    /// RN-12: the total is asked of the aggregate, which adds up its lines, and no column holds
    /// it. The currency rides along with that total rather than being written here, so there is
    /// one place to change the day the system stops being single-currency (D-05, D-C10).
    /// </summary>
    private static SaleView ToView(Sale sale)
    {
        var total = sale.Total;

        return new SaleView(
            sale.Id,
            sale.SoldAt,
            sale.SoldByUsername,
            total.Amount,
            total.Currency,
            [.. sale.Items.Select(item => new SaleItemView(
                item.ProductId,
                item.ProductName,
                item.Quantity.Value,
                item.UnitPrice.Amount,
                item.Subtotal.Amount))]);
    }
}
