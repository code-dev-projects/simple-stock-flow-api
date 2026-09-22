using Microsoft.Extensions.Options;
using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Common;

namespace SimpleStockFlow.Adapters.Storage;

/// <summary>
/// Binary outbound adapter over disk. It satisfies the "Blob Storage mock" requirement:
/// replacing it with S3 or Azure Blob is another class implementing IFileStorage.
/// </summary>
internal sealed class LocalFileStorage : IFileStorage
{
    private const long BytesPerMegabyte = 1024 * 1024;

    private readonly LocalStorageOptions _options;

    public LocalFileStorage(IOptions<LocalStorageOptions> options) => _options = options.Value;

    public async Task<string> SaveAsync(ImageUpload upload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        // A refused upload is a broken rule, not a server fault. As an InvalidOperationException
        // the translation filter did not recognise it and the caller got a 500 for sending the
        // wrong kind of file.
        if (!_options.AllowedContentTypes.Contains(upload.ContentType, StringComparer.OrdinalIgnoreCase))
            throw new DomainException($"Tipo de archivo no permitido: {upload.ContentType}.");

        // Judged on the declared length, before anything is written: measuring the stream would
        // mean either buffering the whole upload or deleting a half-written file afterwards.
        if (upload.Length > _options.MaxBytes)
            throw new DomainException($"La imagen supera el máximo de {_options.MaxBytes / BytesPerMegabyte} MB.");

        Directory.CreateDirectory(_options.RootPath);

        // The name the client sends is never reused: it would allow traversal and collisions.
        var extension = Path.GetExtension(upload.FileName);
        var key = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(_options.RootPath, key);

        await using var target = File.Create(fullPath);
        await upload.Content.CopyToAsync(target, ct);

        return key;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_options.RootPath, Path.GetFileName(key));
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    public string? ResolveUrl(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : $"{_options.PublicBaseUrl.TrimEnd('/')}/{key}";
}
