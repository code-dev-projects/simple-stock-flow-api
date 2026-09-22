using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using SimpleStockFlow.Adapters.Rest.Filters;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Domain.Common;

namespace SimpleStockFlow.Adapters.IntegrationTests;

/// <summary>
/// The single place where an exception becomes a status code. Each case is pinned here
/// because the difference between 422 and 409 is the difference between "you asked for
/// something impossible" and "try again", and the client acts differently on each.
/// </summary>
public sealed class ExceptionTranslationFilterTests
{
    private readonly ExceptionTranslationFilter _sut = new();

    private static ExceptionContext ContextFor(Exception exception) =>
        new(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>())
        {
            Exception = exception,
        };

    [Fact]
    public void A_broken_business_rule_becomes_422()
    {
        var context = ContextFor(new DomainException("El stock es insuficiente."));

        _sut.OnException(context);

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        result.Value.Should().BeOfType<ProblemDetails>()
            .Which.Detail.Should().Be("El stock es insuficiente.");
        context.ExceptionHandled.Should().BeTrue();
    }

    [Fact]
    public void A_version_conflict_becomes_409()
    {
        var context = ContextFor(new ConcurrencyConflictException("Otra operación se adelantó."));

        _sut.OnException(context);

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        result.Value.Should().BeOfType<ProblemDetails>()
            .Which.Detail.Should().Be("Otra operación se adelantó.");
        context.ExceptionHandled.Should().BeTrue();
    }

    /// <summary>
    /// Anything the filter does not recognise must stay unhandled: swallowing it here would
    /// turn a defect into a tidy response body and it would never reach the logs as a 500.
    /// </summary>
    [Fact]
    public void An_unrecognised_failure_is_left_for_the_host_to_report()
    {
        var context = ContextFor(new InvalidOperationException("The port is not wired."));

        _sut.OnException(context);

        context.Result.Should().BeNull();
        context.ExceptionHandled.Should().BeFalse();
    }
}
