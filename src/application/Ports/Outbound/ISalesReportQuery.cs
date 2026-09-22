using SimpleStockFlow.Application.Common;

namespace SimpleStockFlow.Application.Ports.Outbound;

/// <summary>
/// The read-only side of HU-06 (D-06, ADR-004). It hands back rows the engine has already
/// grouped, so the report's use case never holds a sale: CA-06.5 makes bringing the range into
/// memory to add it up a defect, and a port that spoke in aggregates would make that defect the
/// natural way to write the service.
/// </summary>
public interface ISalesReportQuery
{
    Task<AggregatedSales> AggregateAsync(DateRange range, CancellationToken ct = default);
}

/// <summary>
/// Everything the engine can answer about a range. The currency is not here on purpose: it is
/// a constant of the domain and not a column (D-05), and the use case is what puts it in.
/// </summary>
public sealed record AggregatedSales(
    int SalesCount,
    decimal GrandTotal,
    IReadOnlyList<AggregatedProduct> Products);

/// <summary>
/// One product's share of the range, already summed. DP-02 keeps the seller out of this shape:
/// it is closed on product, category, units and amount, by decision and not by omission.
/// </summary>
public sealed record AggregatedProduct(
    Guid ProductId,
    string ProductName,
    string CategoryName,
    int UnitsSold,
    decimal Revenue);
