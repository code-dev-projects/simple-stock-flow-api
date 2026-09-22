using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimpleStockFlow.Adapters.Persistence.Repositories;
using SimpleStockFlow.Application.Ports.Outbound;

namespace SimpleStockFlow.Adapters.Persistence;

/// <summary>
/// The adapter's public surface: bootstrap calls only here and knows neither SalesDbContext
/// nor the concrete repository implementations.
/// </summary>
public static class PersistenceAdapter
{
    public static IServiceCollection AddPersistenceAdapter(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SalesDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IProductRepository, EfProductRepository>();
        services.AddScoped<ISaleRepository, EfSaleRepository>();
        services.AddScoped<ICategoryRepository, EfCategoryRepository>();
        services.AddScoped<IUserRepository, EfUserRepository>();

        // Read-only and not a repository: it serves rows the engine aggregated, never entities.
        services.AddScoped<ISalesReportQuery, SqlSalesReportQuery>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        return services;
    }
}
