using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SimpleStockFlow.Adapters.Rest.Contracts;
using SimpleStockFlow.Application.Ports.Inbound;

namespace SimpleStockFlow.Adapters.Rest.Controllers;

/// <summary>
/// Read only, and not by omission: categories are seed reference data that no port creates,
/// renames or removes (D-10). It lives on the product port because a category exists in this
/// system for exactly one reason -- to be picked in a product form.
/// </summary>
[ApiController]
[Authorize]
[Route("api/categories")]
public sealed class CategoriesController : ControllerBase
{
    private readonly IManageProducts _products;

    /// <param name="products">The product port, which owns the reference data too.</param>
    public CategoriesController(IManageProducts products) => _products = products;

    /// <summary>Every category, for the selector of a product form (E-09).</summary>
    /// <remarks>
    /// Ordered by name ascending, with the collation of the database, which is en_US.utf8 and
    /// not ASCII: "Fontanería" comes after "Electricidad" and before "General". There is no
    /// paging, no filter and no search, and there is no POST, PUT or DELETE either.
    /// </remarks>
    /// <param name="ct">Cancellation of the request.</param>
    /// <response code="200">A flat array. Empty is [], never a 204 and never a 404.</response>
    /// <response code="401">No token, or a token that does not validate.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryView>), StatusCodes.Status200OK, "application/json")]
    [EmptyResponse(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<CategoryView>>> List(CancellationToken ct) =>
        Ok(await _products.ListCategoriesAsync(ct));
}
