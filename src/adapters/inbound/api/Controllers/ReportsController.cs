using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SimpleStockFlow.Adapters.Rest.Binding;
using SimpleStockFlow.Adapters.Rest.Contracts;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Inbound;

namespace SimpleStockFlow.Adapters.Rest.Controllers;

/// <summary>What was sold in a period, aggregated by product.</summary>
[ApiController]
[Authorize]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly IGetSalesReport _report;

    /// <param name="report">The inbound port; the aggregation happens in the engine, not here.</param>
    public ReportsController(IGetSalesReport report) => _report = report;

    /// <summary>The sales report of a period (E-13).</summary>
    /// <remarks>
    /// One row per product, ordered by revenue descending. It is not paged, and there is no
    /// filter or grouping by seller on purpose. Asking again for a closed period answers exactly
    /// the same, however much the catalogue has changed since, because the name, the price and
    /// the category of each line were frozen when the sale happened. Withdrawn products that
    /// were sold inside the range are included. A period with no sales is a report with
    /// grandTotal 0 and rows [], never an error.
    ///
    /// Known gap, and it is not hidden here: categoryName travels empty in every row, because
    /// the sale line does not store it yet (T-11). The contract declares it a non-null string,
    /// so the system does not meet its own contract in that field today.
    /// </remarks>
    /// <param name="from">Start of the range, taken. ISO 8601 with an explicit offset, mandatory.</param>
    /// <param name="to">End of the range, left out. ISO 8601 with an explicit offset, mandatory.</param>
    /// <param name="ct">Cancellation of the request.</param>
    /// <response code="200">The report. The two edges come back as they were received.</response>
    /// <response code="400">from or to is missing, or is not ISO 8601 with an explicit offset.</response>
    /// <response code="401">No token, or a token that does not validate.</response>
    /// <response code="422">to is earlier than from.</response>
    [HttpGet("sales")]
    [ProducesResponseType(typeof(SalesReport), StatusCodes.Status200OK, "application/json")]
    [ValidationProblemResponse]
    [EmptyResponse(StatusCodes.Status401Unauthorized)]
    [ProblemResponse(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SalesReport>> Sales(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct = default)
    {
        // The two edges arrive as text and are read here instead of being bound, because the
        // binder would answer for them (D-C3, D-C4). Both defects produce a report rather than a
        // refusal, which is why they matter more here than anywhere else: an absent edge binds to
        // DateTimeOffset.MinValue and returns an empty report that reads as "nothing was sold",
        // and a bare "2026-06-01" is resolved against whatever zone the host runs in, so the same
        // request reports a different period on another machine.
        //
        // Both edges are read before either is judged, so a caller that omitted both hears about
        // both instead of fixing one and being refused again.
        var start = this.ReadRangeEdge(from, nameof(from));
        var end = this.ReadRangeEdge(to, nameof(to));

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        return Ok(await _report.HandleAsync(new DateRange(start!.Value, end!.Value), ct));
    }
}
