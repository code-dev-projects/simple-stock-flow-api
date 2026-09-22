using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using SimpleStockFlow.Adapters.Rest.Controllers;

namespace SimpleStockFlow.Adapters.IntegrationTests;

/// <summary>
/// RN-07 at the edge: the API publishes no way to rewrite or erase a sale. The check is on the
/// verbs the routing table would expose, not on the names of the actions, and it covers every
/// controller mounted under the sales route rather than only the one that exists today — a
/// second controller is exactly how the rule would be lost in silence.
/// </summary>
public sealed class SalesEndpointsRefuseMutationTests
{
    private const string SalesRoute = "api/sales";

    private static readonly string[] VerbsThatRewriteOrErase = ["PUT", "PATCH", "DELETE"];

    /// <summary>The verbs that cannot change anything, so they need no justification.</summary>
    private static readonly string[] VerbsThatOnlyRead = ["GET", "HEAD", "OPTIONS"];

    [Fact]
    public void Nothing_under_the_sales_route_answers_a_verb_that_rewrites_or_erases()
    {
        var published = EverythingPublishedUnderSales();

        published.Should().NotBeEmpty(
            "the assertion means nothing if it found no endpoint to look at");

        published
            .Where(endpoint => VerbsThatRewriteOrErase.Contains(endpoint.Verb))
            .Select(Describe)
            .Should().BeEmpty(
                "RN-07: a registered sale is neither modified nor voided, so no such operation exists");
    }

    [Fact]
    public void The_only_write_under_the_sales_route_registers_a_new_sale_at_the_collection()
    {
        EverythingPublishedUnderSales()
            .Where(endpoint => !VerbsThatOnlyRead.Contains(endpoint.Verb))
            .Select(Describe)
            .Should().BeEquivalentTo(
                [$"{nameof(SalesController)}.{nameof(SalesController.Place)} answers POST on {SalesRoute}"],
                "a write addressed to one sale would be an edit of that sale whatever verb it travelled on");
    }

    private static string Describe((MethodInfo Action, string Verb, string Route) endpoint) =>
        $"{endpoint.Action.DeclaringType!.Name}.{endpoint.Action.Name} answers {endpoint.Verb} on {endpoint.Route}";

    private static IReadOnlyList<(MethodInfo Action, string Verb, string Route)> EverythingPublishedUnderSales() =>
        typeof(SalesController).Assembly.GetTypes()
            .Where(static type => type is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(static type => typeof(ControllerBase).IsAssignableFrom(type))
            .Where(static type => RouteOf(type).StartsWith(SalesRoute, StringComparison.OrdinalIgnoreCase))
            .SelectMany(PublishedBy)
            .ToList();

    private static IEnumerable<(MethodInfo Action, string Verb, string Route)> PublishedBy(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static action => !action.IsSpecialName)
            .SelectMany(action => action
                .GetCustomAttributes<HttpMethodAttribute>(inherit: true)
                .SelectMany(attribute => attribute.HttpMethods.Select(verb =>
                    (Action: action, Verb: verb.ToUpperInvariant(), Route: RouteUnder(controller, action, attribute)))));

    private static string RouteOf(Type controller) =>
        controller.GetCustomAttributes<RouteAttribute>(inherit: true).FirstOrDefault()?.Template ?? string.Empty;

    /// <summary>
    /// The template the action adds to its controller's, from the verb attribute or from a
    /// Route of its own. Without it a POST aimed at one sale would read the same as the POST
    /// that registers one.
    /// </summary>
    private static string RouteUnder(Type controller, MethodInfo action, IRouteTemplateProvider verb)
    {
        var own = verb.Template
            ?? action.GetCustomAttributes<RouteAttribute>(inherit: true).FirstOrDefault()?.Template;

        return string.IsNullOrWhiteSpace(own)
            ? RouteOf(controller)
            : $"{RouteOf(controller)}/{own.TrimStart('/')}";
    }
}
