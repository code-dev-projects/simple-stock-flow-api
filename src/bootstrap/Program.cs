using SimpleStockFlow.Bootstrap.Composition;
using Serilog;

const string PortalCors = "portal";

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter()));

builder.Services.AddHexagon(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPortalCors(builder.Configuration, PortalCors);
builder.Services.AddSwaggerWithBearerAuth();

var app = builder.Build();

await app.ApplyMigrationsAsync();
await app.Services.EnsureAdministratorAsync();

app.UseSerilogRequestLogging();

app.UseApiDocumentation();

app.UseMediaFiles();

app.UseCors(PortalCors);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new HealthStatus("ok")))
    .AllowAnonymous()
    .WithName("Health")
    // Without a tag the generated page files this under the host assembly's name, and
    // "SimpleStockFlow.Bootstrap" is a build artefact, not a thing the API offers.
    .WithTags("Health")
    .WithSummary("Liveness of the process (E-14).")
    .WithDescription(
        "Answers 200 or does not answer at all. It does not reach the database: it says the "
        + "process is up, not that its dependencies are.")
    .Produces<HealthStatus>(StatusCodes.Status200OK);

await app.RunAsync();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;

/// <summary>The body of /health, typed so the generated documentation can show its one field.</summary>
/// <param name="Status">Its only value is "ok".</param>
internal sealed record HealthStatus(string Status);
