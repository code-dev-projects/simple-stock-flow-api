using Microsoft.EntityFrameworkCore;
using Npgsql;
using SimpleStockFlow.Adapters.Persistence;

namespace SimpleStockFlow.Adapters.IntegrationTests;

/// <summary>
/// T-20. Every rule asserted here already holds inside the domain, and that is exactly why it
/// needs a test at this level: an invariant that only lives in C# protects the application, not
/// the data. Any psql session, migration script or future service writes straight past it. So
/// each test bypasses the aggregates on purpose and writes raw SQL through ExecuteSqlRawAsync:
/// a statement that goes through the domain would prove the domain works, which was never in
/// doubt, instead of proving the engine refuses.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EngineInvariantsTests
{
    private readonly PostgresFixture _postgres;

    public EngineInvariantsTests(PostgresFixture postgres) => _postgres = postgres;

    private SalesDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SalesDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options);

    private async Task<SalesDbContext> SchemaReadyContextAsync()
    {
        var context = NewContext();
        await context.Database.MigrateAsync();
        return context;
    }

    [Fact]
    public async Task A_sale_item_without_a_sale_is_refused()
    {
        await using var context = await SchemaReadyContextAsync();
        var productId = await AProductAsync(context);

        // Written as a literal NULL rather than a parameter: the point is the column, and a
        // typed null parameter would make the statement depend on how Npgsql infers its type.
        var orphan = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sales.sale_item (id, sale_id, product_id, product_name, category_name, quantity, unit_price)
            VALUES ({0}, NULL, {1}, 'Taladro', 'Herramientas', 1, 100.00)
            """,
            Guid.NewGuid(), productId);

        var refusal = await orphan.Should().ThrowAsync<PostgresException>();
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.NotNullViolation);
        refusal.Which.ColumnName.Should().Be("sale_id");
    }

    [Fact]
    public async Task A_sale_item_pointing_at_a_product_that_does_not_exist_is_refused()
    {
        await using var context = await SchemaReadyContextAsync();
        var saleId = await ASaleAsync(context);

        var phantom = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sales.sale_item (id, sale_id, product_id, product_name, category_name, quantity, unit_price)
            VALUES ({0}, {1}, {2}, 'Producto fantasma', 'Herramientas', 1, 100.00)
            """,
            Guid.NewGuid(), saleId, Guid.NewGuid());

        var refusal = await phantom.Should().ThrowAsync<PostgresException>();
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        refusal.Which.ConstraintName.Should().Be("FK_sale_item_product_product_id");
    }

    /// <summary>
    /// ADR-003 leans on this one by name: logical deletion is safe because the foreign key
    /// stands as a last-resort barrier so that a manual delete fails loudly. The barrier was
    /// never built, so until now that argument rested on nothing.
    /// </summary>
    [Fact]
    public async Task Deleting_a_product_that_a_sale_already_records_is_refused()
    {
        await using var context = await SchemaReadyContextAsync();
        var productId = await AProductAsync(context);
        var saleId = await ASaleAsync(context);
        await ASaleItemAsync(context, saleId, productId);

        var hardDelete = () => context.Database.ExecuteSqlRawAsync(
            "DELETE FROM sales.product WHERE id = {0}", productId);

        var refusal = await hardDelete.Should().ThrowAsync<PostgresException>();
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        refusal.Which.ConstraintName.Should().Be("FK_sale_item_product_product_id");
    }

    [Fact]
    public async Task The_same_product_twice_in_one_sale_is_refused()
    {
        await using var context = await SchemaReadyContextAsync();
        var productId = await AProductAsync(context);
        var saleId = await ASaleAsync(context);
        await ASaleItemAsync(context, saleId, productId);

        var duplicate = () => ASaleItemAsync(context, saleId, productId);

        var refusal = await duplicate.Should().ThrowAsync<PostgresException>();
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        refusal.Which.ConstraintName.Should().Be("IX_sale_item_sale_id_product_id");
    }

    /// <summary>
    /// The included columns are not decoration: with quantity and unit_price in the index the
    /// report's aggregation never touches the table. And the single-column index the composite
    /// makes redundant has to be gone in the same migration, or both are paid for on every write.
    /// </summary>
    [Fact]
    public async Task The_composite_index_carries_the_report_columns_and_replaces_the_single_one()
    {
        await using var context = await SchemaReadyContextAsync();

        var composite = await IndexDefinitionAsync("IX_sale_item_sale_id_product_id");
        var redundant = await IndexDefinitionAsync("IX_sale_item_sale_id");

        composite.Should().NotBeNull();
        composite.Should().Contain("UNIQUE");
        composite.Should().Contain("(sale_id, product_id)");
        composite.Should().Contain("INCLUDE (quantity, unit_price)");
        redundant.Should().BeNull("the composite index leaves it redundant");
    }

    /// <summary>
    /// Money accepts zero, so the only guard in the whole system is Product.ChangePrice. A price
    /// written by any other route walks in unopposed, which is what this asserts is over.
    /// </summary>
    [Fact]
    public async Task A_product_priced_at_zero_is_refused()
    {
        await using var context = await SchemaReadyContextAsync();
        var categoryId = await ACategoryIdAsync(context);

        var free = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sales.product (id, name, price, stock, category_id, image_key)
            VALUES ({0}, 'Regalo', 0, 1, {1}, NULL)
            """,
            Guid.NewGuid(), categoryId);

        var refusal = await free.Should().ThrowAsync<PostgresException>();
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        refusal.Which.ConstraintName.Should().Be("ck_product_price_positive");
    }

    [Fact]
    public async Task A_sale_line_of_zero_units_is_refused()
    {
        await using var context = await SchemaReadyContextAsync();
        var productId = await AProductAsync(context);
        var saleId = await ASaleAsync(context);

        var nothingSold = () => ASaleItemAsync(context, saleId, productId, quantity: 0);

        var refusal = await nothingSold.Should().ThrowAsync<PostgresException>();
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        refusal.Which.ConstraintName.Should().Be("ck_sale_item_quantity_positive");
    }

    /// <summary>
    /// Blank, not empty: the domain trims before storing, so a name of spaces alone is the case
    /// that a NOT NULL plus an emptiness test would let through.
    /// </summary>
    [Fact]
    public async Task A_category_named_with_spaces_alone_is_refused()
    {
        await using var context = await SchemaReadyContextAsync();

        var blank = () => context.Database.ExecuteSqlRawAsync(
            "INSERT INTO sales.category (id, name) VALUES ({0}, '   ')", Guid.NewGuid());

        var refusal = await blank.Should().ThrowAsync<PostgresException>();
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        refusal.Which.ConstraintName.Should().Be("ck_category_name_not_blank");
    }

    [Fact]
    public async Task A_user_with_a_role_outside_the_closed_set_is_refused()
    {
        await using var context = await SchemaReadyContextAsync();

        var invented = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sales."user" (id, username, password_hash, role)
            VALUES ({0}, {1}, 'hash', 'jefe')
            """,
            Guid.NewGuid(), UniqueUsername());

        var refusal = await invented.Should().ThrowAsync<PostgresException>();
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        refusal.Which.ConstraintName.Should().Be("ck_user_role_allowed");
    }

    /// <summary>
    /// Padded and upper-cased at once. Checking only the lower case would still admit ' ana ',
    /// which the domain would have stored as 'ana': the constraint and the code would then
    /// disagree about what the same account is called.
    /// </summary>
    [Fact]
    public async Task A_username_that_is_not_trimmed_and_lower_cased_is_refused()
    {
        await using var context = await SchemaReadyContextAsync();

        var raw = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sales."user" (id, username, password_hash, role)
            VALUES ({0}, {1}, 'hash', 'seller')
            """,
            Guid.NewGuid(), $"  {UniqueUsername().ToUpperInvariant()}  ");

        var refusal = await raw.Should().ThrowAsync<PostgresException>();
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        refusal.Which.ConstraintName.Should().Be("ck_user_username_normalized");
    }

    /// <summary>
    /// The eight constraints arrive on a database that already holds the five reference
    /// categories and a bootstrapped administrator. If any of them rejected a row that is
    /// already there, the migration itself would not apply, so this asserts what the engine
    /// accepts rather than trusting that it did.
    /// </summary>
    [Fact]
    public async Task The_rows_that_already_exist_are_accepted_as_they_are()
    {
        await using var context = await SchemaReadyContextAsync();

        // By name and not by row count: while the blank-name constraint is still missing, a
        // sibling test succeeds in inserting the row it expected to be refused, and a count
        // would then fail for that reason instead of for the one under test.
        var seeded = await context.Categories.Select(category => category.Name).ToListAsync();

        var administrator = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sales."user" (id, username, password_hash, role)
            VALUES ({0}, {1}, 'hash', 'admin')
            """,
            Guid.NewGuid(), UniqueUsername());

        seeded.Should().Contain(["General", "Herramientas", "Electricidad", "Fontanería", "Pinturas"]);
        await administrator.Should().NotThrowAsync();
    }

    private static string UniqueUsername() => $"ana{Guid.NewGuid():N}";

    private static Task<Guid> ACategoryIdAsync(SalesDbContext context) =>
        context.Categories.Select(category => category.Id).FirstAsync();

    /// <summary>
    /// T-12. The author of a sale is an identifier with a foreign key behind it, not a name that
    /// happens to look like one. While sold_by was free text, a typo or a deleted user left the
    /// authorship of an accounting record pointing at nobody and nothing in the engine said so.
    /// RESTRICT and not CASCADE: deleting a user must never delete the sales they registered.
    /// </summary>
    [Fact]
    public async Task A_sale_whose_author_does_not_exist_is_refused()
    {
        await using var context = await SchemaReadyContextAsync();

        var phantom = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sales.sale (id, sold_at, sold_by_username, sold_by_user_id)
            VALUES ({0}, {1}, 'vendedora', {2})
            """,
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());

        var refusal = await phantom.Should().ThrowAsync<PostgresException>();
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        refusal.Which.ConstraintName.Should().Be("FK_sale_user_sold_by_user_id");
    }

    [Fact]
    public async Task Deleting_a_user_who_already_registered_a_sale_is_refused()
    {
        await using var context = await SchemaReadyContextAsync();

        var userId = Guid.NewGuid();
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sales."user" (id, username, password_hash, role)
            VALUES ({0}, {1}, 'hash', 'seller')
            """,
            userId, $"vendedora{Guid.NewGuid():N}");
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sales.sale (id, sold_at, sold_by_username, sold_by_user_id)
            VALUES ({0}, {1}, 'vendedora', {2})
            """,
            Guid.NewGuid(), DateTimeOffset.UtcNow, userId);

        var erase = () => context.Database.ExecuteSqlRawAsync(
            """DELETE FROM sales."user" WHERE id = {0}""", userId);

        var refusal = await erase.Should().ThrowAsync<PostgresException>();
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    /// <summary>
    /// T-13. Both indexes are PARTIAL over the active rows, because every catalogue read carries
    /// the global filter on deleted_at: an index that also holds the withdrawn products is bigger
    /// than it needs to be and the planner still has to re-check the predicate.
    /// </summary>
    [Fact]
    public async Task The_catalogue_is_indexed_by_category_and_name_over_the_active_rows_only()
    {
        // Migrates first: read on its own this test found a table with no indexes at all,
        // and passed only when another test happened to run before it and build the schema.
        await using var context = await SchemaReadyContextAsync();

        var definition = await IndexDefinitionAsync("IX_product_category_id_name_active");

        // The list is in the message on purpose: "it is not there" and "it is there under another
        // name" fail identically otherwise, and the second one sent me looking in the wrong place.
        definition.Should().NotBeNull(
            "plan.md §3.2 asks for it and CA-01.3 reads by category; product carries: {0}",
            string.Join(", ", await ProductIndexNamesAsync()));
        definition.Should().Contain("category_id").And.Contain("name");
        definition.Should().Contain("WHERE", "it is partial, over the products still on sale");
        definition.Should().Contain("deleted_at IS NULL");
    }

    /// <summary>
    /// The search of CA-01.2 is "contains, case-insensitively", which no b-tree can serve: it has
    /// no prefix to walk. Trigrams are what make it an index scan instead of reading every row.
    /// </summary>
    [Fact]
    public async Task The_search_by_name_is_backed_by_trigrams_and_not_by_a_prefix_index()
    {
        // Migrates first: read on its own this test found a table with no indexes at all,
        // and passed only when another test happened to run before it and build the schema.
        await using var context = await SchemaReadyContextAsync();

        var definition = await IndexDefinitionAsync("IX_product_name_trigram_active");

        definition.Should().NotBeNull("CA-01.2 searches by 'contains', which a b-tree cannot serve");
        definition.Should().Contain("gin", "trigram indexes are GIN");
        definition.Should().Contain("gin_trgm_ops");
        definition.Should().Contain("deleted_at IS NULL");
    }

    [Fact]
    public async Task The_extension_the_trigram_index_needs_is_installed()
    {
        await using var context = await SchemaReadyContextAsync();

        var installed = await context.Database
            .SqlQuery<int>($"SELECT count(*) AS \"Value\" FROM pg_extension WHERE extname = 'pg_trgm'")
            .SingleAsync();

        installed.Should().Be(1, "the index above cannot exist without it");
    }

    /// <summary>A real operator, because FK-4 refuses a sale whose author does not exist.</summary>
    private static async Task<Guid> AUserAsync(SalesDbContext context)
    {
        var id = Guid.NewGuid();
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sales."user" (id, username, password_hash, role)
            VALUES ({0}, {1}, 'hash', 'seller')
            """,
            id, $"vendedora{Guid.NewGuid():N}");

        return id;
    }

    private static async Task<Guid> AProductAsync(SalesDbContext context)
    {
        var id = Guid.NewGuid();

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sales.product (id, name, price, stock, category_id, image_key)
            VALUES ({0}, 'Taladro percutor', 320.00, 10, {1}, NULL)
            """,
            id, await ACategoryIdAsync(context));

        return id;
    }

    private static async Task<Guid> ASaleAsync(SalesDbContext context)
    {
        var id = Guid.NewGuid();

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sales.sale (id, sold_at, sold_by_username, sold_by_user_id)
            VALUES ({0}, {1}, 'vendedora', {2})
            """,
            id, DateTimeOffset.UtcNow, await AUserAsync(context));

        return id;
    }

    private static Task ASaleItemAsync(SalesDbContext context, Guid saleId, Guid productId, int quantity = 1) =>
        context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sales.sale_item (id, sale_id, product_id, product_name, category_name, quantity, unit_price)
            VALUES ({0}, {1}, {2}, 'Taladro percutor', 'Herramientas', {3}, 320.00)
            """,
            Guid.NewGuid(), saleId, productId, quantity);

    private async Task<IReadOnlyList<string>> ProductIndexNamesAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'sales' AND tablename = 'product'",
            connection);

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }

    private async Task<string?> IndexDefinitionAsync(string indexName)
    {
        await using var connection = new NpgsqlConnection(_postgres.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'sales' AND indexname = @name",
            connection);
        command.Parameters.AddWithValue("name", indexName);

        return await command.ExecuteScalarAsync() as string;
    }
}
