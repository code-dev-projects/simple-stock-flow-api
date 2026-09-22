using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Domain.Common;

namespace SimpleStockFlow.Adapters.Rest.Filters;

/// <summary>
/// The only place where an exception becomes a status code. Neither the domain nor the
/// application knows the protocol exists, so the whole translation table lives here; a
/// business try/catch inside a controller would put the same table in two places.
/// </summary>
public sealed class ExceptionTranslationFilter : IExceptionFilter
{
    /// <inheritdoc />
    public void OnException(ExceptionContext context)
    {
        var problem = Translate(context.Exception);
        if (problem is null)
            return;

        context.Result = new ObjectResult(problem) { StatusCode = problem.Status };
        context.ExceptionHandled = true;
    }

    /// <summary>Null means "not ours": the host reports it as a 500 and it reaches the logs.</summary>
    private static ProblemDetails? Translate(Exception exception) => exception switch
    {
        // The request was well formed and the rule is what refused it, so it is not a 400.
        DomainException rule => new ProblemDetails
        {
            Title = "Regla de negocio violada",
            Detail = rule.Message,
            Status = StatusCodes.Status422UnprocessableEntity,
        },

        // By the time this arrives the retries are spent (ADR-002). The client is being asked
        // to try again, not told it did anything wrong: that is why it is not a 422.
        ConcurrencyConflictException conflict => new ProblemDetails
        {
            Title = "Conflicto con otra operación simultánea",
            Detail = conflict.Message,
            Status = StatusCodes.Status409Conflict,
        },

        _ => null,
    };
}
