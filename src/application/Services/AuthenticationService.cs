using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.Identity;

namespace SimpleStockFlow.Application.Services;

public sealed class AuthenticationService : IAuthenticate, IProvisionAdministrator
{
    /// <summary>
    /// One refusal for both a missing user and a bad password (CA-07.2). Two different
    /// messages would turn the login form into a username enumerator.
    /// </summary>
    private const string InvalidCredentials = "Usuario o contraseña incorrectos.";

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenGenerator _tokens;
    private readonly IUnitOfWork _unitOfWork;

    public AuthenticationService(
        IUserRepository users,
        IPasswordHasher hasher,
        ITokenGenerator tokens,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _hasher = hasher;
        _tokens = tokens;
        _unitOfWork = unitOfWork;
    }

    public async Task<AuthResult> LoginAsync(Credentials credentials, CancellationToken ct = default)
    {
        var user = await _users.FindByUsernameAsync(User.NormalizeUsername(credentials.Username), ct);

        // The hash is checked whether or not the user exists, so both refusals cost the same.
        var passwordMatches = _hasher.Verify(credentials.Password, user?.PasswordHash ?? _hasher.DecoyHash);

        if (user is null || !passwordMatches)
            throw new DomainException(InvalidCredentials);

        var (token, expiresAt) = _tokens.Generate(user);
        return new AuthResult(token, expiresAt, user.Username, user.Role);
    }

    /// <summary>
    /// DP-04, decided 2026-09-20: a running system only ever gains sellers. The refusal is checked
    /// before the name is looked up so that asking for an administrator is refused for what it is
    /// rather than for a name that happens to be taken.
    /// </summary>
    public Task<Guid> RegisterAsync(Credentials credentials, string role, CancellationToken ct = default)
    {
        if (role == Roles.Admin)
            throw new DomainException(
                "Solo se pueden dar de alta vendedores. El administrador lo crea el despliegue.");

        return CreateAsync(credentials, role, ct);
    }

    public Task<Guid> ProvisionAdministratorAsync(Credentials credentials, CancellationToken ct = default) =>
        CreateAsync(credentials, Roles.Admin, ct);

    private async Task<Guid> CreateAsync(Credentials credentials, string role, CancellationToken ct)
    {
        var username = User.NormalizeUsername(credentials.Username);

        if (await _users.FindByUsernameAsync(username, ct) is not null)
            throw new DomainException($"El usuario '{username}' ya existe.");

        var user = User.Create(username, _hasher.Hash(credentials.Password), role);

        await _users.AddAsync(user, ct);
        await _unitOfWork.CommitAsync(ct);

        return user.Id;
    }
}
