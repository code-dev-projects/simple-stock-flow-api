using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SimpleStockFlow.Adapters.Rest.Binding;
using SimpleStockFlow.Adapters.Rest.Contracts;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Inbound;

namespace SimpleStockFlow.Adapters.Rest.Controllers;

/// <summary>
/// Sales. Any signed-in role may register one, because selling is the seller's job. A sale that
/// has been registered is never modified: there is no PUT and no DELETE here, and that is
/// deliberate.
/// </summary>
[ApiController]
[Authorize]
[Route("api/sales")]
public sealed class SalesController : ControllerBase
{
    private readonly IPlaceSale _placeSale;
    private readonly IGetSale _getSale;

    /// <param name="placeSale">The port that registers a sale.</param>
    /// <param name="getSale">The port that reads sales back.</param>
    public SalesController(IPlaceSale placeSale, IGetSale getSale)
    {
        _placeSale = placeSale;
        _getSale = getSale;
    }

    /// <summary>Registers a sale and discounts the stock (E-10).</summary>
    /// <remarks>
    /// The seller is not in the body: it is read from the token. All or nothing -- if one line
    /// is refused, no product is discounted at all. The order in which the rules are checked is
    /// part of the contract and is contraintuitive: a line with quantity 0 on a product that
    /// does not exist answers that the product does not exist, not that the quantity is wrong.
    /// The order is: empty lines, repeated products, unknown product, quantity, stock.
    /// </remarks>
    /// <param name="request">The lines of the sale.</param>
    /// <param name="ct">Cancellation of the request.</param>
    /// <response code="201">The identifier of the new sale, with a Location that resolves.</response>
    /// <response code="400">lines is missing, or the JSON does not bind.</response>
    /// <response code="401">No token, or a token that does not validate.</response>
    /// <response code="409">Two sales raced for the last unit and the three retries are spent.</response>
    /// <response code="422">A rule refused it: no lines, a repeated product, a product that does not exist or is withdrawn, a quantity not above zero, or not enough stock.</response>
    [HttpPost]
    [ProducesResponseType(typeof(CreatedResource), StatusCodes.Status201Created, "application/json")]
    [ValidationProblemResponse]
    [EmptyResponse(StatusCodes.Status401Unauthorized)]
    [ProblemResponse(StatusCodes.Status409Conflict)]
    [ProblemResponse(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult> Place([FromBody] PlaceSaleRequest request, CancellationToken ct)
    {
        // Both read from the token and neither from the body: the subject is the identifier the
        // foreign key holds to (T-12), and the name is frozen as it was spelled at this moment.
        var soldByUsername = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "desconocido";
        var soldByUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var lines = request.Lines.Select(line => new SaleLine(line.ProductId, line.Quantity)).ToList();
        var id = await _placeSale.HandleAsync(new PlaceSaleCommand(soldByUserId, soldByUsername, lines), ct);

        return CreatedAtAction(nameof(Get), new { id }, new CreatedResource(id));
    }

    /// <summary>One sale by identifier, with its lines (E-12).</summary>
    /// <param name="id">The identifier of the sale.</param>
    /// <param name="ct">Cancellation of the request.</param>
    /// <response code="200">The sale. The name and the unit price of each line are the ones frozen when it was sold, not the ones in the catalogue now.</response>
    /// <response code="401">No token, or a token that does not validate.</response>
    /// <response code="404">No sale carries that identifier, or it is not a uuid (D-C7).</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SaleView), StatusCodes.Status200OK, "application/json")]
    [EmptyResponse(StatusCodes.Status401Unauthorized)]
    [EmptyResponse(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SaleView>> Get(Guid id, CancellationToken ct)
    {
        var sale = await _getSale.GetAsync(id, ct);
        return sale is null ? NoSuchSale() : Ok(sale);
    }

    /// <summary>The sales of a period, paged (E-11).</summary>
    /// <remarks>
    /// Ordered by soldAt descending, the most recent first. The range takes its start and leaves
    /// out its end -- from &lt;= soldAt &lt; to -- which is what makes two consecutive ranges add up
    /// to the whole period without counting anything twice.
    /// </remarks>
    /// <param name="from">Start of the range, taken. ISO 8601 with an explicit offset, mandatory.</param>
    /// <param name="to">End of the range, left out. ISO 8601 with an explicit offset, mandatory.</param>
    /// <param name="page">1 by default. Below 1 is served as 1.</param>
    /// <param name="size">20 by default. Above 100 is served as 100.</param>
    /// <param name="ct">Cancellation of the request.</param>
    /// <response code="200">The page of sales.</response>
    /// <response code="400">from or to is missing, is not ISO 8601 with an explicit offset, or page/size do not bind.</response>
    /// <response code="401">No token, or a token that does not validate.</response>
    /// <response code="422">to is earlier than from.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<SaleView>), StatusCodes.Status200OK, "application/json")]
    [ValidationProblemResponse]
    [EmptyResponse(StatusCodes.Status401Unauthorized)]
    [ProblemResponse(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<SaleView>>> List(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        // The two edges arrive as text and are read here instead of being bound, because the
        // binder would answer for them (D-C3, D-C4): an absent edge becomes DateTimeOffset.MinValue
        // without a word, and a bare "2026-06-01" is resolved against whatever zone the host runs
        // in. Both produce a page that looks like an answer and is not one.
        //
        // Both edges are read before either is judged, so a caller that omitted both hears about
        // both instead of fixing one and being refused again.
        var start = this.ReadRangeEdge(from, nameof(from));
        var end = this.ReadRangeEdge(to, nameof(to));

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var found = await _getSale.ListAsync(
            new DateRange(start!.Value, end!.Value),
            new PageRequest(page, size),
            ct);

        return Ok(found);
    }

    /// <summary>
    /// D-C7 makes a sale that does not exist and an identifier that is not a uuid one answer,
    /// and section 2.3 of the contract gives the 404 no body. NotFound() cannot do it:
    /// [ApiController] turns every IClientErrorActionResult into problem+json while the
    /// unmatched route answers with nothing, and the difference would tell a caller which of
    /// the two happened.
    /// </summary>
    private ActionResult NoSuchSale()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return new EmptyResult();
    }
}
