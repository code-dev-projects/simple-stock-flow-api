namespace SimpleStockFlow.Application.Ports.Inbound;

/// <summary>
/// Creating the first administrator is a deployment act, not a use case of the running system:
/// DP-04 says a running system gains sellers and nothing else. Keeping it off
/// <see cref="IAuthenticate"/> is what makes that structural instead of a check -- the HTTP
/// adapter is wired to the registration port and cannot reach this one at all.
/// </summary>
public interface IProvisionAdministrator
{
    Task<Guid> ProvisionAdministratorAsync(Credentials credentials, CancellationToken ct = default);
}
