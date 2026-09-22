using Microsoft.Extensions.DependencyInjection;
using SimpleStockFlow.Application.Ports.Outbound;

namespace SimpleStockFlow.Adapters.Storage;

public static class StorageAdapter
{
    public static IServiceCollection AddLocalStorageAdapter(
        this IServiceCollection services,
        Action<LocalStorageOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        return services;
    }
}
