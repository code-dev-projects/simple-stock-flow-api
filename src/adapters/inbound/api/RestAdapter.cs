using Microsoft.Extensions.DependencyInjection;
using SimpleStockFlow.Adapters.Rest.Filters;

namespace SimpleStockFlow.Adapters.Rest;

/// <summary>Registration of the driving REST adapter.</summary>
public static class RestAdapter
{
    /// <summary>
    /// The controllers live in this assembly, not in the host: it has to be registered
    /// explicitly as an ApplicationPart or MVC never discovers them.
    /// </summary>
    public static IServiceCollection AddRestAdapter(this IServiceCollection services)
    {
        services
            .AddControllers(options => options.Filters.Add<ExceptionTranslationFilter>())
            .AddApplicationPart(typeof(RestAdapter).Assembly)
            // The single point where the 400's body is decided. Left to the framework it carried
            // no detail (D-C9), spoke English to a Spanish reader, and named the server's own
            // types back to the caller -- see ValidationProblemFactory.
            .ConfigureApiBehaviorOptions(options =>
                options.InvalidModelStateResponseFactory = ValidationProblemFactory.Build);

        return services;
    }
}
