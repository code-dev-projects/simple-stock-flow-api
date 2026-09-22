using NSubstitute;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Application.Services;
using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.ValueObjects;

namespace SimpleStockFlow.Application.UnitTests;

/// <summary>
/// Faked outbound ports, zero infrastructure. What a double cannot answer -- case-insensitive
/// matching, the row order and the collation behind it -- is asserted against a real engine in
/// Adapters.IntegrationTests instead of being faked into agreement here.
/// </summary>
public sealed class ProductCatalogServiceTests
{
    private static readonly Category Tools = new(Guid.Parse("22222222-2222-4222-8222-222222222222"), "Herramientas");
    private static readonly Category Paints = new(Guid.Parse("55555555-5555-4555-8555-555555555555"), "Pinturas");
    private static readonly Guid UnknownCategory = Guid.Parse("99999999-9999-4999-8999-999999999999");

    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly ProductCatalogService _sut;

    public ProductCatalogServiceTests()
    {
        GivenCategories(Tools, Paints);

        // NSubstitute answers a string-returning member with "" rather than null, which is
        // precisely the empty address CA-03.2 forbids. The double is taught the port's own
        // contract, which LocalFileStorageTests pins against the real adapter.
        _storage.ResolveUrl(null).Returns((string?)null);

        _sut = new ProductCatalogService(_products, _categories, _storage, _unitOfWork);
    }

    private void GivenCategories(params Category[] categories)
    {
        _categories
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>(categories));

