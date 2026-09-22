using System.Reflection;
using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Sales;

namespace SimpleStockFlow.Application.UnitTests;

/// <summary>
/// RN-07 seen from the application layer: a sale cannot be altered from outside the aggregate
/// either. The ports are the only doors the use cases have onto a stored sale, so the rule is
/// asserted on their shape. Nothing here names a forbidden operation — a blocklist of names is
/// defeated by renaming it — it states what every member is allowed to be.
/// </summary>
public sealed class SaleWriteSurfaceTests
{
    /// <summary>
    /// The single member of the outbound port that may take a sale in. Adding a second one has
    /// to be argued for here first, and that argument is the reason this list exists.
    /// </summary>
    private static readonly string[] DeclaredRepositoryWriters = ["AddAsync"];

    [Fact]
    public void The_outbound_port_lets_a_sale_in_only_to_append_it()
    {
        MembersThatDoNotOnlyRead(typeof(ISaleRepository), typeof(Sale))
            .Should().BeEquivalentTo(
                DeclaredRepositoryWriters,
                "RN-07: once a sale is stored there is no operation that updates it or erases it");
    }

    [Fact]
    public void The_inbound_port_that_serves_sales_only_reads_them()
    {
        MembersThatDoNotOnlyRead(typeof(IGetSale), typeof(SaleView))
            .Should().BeEmpty(
                "the use case that serves a sale is read-only and stays that way (T-07)");
    }

    /// <summary>
    /// A reading member hands the sale out and takes none in: it is asked with coordinates —
    /// an identifier, a date range, a page — and answers with sales. Anything else is a member
    /// that can write, whether it gives nothing back, accepts a sale, or answers something that
    /// is not a sale at all, which is what a delete returning a flag looks like.
    /// </summary>
    private static IEnumerable<string> MembersThatDoNotOnlyRead(Type port, Type carried) =>
        port.GetMethods()
            .Where(member => !OnlyReads(member, carried))
            .Select(static member => member.Name);

    private static bool OnlyReads(MethodInfo member, Type carried) =>
        Carries(member.ReturnType, carried)
        && !member.GetParameters().Any(parameter => Carries(parameter.ParameterType, carried));

    /// <summary>Looks through the wrappers: Task, PagedResult and the lists all nest the type.</summary>
    private static bool Carries(Type type, Type carried) =>
        type == carried
        || (type.IsGenericType && type.GetGenericArguments().Any(argument => Carries(argument, carried)));
}
