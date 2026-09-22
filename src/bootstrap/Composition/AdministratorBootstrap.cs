using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Identity;

namespace SimpleStockFlow.Bootstrap.Composition;

/// <summary>
/// Creates the first administrator from configuration, because registering demands an
/// administrator token and a fresh database has nobody to mint one. The credentials come from
/// the environment and are never versioned (article IX).
/// </summary>
public static class AdministratorBootstrap
{
    public const string UsernameKey = "Bootstrap:AdminUsername";
    public const string PasswordKey = "Bootstrap:AdminPassword";

    public static async Task EnsureAdministratorAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var username = configuration[UsernameKey];
        var password = configuration[PasswordKey];

        // No default credentials, not even a weak one: a system with no way in is safer than
        // one with a way in that everybody already knows.
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return;

        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        if (await users.FindByUsernameAsync(User.NormalizeUsername(username), ct) is not null)
            return;

        // Not IAuthenticate: since DP-04 that port refuses to create an administrator, and this is
        // the one place allowed to -- the deployment, once, before anybody can sign in.
        var provisioning = scope.ServiceProvider.GetRequiredService<IProvisionAdministrator>();
        await provisioning.ProvisionAdministratorAsync(new Credentials(username, password), ct);
    }
}
