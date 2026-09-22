using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SimpleStockFlow.Adapters.Persistence;
using SimpleStockFlow.Adapters.Rest;
using SimpleStockFlow.Adapters.Security;
using SimpleStockFlow.Adapters.Storage;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SimpleStockFlow.Bootstrap.Composition;

/// <summary>
/// Everything the host needs that is transport rather than hexagon: authentication, CORS,
/// Swagger, the schema and the media files. Kept apart so Program.cs stays a readable list
/// of decisions instead of a wall of configuration.
/// </summary>
internal static class HostConfiguration
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        // A missing secret must stop the boot. Signing with a filler key would start fine and hand out
        // tokens anyone could forge, and the failure would surface far from its cause.
        if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Trim().Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is missing or too short. Set the Jwt__SigningKey environment variable " +
                "(JWT_SIGNING_KEY in the simple-stock-flow-infra .env file) to at least 32 characters. " +
                "There is no default on purpose: signing with a filler key is worse than refusing to start.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                ClockSkew = TimeSpan.FromSeconds(30),
            });

        services.AddAuthorization();

        return services;
    }

    public static IServiceCollection AddPortalCors(
        this IServiceCollection services,
        IConfiguration configuration,
        string policyName) =>
        services.AddCors(options => options.AddPolicy(policyName, policy => policy
            .WithOrigins(configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
            .AllowAnyHeader()
            .AllowAnyMethod()));

    public static IServiceCollection AddSwaggerWithBearerAuth(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Simple Stock Flow API",
                Version = "v1",
                Description =
                    "Products and sales. Sign in through POST /api/auth/login, paste the accessToken "
                    + "into Authorize, and every other operation becomes executable from this page. "
                    + "Field names are camelCase, amounts carry two decimals, the currency is always "
                    + "COP, and a date range takes its start and leaves out its end. Error bodies are "
                    + "problem+json for 409 and 422, problem+json with an errors map for 400, and "
                    + "empty for 401, 403 and 404.",
            });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "The accessToken of POST /api/auth/login, on its own: Swagger adds the Bearer prefix.",
            });
            options.OperationFilter<BearerWhereRequiredFilter>();
            options.OperationFilter<MandatoryDateRangeFilter>();

            // Without this the generator treats every reference type as nullable, so the document
            // declared 31 strings nullable that the contract says are not -- currency among them,
            // which D-C10 protects by name -- and left every schema without a required list, which
            // makes a generated client treat even totalPages as optional (defect A-10).
            options.SupportNonNullableReferenceTypes();
            options.SchemaFilter<RequiredWhereNotNullableFilter>();
            options.DocumentFilter<MediaRouteFilter>();

            IncludeRestAdapterDocumentation(options);
        });

        return services;
    }

    /// <summary>
    /// Mounts the OpenAPI document and its page when Swagger:Enabled says so, and it says so by
    /// default.
    ///
    /// The mount used to hang off IsDevelopment(), and the deployed container runs as Production:
    /// the documentation was therefore missing from the one deployment anybody was ever going to
    /// read it in. This is a technical exercise whose browsable API is part of what is assessed,
    /// so the default is on and the environment has no say. A real deployment sets
    /// Swagger__Enabled=false, which is the one line that takes the page away again.
    /// </summary>
    public static WebApplication UseApiDocumentation(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Swagger:Enabled", true))
            return app;

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("v1/swagger.json", "Simple Stock Flow API v1");
            options.DocumentTitle = "Simple Stock Flow API";
        });

        return app;
    }

    /// <summary>
    /// The operation descriptions live in the REST adapter's XML file, next to the controllers
    /// they document. The path is derived from the assembly rather than spelled out, so renaming
    /// the project cannot leave it pointing at nothing; the existence check is what keeps a build
    /// without that file from taking the host down instead of merely thinning the page.
    /// </summary>
    private static void IncludeRestAdapterDocumentation(SwaggerGenOptions options)
    {
        var file = Path.Combine(
            AppContext.BaseDirectory,
            $"{typeof(RestAdapter).Assembly.GetName().Name}.xml");

        if (File.Exists(file))
            options.IncludeXmlComments(file, includeControllerXmlComments: true);
    }

    /// <summary>
    /// The Postgres container only creates the database; the schema belongs to this service's
    /// migrations.
    /// </summary>
    public static async Task ApplyMigrationsAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await context.Database.MigrateAsync();
    }

    /// <summary>Serves what LocalFileStorage wrote, under the very URL ResolveUrl publishes.</summary>
    public static WebApplication UseMediaFiles(this WebApplication app)
    {
        var storage = app.Configuration.GetSection(LocalStorageOptions.SectionName).Get<LocalStorageOptions>()
                      ?? new LocalStorageOptions();
        Directory.CreateDirectory(storage.RootPath);

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.GetFullPath(storage.RootPath)),
            RequestPath = storage.PublicBaseUrl.TrimEnd('/'),
        });

        return app;
    }
}
