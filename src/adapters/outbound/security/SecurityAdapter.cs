using Microsoft.Extensions.DependencyInjection;
using SimpleStockFlow.Application.Ports.Outbound;

namespace SimpleStockFlow.Adapters.Security;

public static class SecurityAdapter
{
    public static IServiceCollection AddSecurityAdapter(this IServiceCollection services, Action<JwtOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenGenerator, JwtTokenGenerator>();
        return services;
    }
}
