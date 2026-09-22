using Microsoft.Extensions.DependencyInjection;
using SimpleStockFlow.Adapters.Storage;
using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Common;

namespace SimpleStockFlow.Adapters.IntegrationTests;

/// <summary>
/// Defect A-2: Storage:MaxBytes was declared and read by nobody, and a rejected content type
/// left as an InvalidOperationException that the translation filter does not recognise, so it
/// reached the caller as a 500. E-08 prescribes 422 for both, which means both have to arrive
/// as broken rules. The limit is checked against the declared length so nothing is written
/// first: a half-written file that then has to be deleted is a second failure waiting to happen.
/// </summary>
public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"simple-stock-flow-storage-{Guid.NewGuid():N}");

    private IFileStorage NewStorage(long maxBytes = 5 * 1024 * 1024) =>
        new ServiceCollection()
            .AddLocalStorageAdapter(options =>
            {
                options.RootPath = _root;
                options.MaxBytes = maxBytes;
                options.PublicBaseUrl = "/media";
                options.AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
            })
            .BuildServiceProvider()
            .GetRequiredService<IFileStorage>();

    private static ImageUpload AnUpload(string contentType = "image/jpeg", int bytes = 3) =>
        new(new MemoryStream(new byte[bytes]), "la foto del cliente.jpg", contentType, bytes);

    [Fact]
    public async Task A_content_type_outside_the_allow_list_is_refused_as_a_broken_rule()
    {
        var act = async () => await NewStorage().SaveAsync(AnUpload("application/pdf"));

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("Tipo de archivo no permitido: application/pdf.");
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public async Task The_three_accepted_types_go_through(string contentType)
    {
        var key = await NewStorage().SaveAsync(AnUpload(contentType));

        key.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>The case of the header is the client's business, not a reason to refuse.</summary>
    [Fact]
    public async Task The_allow_list_does_not_mind_the_case_of_the_header()
    {
        var key = await NewStorage().SaveAsync(AnUpload("IMAGE/JPEG"));

        key.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task An_upload_over_the_limit_is_refused_as_a_broken_rule_naming_the_limit()
    {
        var act = async () => await NewStorage(maxBytes: 5 * 1024 * 1024)
            .SaveAsync(AnUpload(bytes: 5 * 1024 * 1024 + 1));

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("La imagen supera el máximo de 5 MB.");
    }

    [Fact]
    public async Task An_upload_over_the_limit_leaves_nothing_behind_on_disk()
    {
        var act = async () => await NewStorage(maxBytes: 8).SaveAsync(AnUpload(bytes: 9));

        await act.Should().ThrowAsync<DomainException>();
        (Directory.Exists(_root) ? Directory.GetFiles(_root) : []).Should().BeEmpty();
    }

    [Fact]
    public async Task An_upload_exactly_on_the_limit_is_accepted()
    {
        var key = await NewStorage(maxBytes: 8).SaveAsync(AnUpload(bytes: 8));

        key.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The name the client sends is never reused: it would allow traversal and collisions, and
    /// here it also carries spaces that have no business in a URL.
    /// </summary>
    [Fact]
    public async Task The_stored_key_keeps_the_extension_and_drops_the_name_the_client_sent()
    {
        var storage = NewStorage();

        var key = await storage.SaveAsync(AnUpload());

        key.Should().EndWith(".jpg").And.NotContain("la foto");
        File.Exists(Path.Combine(_root, key)).Should().BeTrue();
        storage.ResolveUrl(key).Should().Be($"/media/{key}");
    }

    [Fact]
    public void No_key_resolves_to_no_address_at_all()
    {
        NewStorage().ResolveUrl(null).Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
