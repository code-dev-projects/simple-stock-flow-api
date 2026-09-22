namespace SimpleStockFlow.Application.Ports.Inbound;

public interface IAuthenticate
{
    Task<AuthResult> LoginAsync(Credentials credentials, CancellationToken ct = default);
    Task<Guid> RegisterAsync(Credentials credentials, string role, CancellationToken ct = default);
}

public sealed record Credentials(string Username, string Password);

public sealed record AuthResult(string AccessToken, DateTimeOffset ExpiresAt, string Username, string Role);