        foreach (var category in categories)
            _categories
                .FindAsync(category.Id, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<Category?>(category));
    }

    private Product GivenStoredProduct(
        string name = "Martillo",
        decimal price = 25_000m,
        int stock = 4,
        string? imageKey = null)
    {
        var product = Product.Create(name, new Money(price), stock, Tools.Id);
        product.AttachImage(imageKey);
        _products.FindAsync(product.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Product?>(product));
        return product;
    }

    private void GivenSearchResult(PagedResult<Product> result) =>
        _products
            .SearchAsync(Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));

    private static ImageUpload AnUpload(string contentType = "image/jpeg") =>
        new(new MemoryStream([1, 2, 3]), "martillo.jpg", contentType, 3);

    [Fact]
    public async Task Creates_the_product_and_commits_exactly_once()
    {
        var id = await _sut.CreateAsync(new CreateProductCommand("  Martillo  ", 25_000m, 4, Tools.Id));

        id.Should().NotBeEmpty();
        await _products.Received(1).AddAsync(
            Arg.Is<Product>(product => product.Id == id && product.Name == "Martillo" && product.Stock == 4),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_a_price_of_zero_without_committing()
    {
        var act = async () => await _sut.CreateAsync(new CreateProductCommand("Martillo", 0m, 4, Tools.Id));

        await act.Should().ThrowAsync<DomainException>().WithMessage("El precio debe ser mayor a cero.");
        await _products.DidNotReceive().AddAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_a_negative_initial_stock_without_committing()
    {
        var act = async () => await _sut.CreateAsync(new CreateProductCommand("Martillo", 25_000m, -1, Tools.Id));

        await act.Should().ThrowAsync<DomainException>().WithMessage("El stock inicial no puede ser negativo.");
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// CA-02.4. Whether the referenced row exists is the one question the aggregate cannot
    /// answer -- it holds no repository -- so the use case asks it, exactly as PlaceSaleService
    /// asks whether a product exists. The message is prescribed by E-05.
    /// </summary>
    [Fact]
    public async Task Rejects_a_category_that_does_not_exist_without_committing()
    {
        var act = async () => await _sut.CreateAsync(new CreateProductCommand("Martillo", 25_000m, 4, UnknownCategory));

        await act.Should().ThrowAsync<DomainException>().WithMessage($"La categoría {UnknownCategory} no existe.");
        await _products.DidNotReceive().AddAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// E-05 fixes the order so the message is deterministic when more than one field is wrong.
    /// A blank name wins over everything that follows it in the constructor.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_name_is_reported_before_anything_else(string name)
    {
        var act = async () => await _sut.CreateAsync(new CreateProductCommand(name, 0m, -1, UnknownCategory));

        await act.Should().ThrowAsync<DomainException>().WithMessage("El nombre del producto es obligatorio.");
    }

    [Fact]
    public async Task The_price_is_reported_before_the_category_and_the_stock()
    {
        var act = async () => await _sut.CreateAsync(new CreateProductCommand("Martillo", 0m, -1, UnknownCategory));

        await act.Should().ThrowAsync<DomainException>().WithMessage("El precio debe ser mayor a cero.");
    }

    /// <summary>
    /// E-05 lists "La categoría es obligatoria." and "La categoría &lt;id&gt; no existe." as two
    /// distinct 422s. An empty identifier is the missing-value case, and it has to stay
    /// reachable: asking the repository for it first would answer "no existe" instead and the
    /// first of the two rows could never be produced.
    /// </summary>
    [Fact]
    public async Task An_empty_category_identifier_is_reported_as_missing_and_not_as_unknown()
    {
        var act = async () => await _sut.CreateAsync(new CreateProductCommand("Martillo", 25_000m, 4, Guid.Empty));

        await act.Should().ThrowAsync<DomainException>().WithMessage("La categoría es obligatoria.");
    }

    [Fact]
    public async Task The_stock_is_reported_last()
    {
        var act = async () => await _sut.CreateAsync(new CreateProductCommand("Martillo", 25_000m, -1, Tools.Id));

        await act.Should().ThrowAsync<DomainException>().WithMessage("El stock inicial no puede ser negativo.");
    }

    [Fact]
    public async Task Serves_the_page_the_repository_returns_with_the_live_category_name()
    {
        var hammer = Product.Create("Martillo", new Money(25_000m), 4, Tools.Id);
        GivenSearchResult(new PagedResult<Product>([hammer], Page: 2, Size: 20, Total: 25));

        var page = await _sut.ListAsync(new ProductFilter(), new PageRequest(2, 20));

        page.Page.Should().Be(2);
        page.Size.Should().Be(20);
        page.Total.Should().Be(25);
        page.TotalPages.Should().Be(2);
        page.Items.Should().ContainSingle().Which.Should().Be(
            new ProductView(hammer.Id, "Martillo", 25_000m, "COP", 4, Tools.Id, "Herramientas", null));
    }

    /// <summary>
    /// CA-01.2 and CA-01.3. The predicate belongs to the engine: what the use case owes is
    /// handing both filters over untouched instead of sifting the page in memory.
    /// </summary>
    [Fact]
    public async Task Hands_the_search_and_the_category_filter_to_the_repository_untouched()
    {
        GivenSearchResult(PagedResult<Product>.Empty(new PageRequest(1, 20)));

        await _sut.ListAsync(new ProductFilter("mar", Tools.Id), new PageRequest(1, 20));

        await _products.Received(1).SearchAsync(
            "mar",
            Tools.Id,
            Arg.Is<PageRequest>(page => page.Page == 1 && page.Size == 20),
            Arg.Any<CancellationToken>());
    }

    /// <summary>D-C5 reaches the repository too: it must read the window actually served.</summary>
    [Fact]
    public async Task A_size_over_the_maximum_reaches_the_repository_already_trimmed()
    {
        GivenSearchResult(PagedResult<Product>.Empty(new PageRequest(1, PageRequest.MaxSize)));

        var page = await _sut.ListAsync(new ProductFilter(), new PageRequest(1, 500));

        page.Size.Should().Be(PageRequest.MaxSize);
        await _products.Received(1).SearchAsync(
            Arg.Any<string?>(),
            Arg.Any<Guid?>(),
            Arg.Is<PageRequest>(request => request.Size == PageRequest.MaxSize),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_page_carries_an_empty_list_and_no_pages()
    {
        GivenSearchResult(PagedResult<Product>.Empty(new PageRequest(1, 20)));

        var page = await _sut.ListAsync(new ProductFilter(), new PageRequest(1, 20));

        page.Items.Should().NotBeNull().And.BeEmpty();
        page.Total.Should().Be(0);
        page.TotalPages.Should().Be(0);
    }

    [Fact]
    public async Task Getting_a_product_that_does_not_exist_returns_nothing()
    {
        var view = await _sut.GetAsync(Guid.NewGuid());

        view.Should().BeNull();
    }

    /// <summary>CA-03.1: the use case never builds the address, the storage port resolves it.</summary>
    [Fact]
    public async Task A_product_with_an_image_carries_the_url_the_storage_resolves()
    {
        var product = GivenStoredProduct(imageKey: "9f2ca1.jpg");
        _storage.ResolveUrl("9f2ca1.jpg").Returns("/media/9f2ca1.jpg");

        var view = await _sut.GetAsync(product.Id);

        view!.ImageUrl.Should().Be("/media/9f2ca1.jpg");
    }

    /// <summary>CA-03.2: no image is null, never an empty string and never a broken address.</summary>
    [Fact]
    public async Task A_product_without_an_image_carries_no_url_at_all()
    {
        var product = GivenStoredProduct();

        var view = await _sut.GetAsync(product.Id);

        view!.ImageUrl.Should().BeNull();
    }

    /// <summary>
    /// D-C10. The portal upper-cases the currency without a guard, so a null would not produce a
    /// bad label: it would take the screen down.
    /// </summary>
    [Fact]
    public async Task The_view_always_carries_the_single_currency_of_the_system()
    {
        var product = GivenStoredProduct();

        var view = await _sut.GetAsync(product.Id);

        view!.Currency.Should().Be(Money.DefaultCurrency);
    }

    [Fact]
    public async Task Updates_every_attribute_and_commits_exactly_once()
    {
        var product = GivenStoredProduct("Martillo", 25_000m, 4);

        var updated = await _sut.UpdateAsync(
            new UpdateProductCommand(product.Id, "Martillo de goma", 31_500m, 9, Paints.Id));

        updated.Should().BeTrue();
        product.Name.Should().Be("Martillo de goma");
        product.Price.Amount.Should().Be(31_500m);
        product.Stock.Should().Be(9);
        product.CategoryId.Should().Be(Paints.Id);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>E-06 is a replacement, not a patch: the stock is stated, not moved.</summary>
    [Fact]
    public async Task Updating_lowers_the_stock_without_blaming_it_for_being_insufficient()
    {
        var product = GivenStoredProduct("Martillo", 25_000m, 4);

        await _sut.UpdateAsync(new UpdateProductCommand(product.Id, "Martillo", 25_000m, 1, Tools.Id));

        product.Stock.Should().Be(1);
    }

    [Fact]
    public async Task Updating_a_product_that_does_not_exist_reports_it_and_commits_nothing()
    {
        var updated = await _sut.UpdateAsync(
            new UpdateProductCommand(Guid.NewGuid(), "Martillo", 25_000m, 4, Tools.Id));

        updated.Should().BeFalse();
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Updating_onto_a_category_that_does_not_exist_is_rejected_without_committing()
    {
        var product = GivenStoredProduct();

        var act = async () => await _sut.UpdateAsync(
            new UpdateProductCommand(product.Id, "Martillo", 25_000m, 4, UnknownCategory));

        await act.Should().ThrowAsync<DomainException>().WithMessage($"La categoría {UnknownCategory} no existe.");
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Updating_with_a_negative_stock_is_rejected_without_committing()
    {
        var product = GivenStoredProduct();

        var act = async () => await _sut.UpdateAsync(
            new UpdateProductCommand(product.Id, "Martillo", 25_000m, -1, Tools.Id));

        await act.Should().ThrowAsync<DomainException>().WithMessage("El stock inicial no puede ser negativo.");
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// CA-03.3 and D-08. The storage takes no part in the database transaction, so the two
    /// cannot be made atomic. Of the two possible orders only one is safe: an orphaned binary
    /// is harmless, a key pointing at a binary that is gone is a permanently broken image.
    /// </summary>
    [Fact]
    public async Task Taking_a_product_down_commits_the_cleared_key_before_the_binary_is_deleted()
    {
        var product = GivenStoredProduct(imageKey: "9f2ca1.jpg");

        var removed = await _sut.DeleteAsync(product.Id);

        removed.Should().BeTrue();
        product.ImageKey.Should().BeNull();
        Received.InOrder(() =>
        {
            _unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
            _storage.DeleteAsync("9f2ca1.jpg", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Taking_down_a_product_without_an_image_never_asks_the_storage_for_anything()
    {
        var product = GivenStoredProduct();

        await _sut.DeleteAsync(product.Id);

        await _storage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Taking_down_a_product_that_does_not_exist_reports_it_and_commits_nothing()
    {
        var removed = await _sut.DeleteAsync(Guid.NewGuid());

        removed.Should().BeFalse();
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _storage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Attaching_an_image_stores_the_binary_commits_and_returns_its_url()
    {
        var product = GivenStoredProduct();
        _storage.SaveAsync(Arg.Any<ImageUpload>(), Arg.Any<CancellationToken>()).Returns("9f2ca1.jpg");
        _storage.ResolveUrl("9f2ca1.jpg").Returns("/media/9f2ca1.jpg");

        var url = await _sut.AttachImageAsync(product.Id, AnUpload());

        url.Should().Be("/media/9f2ca1.jpg");
        product.ImageKey.Should().Be("9f2ca1.jpg");
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The product is looked up before the binary is written: saving first would leave a file
    /// on disk that no row will ever reference, and nothing deletes it.
    /// </summary>
    [Fact]
    public async Task Attaching_an_image_to_a_product_that_does_not_exist_never_stores_the_binary()
    {
        var url = await _sut.AttachImageAsync(Guid.NewGuid(), AnUpload());

        url.Should().BeNull();
        await _storage.DidNotReceive().SaveAsync(Arg.Any<ImageUpload>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The binary of an image is the only datum in the system that is deleted for good, and
    /// replacing one deletes the previous file. Same order as the takedown, for the same reason.
    /// </summary>
    [Fact]
    public async Task Replacing_an_image_deletes_the_previous_binary_after_the_commit()
    {
        var product = GivenStoredProduct(imageKey: "vieja.jpg");
        _storage.SaveAsync(Arg.Any<ImageUpload>(), Arg.Any<CancellationToken>()).Returns("nueva.jpg");

        await _sut.AttachImageAsync(product.Id, AnUpload());

        product.ImageKey.Should().Be("nueva.jpg");
        Received.InOrder(() =>
        {
            _unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
            _storage.DeleteAsync("vieja.jpg", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Attaching_the_first_image_deletes_nothing()
    {
        var product = GivenStoredProduct();
        _storage.SaveAsync(Arg.Any<ImageUpload>(), Arg.Any<CancellationToken>()).Returns("nueva.jpg");

        await _sut.AttachImageAsync(product.Id, AnUpload());

        await _storage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>D-C1: a flat list, in the order the repository serves, with no envelope.</summary>
    [Fact]
    public async Task Lists_the_reference_categories_in_the_order_the_repository_serves_them()
    {
        var categories = await _sut.ListCategoriesAsync();

        categories.Should().Equal(
            new CategoryView(Tools.Id, "Herramientas"),
            new CategoryView(Paints.Id, "Pinturas"));
    }

    /// <summary>
    /// D-C1 forbids 204 for an empty table: the portal maps over the body, and no body breaks
    /// it. An empty list has to survive the use case as an empty list.
    /// </summary>
    [Fact]
    public async Task An_empty_category_table_is_an_empty_list_and_not_a_failure()
    {
        GivenCategories();

        var categories = await _sut.ListCategoriesAsync();

        categories.Should().NotBeNull().And.BeEmpty();
    }
}
