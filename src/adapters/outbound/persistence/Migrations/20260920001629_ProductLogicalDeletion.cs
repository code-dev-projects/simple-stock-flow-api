using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleStockFlow.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductLogicalDeletion : Migration
    {
        // The scaffolded output was reviewed before being accepted and is used unchanged, which
        // the two earlier hand-corrected migrations make worth stating. Nothing had to be
        // removed here because the column is nullable: EF neither fabricates a backfill nor
        // emits a SET DEFAULT, and no column in this schema carries a default on purpose -- the
        // values come from the domain. Dropping the column is a lossless Down while product is
        // empty, and it stops being lossless the day the first takedown is recorded.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                schema: "sales",
                table: "product",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "sales",
                table: "product");
        }
    }
}
