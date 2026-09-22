using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleStockFlow.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockNonNegative : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The model gained the xmin concurrency token in this same change, and the scaffolder
            // proposed creating a column for it. There is nothing to create: xmin is a Postgres
            // system column that every table already has, and the engine rejects an ALTER TABLE
            // that tries to add it. The token costs zero schema surface, which is the point of
            // choosing it (ADR-002); the only real DDL here is the constraint below.
            migrationBuilder.AddCheckConstraint(
                name: "ck_products_stock_non_negative",
                schema: "sales",
                table: "products",
                sql: "stock >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_products_stock_non_negative",
                schema: "sales",
                table: "products");
        }
    }
}
