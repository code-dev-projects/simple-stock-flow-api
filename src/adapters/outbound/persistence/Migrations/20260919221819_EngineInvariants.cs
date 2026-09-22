using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleStockFlow.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EngineInvariants : Migration
    {
        // The scaffolded output was reviewed line by line before being accepted, and one argument
        // was removed from the AlterColumn below: a defaultValue of Guid.Empty. It produced two
        // statements that must not run. First an UPDATE filling every NULL sale_id with a sale
        // that does not exist, fabricating rows instead of refusing; sale_item is empty today, so
        // there is nothing to fill, and the moment there were, inventing the parent of an orphan
        // line is not a decision a migration may take on its own. Second, and permanent, an
        // ALTER COLUMN ... SET DEFAULT, when no column in this schema has a default on purpose:
        // the values come from the domain, and a default in the engine would be a second source
        // of truth that nothing tests. Dropping the argument removes both.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sale_item_sale_id",
                schema: "sales",
                table: "sale_item");

            // Before the composite unique index below, never after: while sale_id admits NULL the
            // index protects nothing, because in SQL no NULL ever collides with another.
            migrationBuilder.AlterColumn<Guid>(
                name: "sale_id",
                schema: "sales",
                table: "sale_item",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_role_allowed",
                schema: "sales",
                table: "user",
                sql: "role IN ('admin', 'seller')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_username_normalized",
                schema: "sales",
                table: "user",
                sql: "username = lower(btrim(username))");

            migrationBuilder.CreateIndex(
                name: "IX_sale_item_sale_id_product_id",
                schema: "sales",
                table: "sale_item",
                columns: new[] { "sale_id", "product_id" },
                unique: true)
                .Annotation("Npgsql:IndexInclude", new[] { "quantity", "unit_price" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_sale_item_quantity_positive",
                schema: "sales",
                table: "sale_item",
                sql: "quantity > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_product_price_positive",
                schema: "sales",
                table: "product",
                sql: "price > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_category_name_not_blank",
                schema: "sales",
                table: "category",
                sql: "btrim(name) <> ''");

            migrationBuilder.AddForeignKey(
                name: "FK_sale_item_product_product_id",
                schema: "sales",
                table: "sale_item",
                column: "product_id",
                principalSchema: "sales",
                principalTable: "product",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sale_item_product_product_id",
                schema: "sales",
                table: "sale_item");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_role_allowed",
                schema: "sales",
                table: "user");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_username_normalized",
                schema: "sales",
                table: "user");

            migrationBuilder.DropIndex(
                name: "IX_sale_item_sale_id_product_id",
                schema: "sales",
                table: "sale_item");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sale_item_quantity_positive",
                schema: "sales",
                table: "sale_item");

            migrationBuilder.DropCheckConstraint(
                name: "ck_product_price_positive",
                schema: "sales",
                table: "product");

            migrationBuilder.DropCheckConstraint(
                name: "ck_category_name_not_blank",
                schema: "sales",
                table: "category");

            migrationBuilder.AlterColumn<Guid>(
                name: "sale_id",
                schema: "sales",
                table: "sale_item",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_sale_item_sale_id",
                schema: "sales",
                table: "sale_item",
                column: "sale_id");
        }
    }
}
