using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.Identity;

namespace SimpleStockFlow.Domain.UnitTests;

/// <summary>
/// RN-11 had no test anywhere: <c>Roles.IsValid</c> was never reached from one, so nothing held
/// the closed set of two. The lowercase side of RN-10 was only exercised sideways, from the
/// application layer, which tests that the lookup normalises and not that the aggregate decides
/// the stored form.
/// </summary>
public sealed class UserTests
{
    private const string AnyHash = "hash-produced-by-the-security-adapter";

    /// <summary>
    /// Case is part of the value, not a variant of it: the set is matched ordinally, so "Admin"
    /// is as invalid as "jefe". The message is the one E-02 publishes as the 422 detail.
    /// </summary>
    [Theory]
    [InlineData("jefe")]
    [InlineData("administrator")]
    [InlineData("Admin")]
    [InlineData("SELLER")]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_role_outside_the_closed_set(string role)
    {
        var act = () => User.Create("ana", AnyHash, role);

        act.Should().Throw<DomainException>().WithMessage($"Rol no válido: '{role}'.");
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Seller)]
    public void Create_accepts_the_two_roles_of_the_closed_set(string role)
    {
        User.Create("ana", AnyHash, role).Role.Should().Be(role);
    }

    /// <summary>
    /// The role is optional in the signature and mandatory in the contract (E-02), so the
    /// fallback only ever applies to a caller inside the hexagon. It must be the one that
    /// cannot register users, or an omission would hand out administration in silence.
    /// </summary>
    [Fact]
    public void Create_falls_back_to_the_role_that_can_only_sell()
    {
        User.Create("ana", AnyHash).Role.Should().Be(Roles.Seller);
    }

    [Theory]
    [InlineData("  Ana  ", "ana")]
    [InlineData("ADMIN", "admin")]
    [InlineData("Ariel.G", "ariel.g")]
    public void Create_stores_the_username_trimmed_and_in_lowercase(string typed, string stored)
    {
        User.Create(typed, AnyHash).Username.Should().Be(stored);
    }

    /// <summary>
    /// Asserted on the function as well as through the aggregate because a lookup that skipped
    /// it would let "Ana " register a second account that can never log in: the aggregate would
    /// store it as "ana" and collide with the first.
    /// </summary>
    [Theory]
    [InlineData("  Ana  ", "ana")]
    [InlineData("ADMIN", "admin")]
    public void NormalizeUsername_is_the_one_form_a_lookup_may_use(string typed, string stored)
    {
        User.NormalizeUsername(typed).Should().Be(stored);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_demands_a_username(string username)
    {
        var act = () => User.Create(username, AnyHash);

        act.Should().Throw<DomainException>().WithMessage("El usuario es obligatorio.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_demands_a_password_hash(string passwordHash)
    {
        var act = () => User.Create("ana", passwordHash);

        act.Should().Throw<DomainException>().WithMessage("El hash de la clave es obligatorio.");
    }
}
