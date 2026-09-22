using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleStockFlow.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccentSeedCategoryNames : Migration
    {
        // Spelling-only migration: the seeded category name was written without its accent.
        // InitialSchema is already applied, so it is left untouched and the row is corrected
        // here instead. The UPDATE is the whole migration on purpose: nothing else about the
        // schema changed, and the unique index on sales.categories(name) is safe because the
        // row is renamed in place and no other row already holds the accented name.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "sales",
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("44444444-4444-4444-8444-444444444444"),
                column: "name",
                value: "Fontanería");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "sales",
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("44444444-4444-4444-8444-444444444444"),
                column: "name",
                value: "Fontaneria");
        }
    }
}
