using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Application.Services;

/// <summary>
/// HU-06. The use case owns two things and no more: the range it was asked for, which it hands
/// over untouched, and the currency, which is a constant of the domain and not a column (D-05).
/// Everything else is the engine's, through <see cref="ISalesReportQuery"/> -- CA-06.5 makes
/// bringing the sales of the range into memory to add them up a defect, not an alternative.
/// </summary>
public sealed class SalesReportService : IGetSalesReport
{
    private readonly ISalesReportQuery _report;

    public SalesReportService(ISalesReportQuery report) => _report = report;

    public async Task<SalesReport> HandleAsync(DateRange range, CancellationToken ct = default)
    {
        var aggregate = await _report.AggregateAsync(range, ct);

        return new SalesReport(
            // E-13 returns the two edges as they were received, not as the engine resolved them.
            range.From,
            range.To,
            aggregate.SalesCount,
            aggregate.GrandTotal,
            // D-C10: the portal calls currency.toUpperCase() without a guard, so the empty
            // report is exactly where a currency read off the rows would take the screen down.
            Money.DefaultCurrency,
            // The rows arrive ordered by revenue descending, which E-13 states as contract.
            // Sorting again here would move that decision out of the engine and out of sight.
            [.. aggregate.Products.Select(product => new SalesReportRow(
                product.ProductId,
                product.ProductName,
                product.CategoryName,
                product.UnitsSold,
                product.Revenue))]);
    }
}
