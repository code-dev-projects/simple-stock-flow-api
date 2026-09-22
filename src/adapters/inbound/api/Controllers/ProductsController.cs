using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SimpleStockFlow.Adapters.Rest.Contracts;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Inbound;

namespace SimpleStockFlow.Adapters.Rest.Controllers;

/// <summary>
/// The catalogue. Reading is open to any signed-in role; writing is for administrators.
/// The operations by identifier -- get, replace, remove and the image -- do not distinguish a
/// withdrawn product from an active one (ADR-003): only the listing hides the withdrawn ones,
/// which is what lets a historic sale line still resolve its product.
/// </summary>
[ApiController]
[Authorize]
[Route("api/products")]
public sealed class ProductsController : ControllerBase
{
    private readonly IManageProducts _products;

    /// <param name="products">The inbound port; the controller knows no other way in.</param>
    public ProductsController(IManageProducts products) => _products = products;

    /// <summary>The catalogue, paged and filterable (E-03).</summary>
    /// <remarks>
    /// Ordered by name ascending, as a contract and not as an accident. Withdrawn products never
    /// appear, not even when their category is the one being filtered on.
    /// </remarks>
    /// <param name="search">Partial match on the name, case insensitive. Absent means no filter.</param>
    /// <param name="categoryId">Restricts to one category. A value that is not a uuid is a 400.</param>
    /// <param name="page">1 by default. Below 1 is served as 1.</param>
    /// <param name="size">20 by default. Above 100 is served as 100, and the answer reports the size it served, not the one asked for.</param>
    /// <param name="ct">Cancellation of the request.</param>
    /// <response code="200">The page, with the total and the page count.</response>
    /// <response code="400">categoryId, page or size do not bind.</response>
    /// <response code="401">No token, or a token that does not validate.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProductView>), StatusCodes.Status200OK, "application/json")]
    [ValidationProblemResponse]
    [EmptyResponse(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<ProductView>>> List(
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default) =>
        Ok(await _products.ListAsync(new ProductFilter(search, categoryId), new PageRequest(page, size), ct));

    /// <summary>One product by identifier (E-04).</summary>
    /// <remarks>Answers withdrawn products too, so the detail of an old sale can resolve them.</remarks>
    /// <param name="id">The identifier of the product.</param>
    /// <param name="ct">Cancellation of the request.</param>
    /// <response code="200">The product.</response>
    /// <response code="401">No token, or a token that does not validate.</response>
    /// <response code="404">No product carries that identifier, or the identifier is not a uuid. Both answer the same and neither has a body (D-C7).</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductView), StatusCodes.Status200OK, "application/json")]
    [EmptyResponse(StatusCodes.Status401Unauthorized)]
    [EmptyResponse(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductView>> Get(Guid id, CancellationToken ct)
    {
        var product = await _products.GetAsync(id, ct);
        return product is null ? NoSuchProduct() : Ok(product);
    }

    /// <summary>Creates a product (E-05). Administrators only.</summary>
    /// <remarks>
    /// Neither the currency nor the image travel here: the system is single-currency, and the
    /// image is a separate request. The order in which the rules are checked is part of the
    /// contract, so the message is deterministic: the binding of the JSON first, then the name,
    /// the price and the category, and the stock last.
    /// </remarks>
    /// <param name="request">Name, price, stock and category.</param>
    /// <param name="ct">Cancellation of the request.</param>
    /// <response code="201">The identifier of the new product, with a Location that resolves.</response>
    /// <response code="400">A field is missing or a type does not bind.</response>
    /// <response code="401">No token, or a token that does not validate.</response>
    /// <response code="403">A token whose role is not admin.</response>
    /// <response code="422">A rule refused it: empty name, price not above zero, negative stock, or a category that does not exist.</response>
    [HttpPost]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(CreatedResource), StatusCodes.Status201Created, "application/json")]
    [ValidationProblemResponse]
    [EmptyResponse(StatusCodes.Status401Unauthorized)]
    [EmptyResponse(StatusCodes.Status403Forbidden)]
    [ProblemResponse(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult> Create([FromBody] CreateProductRequest request, CancellationToken ct)
    {
        var id = await _products.CreateAsync(
            new CreateProductCommand(request.Name, request.Price, request.Stock, request.CategoryId), ct);

        return CreatedAtAction(nameof(Get), new { id }, new CreatedResource(id));
    }

    /// <summary>Replaces a product (E-06). Administrators only.</summary>
    /// <remarks>
    /// A replacement, not a patch: every field is mandatory and the identifier in the route is
    /// the one that decides which row is written.
    /// </remarks>
    /// <param name="id">The identifier of the product to replace.</param>
    /// <param name="request">The complete new state.</param>
    /// <param name="ct">Cancellation of the request.</param>
    /// <response code="204">Written. No body.</response>
    /// <response code="400">The body does not bind.</response>
    /// <response code="401">No token, or a token that does not validate.</response>
    /// <response code="403">A token whose role is not admin.</response>
    /// <response code="404">No product carries that identifier, or it is not a uuid.</response>
    /// <response code="409">Another operation changed the row and the three retries are spent (ADR-002).</response>
    /// <response code="422">The same rules that refuse a creation.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ValidationProblemResponse]
    [EmptyResponse(StatusCodes.Status401Unauthorized)]
    [EmptyResponse(StatusCodes.Status403Forbidden)]
    [EmptyResponse(StatusCodes.Status404NotFound)]
    [ProblemResponse(StatusCodes.Status409Conflict)]
    [ProblemResponse(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult> Update(Guid id, [FromBody] UpdateProductRequest request, CancellationToken ct)
    {
        var updated = await _products.UpdateAsync(
            new UpdateProductCommand(id, request.Name, request.Price, request.Stock, request.CategoryId), ct);

        return updated ? NoContent() : NoSuchProduct();
    }

    /// <summary>Withdraws a product (E-07). Administrators only.</summary>
    /// <remarks>
    /// The row is not deleted (ADR-003). The product leaves the catalogue and cannot be sold any
    /// more, while the sales that contain it stay intact and it keeps appearing in the report of
    /// the period it was sold in. Withdrawing an already withdrawn product answers 204 again.
    /// </remarks>
    /// <param name="id">The identifier of the product to withdraw.</param>
    /// <param name="ct">Cancellation of the request.</param>
    /// <response code="204">Withdrawn. No body.</response>
    /// <response code="401">No token, or a token that does not validate.</response>
    /// <response code="403">A token whose role is not admin.</response>
    /// <response code="404">No product carries that identifier, or it is not a uuid.</response>
    /// <response code="409">Another operation changed the row and the three retries are spent.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [EmptyResponse(StatusCodes.Status401Unauthorized)]
    [EmptyResponse(StatusCodes.Status403Forbidden)]
    [EmptyResponse(StatusCodes.Status404NotFound)]
    [ProblemResponse(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var removed = await _products.DeleteAsync(id, ct);

        return removed ? NoContent() : NoSuchProduct();
    }

    /// <summary>Attaches an image to a product (E-08). Administrators only.</summary>
    /// <remarks>
    /// The field is called file, exactly. The name the client sends is never reused: the stored
    /// key is a fresh identifier plus the original extension, which is what rules out path
    /// traversal and collisions. Accepted types are image/jpeg, image/png and image/webp, and
    /// the ceiling is 5 MB -- above roughly 28.6 MB the request never reaches this code, because
    /// the server stops reading it and answers 400 without saying it was the size.
    /// </remarks>
    /// <param name="id">The identifier of the product the image belongs to.</param>
    /// <param name="file">The image itself.</param>
    /// <param name="ct">Cancellation of the request.</param>
    /// <response code="200">The relative address the image will be served at.</response>
    /// <response code="400">The file field is missing, or the request is past the server's ceiling.</response>
    /// <response code="401">No token, or a token that does not validate.</response>
    /// <response code="403">A token whose role is not admin.</response>
    /// <response code="404">No product carries that identifier, or it is not a uuid.</response>
    /// <response code="422">The content type is not allowed, or the image is over 5 MB.</response>
    [HttpPost("{id:guid}/image")]
    [Authorize(Roles = "admin")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(StoredImage), StatusCodes.Status200OK, "application/json")]
    [ValidationProblemResponse]
    [EmptyResponse(StatusCodes.Status401Unauthorized)]
    [EmptyResponse(StatusCodes.Status403Forbidden)]
    [EmptyResponse(StatusCodes.Status404NotFound)]
    [ProblemResponse(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult> UploadImage(Guid id, IFormFile file, CancellationToken ct)
    {
        // IFormFile dies here: the port only knows ImageUpload.
        await using var stream = file.OpenReadStream();
        var url = await _products.AttachImageAsync(
            id, new ImageUpload(stream, file.FileName, file.ContentType, file.Length), ct);

        return url is null ? NoSuchProduct() : Ok(new StoredImage(url));
    }

    /// <summary>
    /// D-C7 decides that a product that does not exist and an identifier that is not a uuid are
    /// one answer from outside, and section 2.3 of the contract gives the 404 no body at all.
    /// Neither NotFound() nor StatusCode(404) can do it: [ApiController] turns every
    /// IClientErrorActionResult into problem+json, while the unmatched route answers with
    /// nothing, and the difference would tell a caller which of the two happened.
    /// </summary>
    private ActionResult NoSuchProduct()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return new EmptyResult();
    }
}
