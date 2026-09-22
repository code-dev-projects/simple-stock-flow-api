namespace SimpleStockFlow.Adapters.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "simple-stock-flow";
    public string Audience { get; set; } = "simple-stock-flow-app";

    /// <summary>Never in a versioned appsettings: it arrives as an environment variable.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int LifetimeMinutes { get; set; } = 60;
}
