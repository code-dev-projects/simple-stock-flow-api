using SimpleStockFlow.Application.Common;

namespace SimpleStockFlow.Application.Ports.Inbound;

public interface IGetSalesReport
{
    Task<SalesReport> HandleAsync(DateRange range, CancellationToken ct = default);
}

public sealed record SalesReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int SalesCount,
    decimal GrandTotal,
    string Currency,
    IReadOnlyList<SalesReportRow> Rows);

public sealed record SalesReportRow(
    Guid ProductId,
    string ProductName,
    string CategoryName,
    int UnitsSold,
    decimal Revenue);
