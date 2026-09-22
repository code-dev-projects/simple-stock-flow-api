using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleStockFlow.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogueAccessIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Written as SQL and not through the model builder because EF cannot express a GIN
            // index with an operator class, and splitting the pair across two mechanisms -- one
            // tracked by the snapshot, one not -- is worse than keeping both in one place.

            // The search of CA-01.2 is "contains, case-insensitively". A b-tree has no prefix to
            // walk for that and degrades to reading every row; trigrams are what make it an index
            // scan. The extension has to exist before the index that uses it.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // Both partial over the active rows: every catalogue read carries the global filter on
            // deleted_at (ADR-003), so an index that also held the withdrawn products would be
            // larger for no reader and the planner would still re-check the predicate.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_product_category_id_name_active"
                ON sales.product (category_id, name)
                WHERE deleted_at IS NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_product_name_trigram_active"
                ON sales.product USING gin (name gin_trgm_ops)
                WHERE deleted_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS sales.\"IX_product_name_trigram_active\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS sales.\"IX_product_category_id_name_active\";");
            // The extension is left installed on purpose: dropping it would break anything else
            // that came to depend on it, and an unused extension costs nothing.
        }
    }
}
