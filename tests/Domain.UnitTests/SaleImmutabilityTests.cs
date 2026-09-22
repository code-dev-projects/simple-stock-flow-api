using System.Collections;
using System.Reflection;
using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.Sales;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Domain.UnitTests;

/// <summary>
/// RN-07 — a registered sale is never edited nor voided, and no operation exists that would
/// allow it. The rule holds by absence, and an absence does not protest when it disappears:
/// what is asserted here is the shape of the aggregate, not the outcome of one call. Every
/// assertion is about the capacity to write, never about a particular name, so a new read
/// breaks nothing and a new writer breaks everything.
/// </summary>
public sealed class SaleImmutabilityTests
{
    /// <summary>
    /// The writers the design declares. A member that can write and is missing from this list
    /// has to be argued for right here before the suite goes green again, which is the whole
    /// point of the file.
    /// </summary>
    private static readonly string[] DeclaredSaleWriters = ["Open", "AddItem", "EnsureConfirmable"];

    [Fact]
    public void The_collection_of_lines_handed_out_refuses_every_mutation()
    {
        var sale = ASaleWithOneLine();
        var exposed = sale.Items;
        var line = exposed.Single();

        foreach (var (attempt, mutate) in MutationsStillReachableOn(exposed, line))
        {
            mutate.Should().Throw<NotSupportedException>(
                "Sale.Items must refuse {0}; an aggregate that hands out its own list accepts it",
                attempt);
        }

        sale.Items.Should().ContainSingle("no attempt from outside may change what the sale holds");
    }

    [Fact]
    public void The_sale_exposes_no_public_way_to_write_beyond_the_declared_operations()
    {
        WritersOf(typeof(Sale)).Should().BeEquivalentTo(
            DeclaredSaleWriters,
            "RN-07 forbids altering a sale: a new public operation that writes has to be justified here first");
    }

    [Fact]
    public void The_line_of_a_sale_exposes_no_public_way_to_write_at_all()
    {
        WritersOf(typeof(SaleItem)).Should().BeEmpty(
            "a line is frozen the moment it is created: quantity, name and price included");
    }

    [Fact]
    public void Nothing_on_the_sale_or_its_lines_can_be_assigned_from_outside()
    {
        AssignableMembersOf(typeof(Sale)).Should().BeEmpty(
            "a public setter or a writable field reopens the sale without any method being added");

        AssignableMembersOf(typeof(SaleItem)).Should().BeEmpty(
            "the frozen price of a line is only frozen while nobody can assign it");
    }

    [Fact]
    public void No_public_member_of_the_sale_hands_out_a_writable_collection_of_lines()
    {
        var leaks = PublicReturnTypesOf(typeof(Sale))
            .Where(static returned => typeof(ICollection<SaleItem>).IsAssignableFrom(returned))
            .Select(static returned => returned.Name);

        leaks.Should().BeEmpty(
            "handing the lines back through a writable interface removes the sale's lines from the aggregate's control");
    }

    private static Sale ASaleWithOneLine()
    {
        var sale = Sale.Open(DateTimeOffset.UtcNow, Guid.NewGuid(), "ariel");
        sale.AddItem(Product.Create("Mouse", new Money(50_000m), 5, Guid.NewGuid()), new Quantity(2), "Herramientas");
        return sale;
    }

    /// <summary>
    /// Every way of writing to the returned collection that the runtime still offers a caller.
    /// An aggregate that returns its own list reaches all of them and refuses none; one that
    /// returns a read-only view reaches them and refuses each.
    /// </summary>
    private static IEnumerable<(string Attempt, Action Mutate)> MutationsStillReachableOn(
        IReadOnlyCollection<SaleItem> exposed,
        SaleItem line)
    {
        if (exposed is ICollection<SaleItem> collection)
        {
            yield return ("Add", () => collection.Add(line));
            yield return ("Remove", () => collection.Remove(line));
            yield return ("Clear", collection.Clear);
        }

        if (exposed is IList<SaleItem> list)
        {
            yield return ("assignment through the indexer", () => list[0] = line);
            yield return ("Insert", () => list.Insert(0, line));
            yield return ("RemoveAt", () => list.RemoveAt(0));
        }

        if (exposed is IList untyped)
        {
            yield return ("the untyped Add", () => untyped.Add(line));
            yield return ("the untyped Remove", () => untyped.Remove(line));
        }
    }

    /// <summary>
    /// A method counts as able to write when it gives nothing back, or when it gives back the
    /// very type it belongs to — the first is a command, the second the fluent disguise of one.
    /// A method that answers a question, returning an amount, a count or a verdict, is a read
    /// and never registers here, so adding one costs nothing.
    /// </summary>
    private static IEnumerable<string> WritersOf(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(static method => method.DeclaringType != typeof(object))
            .Where(static method => !method.IsSpecialName)
            .Where(method => GivesNothingBack(method.ReturnType) || method.ReturnType == type)
            .Select(static method => method.Name)
            .Distinct();

    private static bool GivesNothingBack(Type returnType) =>
        returnType == typeof(void) || returnType == typeof(Task) || returnType == typeof(ValueTask);

    /// <summary>Inherited members count: a writer added to the base class is a writer here.</summary>
    private static IEnumerable<string> AssignableMembersOf(Type type)
    {
        var settable = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(static property => property.SetMethod is { IsPublic: true })
            .Select(static property => $"{property.DeclaringType!.Name}.{property.Name} has a public setter");

        var writableFields = type
            .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(static field => !field.IsInitOnly && !field.IsLiteral)
            .Select(static field => $"{field.DeclaringType!.Name}.{field.Name} is a writable public field");

        return settable.Concat(writableFields);
    }

    private static IEnumerable<Type> PublicReturnTypesOf(Type type)
    {
        var fromProperties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(static property => property.PropertyType);

        var fromMethods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(static method => method.DeclaringType != typeof(object))
            .Select(static method => method.ReturnType);

        return fromProperties.Concat(fromMethods);
    }
}
