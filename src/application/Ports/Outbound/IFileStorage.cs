using SimpleStockFlow.Application.Ports.Inbound;

namespace SimpleStockFlow.Application.Ports.Outbound;

/// <summary>
/// The local implementation is what satisfies the "Blob Storage mock" requirement; moving to
/// S3 or Azure Blob is a new class and one binding line, with no change to domain or
/// application.
/// </summary>
public interface IFileStorage
{
    Task<string> SaveAsync(ImageUpload upload, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    string? ResolveUrl(string? key);
}
