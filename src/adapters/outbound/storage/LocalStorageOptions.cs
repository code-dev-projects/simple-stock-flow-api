namespace SimpleStockFlow.Adapters.Storage;

public sealed class LocalStorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Physical folder. Under Docker it points at a volume, not at the image.</summary>
    public string RootPath { get; set; } = "/var/lib/simple-stock-flow/media";

    public string PublicBaseUrl { get; set; } = "/media";

    public long MaxBytes { get; set; } = 5 * 1024 * 1024;

    public string[] AllowedContentTypes { get; set; } = ["image/jpeg", "image/png", "image/webp"];
}
