using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Outbound;

namespace SimpleStockFlow.Adapters.Persistence.Repositories;

/// <summary>
/// Pattern Q9, the most expensive read in the system, written as SQL because that is what it
/// is. The grouping, the sum and the order all happen in the engine; what crosses the port is
/// one row per product. LINQ is not used here for one reason: DP-01 needs a window function to
/// pick the frozen name, and a translation that silently gave up would move the aggregation
/// into memory -- the defect CA-06.5 declares blocking -- without changing a line of C#.
/// </summary>
internal sealed class SqlSalesReportQuery : ISalesReportQuery
{
    /// <summary>
    /// One rule, and it governs both frozen labels: the aggregation groups BY the name and BY the
    /// category the sale itself froze, and nothing picks a winner. Two labels are two rows.
    ///
    /// The alternative -- showing the label the most recent sale of the range froze -- was how this
    /// query worked until 2026-09-21, and it was defect A-7: widening the range made the older
    /// label vanish and moved its units under a name that sale never carried, so a NEW sale could
    /// change what a closed period already said. That is ADR-004's own defect arriving through the
    /// sale instead of through the live catalogue. Collapsing two labels into one would be deciding
    /// they mean the same thing, and that decision belonged to whoever renamed.
    ///
    /// No window function and no CTE: both existed only to carry the tie-break that is now gone.
    /// The live catalogue is never read -- ADR-004, CA-06.4, RNF-02.
    /// </summary>
    private const string ProductTotals = """
        SELECT item.product_id,
               item.product_name,
               item.category_name,
               SUM(item.quantity)::int AS units_sold,
               SUM(item.quantity * item.unit_price) AS revenue
        FROM sales.sale_item AS item
        JOIN sales.sale AS sale ON sale.id = item.sale_id
        WHERE sale.sold_at >= @from AND sale.sold_at < @to
        GROUP BY item.product_id, item.product_name, item.category_name
        ORDER BY revenue DESC, item.product_name ASC, item.category_name ASC
        """;

    /// <summary>
    /// The count is taken over sale and the amount over sale_item on purpose: E-13 counts sales,
    /// not lines and not units, and a sale that somehow reached the table without lines must
    /// still be counted rather than silently dropped by the join. COALESCE is what keeps the
    /// empty range answering 0 instead of null (CA-06.2).
    /// </summary>
    private const string RangeTotals = """
        SELECT (SELECT COUNT(*)
                FROM sales.sale
                WHERE sold_at >= @from AND sold_at < @to)::int AS sales_count,
               COALESCE((SELECT SUM(item.quantity * item.unit_price)
                         FROM sales.sale_item AS item
                         JOIN sales.sale AS sale ON sale.id = item.sale_id
                         WHERE sale.sold_at >= @from AND sale.sold_at < @to), 0) AS grand_total
        """;

    private readonly SalesDbContext _context;

    public SqlSalesReportQuery(SalesDbContext context) => _context = context;

    public async Task<AggregatedSales> AggregateAsync(DateRange range, CancellationToken ct = default)
    {
        // Npgsql refuses to compare a timestamptz against a DateTimeOffset whose offset is not
        // zero, and D-C3 accepts any explicit offset. Normalising picks the same instant written
        // another way, so neither edge of D-C2 moves.
        var from = range.From.ToUniversalTime();
        var to = range.To.ToUniversalTime();

        await _context.Database.OpenConnectionAsync(ct);
        try
        {
            var (salesCount, grandTotal) = await ReadRangeTotalsAsync(from, to, ct);
            return new AggregatedSales(salesCount, grandTotal, await ReadProductTotalsAsync(from, to, ct));
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    private async Task<(int SalesCount, decimal GrandTotal)> ReadRangeTotalsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        using var command = NewCommand(RangeTotals, from, to);
        await using var reader = await command.ExecuteReaderAsync(ct);

        await reader.ReadAsync(ct);
        return (reader.GetInt32(0), reader.GetDecimal(1));
    }

    private async Task<IReadOnlyList<AggregatedProduct>> ReadProductTotalsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        using var command = NewCommand(ProductTotals, from, to);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var products = new List<AggregatedProduct>();

        while (await reader.ReadAsync(ct))
        {
            products.Add(new AggregatedProduct(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetDecimal(4)));
        }

        return products;
    }

    private DbCommand NewCommand(string sql, DateTimeOffset from, DateTimeOffset to)
    {
        var command = _context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        // A report read inside an open transaction has to join it; outside one this is null and
        // the command runs on the connection as it stands.
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();

        command.Parameters.Add(Instant(command, "from", from));
        command.Parameters.Add(Instant(command, "to", to));
        return command;
    }

    private static DbParameter Instant(DbCommand command, string name, DateTimeOffset value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        return parameter;
    }
}
