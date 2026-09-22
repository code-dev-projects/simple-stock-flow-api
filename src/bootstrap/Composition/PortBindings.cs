using SimpleStockFlow.Adapters.Persistence;
using SimpleStockFlow.Adapters.Rest;
using SimpleStockFlow.Adapters.Security;
using SimpleStockFlow.Adapters.Storage;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Application.Services;

namespace SimpleStockFlow.Bootstrap.Composition;

/// <summary>
/// THE composition point of the hexagon: the only file in the repository where a port meets
/// its adapter. An infrastructure "new" anywhere outside here means the hexagon is broken.
/// </summary>
internal static class PortBindings
{
    public static IServiceCollection AddHexagon(this IServiceCollection services, IConfiguration configuration) =>
        services
            .AddInboundPorts()
            .AddOutboundPorts(configuration)
            .AddRestAdapter();

    private static IServiceCollection AddInboundPorts(this IServiceCollection services)
    {
        services.AddScoped<IPlaceSale, PlaceSaleService>();
        services.AddScoped<IGetSale, GetSaleService>();
        services.AddScoped<IGetSalesReport, SalesReportService>();
        services.AddScoped<IManageProducts, ProductCatalogService>();
        services.AddScoped<IAuthenticate, AuthenticationService>();
        services.AddScoped<IProvisionAdministrator, AuthenticationService>();

        return services;
    }

    private static IServiceCollection AddOutboundPorts(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistenceAdapter(
            configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured."));

        services.AddLocalStorageAdapter(options => configuration.GetSection(LocalStorageOptions.SectionName).Bind(options));

        services.AddSecurityAdapter(options => configuration.GetSection(JwtOptions.SectionName).Bind(options));

        services.AddSingleton<IClock, SystemClock>();

        // Stateless and cheap: the policy holds a backoff function, not a conversation.
        services.AddSingleton<ConflictRetryPolicy>();

        return services;
    }
}
