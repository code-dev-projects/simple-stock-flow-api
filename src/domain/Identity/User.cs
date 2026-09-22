using SimpleStockFlow.Domain.Common;

namespace SimpleStockFlow.Domain.Identity;

public sealed class User : AggregateRoot<Guid>
{
    public string Username { get; private set; } = null!;

    /// <summary>Hash + salt already computed by the security adapter. The domain never sees the plain password.</summary>
    public string PasswordHash { get; private set; } = null!;

    public string Role { get; private set; } = Roles.Seller;

    private User() { }

    private User(Guid id, string username, string passwordHash, string role) : base(id)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new DomainException("El usuario es obligatorio.");
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("El hash de la clave es obligatorio.");
        if (!Roles.IsValid(role))
            throw new DomainException($"Rol no válido: '{role}'.");

        Username = NormalizeUsername(username);
        PasswordHash = passwordHash;
        Role = role;
    }

    public static User Create(string username, string passwordHash, string role = Roles.Seller) =>
        new(Guid.NewGuid(), username, passwordHash, role);

    /// <summary>
    /// The one form a username is ever stored in. Callers that look a user up must go through
    /// here: a lookup that skipped it would let "Ana " register a second account that can never
    /// log in, because the aggregate would store it as "ana" and collide.
    /// </summary>
    public static string NormalizeUsername(string username) =>
        username.Trim().ToLowerInvariant();
}

public static class Roles
{
    public const string Admin = "admin";
    public const string Seller = "seller";

    public static bool IsValid(string role) =>
        role is Admin or Seller;
}
