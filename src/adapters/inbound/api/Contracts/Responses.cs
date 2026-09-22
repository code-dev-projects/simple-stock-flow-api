using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SimpleStockFlow.Adapters.Rest.Contracts;

/// <summary>
/// The body of every 201 this API answers. It exists so the generated documentation can show
/// the shape: an anonymous object serialises to the same JSON but carries no type for a schema,
/// and a reader of the page would see a 201 with nothing in it.
/// </summary>
/// <param name="Id">The identifier of the resource that was just created.</param>
public sealed record CreatedResource(Guid Id);

/// <summary>The address of a stored image, as it will later appear in the product's imageUrl.</summary>
/// <param name="Url">Relative path under the media base, never absolute.</param>
public sealed record StoredImage(string Url);

/// <summary>
/// A 401, a 403 or a 404: the status code travels alone, with Content-Length 0
/// (api-contract.md section 2.3).
/// </summary>
public sealed class EmptyResponseAttribute : ProducesResponseTypeAttribute
{
    /// <param name="statusCode">The status code answered with no body at all.</param>
    public EmptyResponseAttribute(int statusCode) : base(statusCode)
    {
    }
}

/// <summary>
/// A 409 or a 422 from <see cref="Filters.ExceptionTranslationFilter"/>: problem+json whose
/// <c>detail</c> carries the domain message, in Spanish, and is what the person using the
/// system ends up reading (api-contract.md section 2.1).
/// </summary>
public sealed class ProblemResponseAttribute : ProducesResponseTypeAttribute
{
    /// <param name="statusCode">The status code the filter translates the exception into.</param>
    public ProblemResponseAttribute(int statusCode)
        : base(typeof(ProblemDetails), statusCode, "application/problem+json")
    {
    }
}

/// <summary>
/// The 400 of model validation, which carries <c>errors</c> and, unlike every other error body,
/// no <c>detail</c> — the gap D-C9 of the contract records and H-2 owns (api-contract.md
/// section 2.2).
/// </summary>
public sealed class ValidationProblemResponseAttribute : ProducesResponseTypeAttribute
{
    /// <summary>Always 400: no other status code carries this body.</summary>
    public ValidationProblemResponseAttribute()
        : base(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")
    {
    }
}
