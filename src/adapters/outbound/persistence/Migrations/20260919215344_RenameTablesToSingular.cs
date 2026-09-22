using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleStockFlow.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameTablesToSingular : Migration
    {
        // Table names only: every column keeps its name, so no data is rewritten. The scaffolded
        // output was reviewed line by line before being accepted, because a DropTable/CreateTable
        // pair would have silently destroyed the five reference categories and the bootstrapped
        // administrator. It emits RenameTable, which Postgres executes as ALTER TABLE ... RENAME TO,
        // so the rows survive; the primary keys and the check constraint are dropped and re-added
        // only because Postgres does not rename a constraint when its table is renamed.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_products_categories_category_id",
                schema: "sales",
                table: "products");

            migrationBuilder.DropForeignKey(
                name: "FK_sale_items_sales_sale_id",
                schema: "sales",
                table: "sale_items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_users",
                schema: "sales",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_sales",
                schema: "sales",
                table: "sales");

            migrationBuilder.DropPrimaryKey(
                name: "PK_sale_items",
                schema: "sales",
                table: "sale_items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_products",
                schema: "sales",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_stock_non_negative",
                schema: "sales",
                table: "products");

            migrationBuilder.DropPrimaryKey(
                name: "PK_categories",
                schema: "sales",
                table: "categories");

            migrationBuilder.RenameTable(
                name: "users",
                schema: "sales",
                newName: "user",
                newSchema: "sales");

            migrationBuilder.RenameTable(
                name: "sales",
                schema: "sales",
                newName: "sale",
                newSchema: "sales");

            migrationBuilder.RenameTable(
                name: "sale_items",
                schema: "sales",
                newName: "sale_item",
                newSchema: "sales");

            migrationBuilder.RenameTable(
                name: "products",
                schema: "sales",
                newName: "product",
                newSchema: "sales");

            migrationBuilder.RenameTable(
                name: "categories",
                schema: "sales",
                newName: "category",
                newSchema: "sales");

            migrationBuilder.RenameIndex(
                name: "IX_users_username",
                schema: "sales",
                table: "user",
                newName: "IX_user_username");

            migrationBuilder.RenameIndex(
                name: "IX_sales_sold_at",
                schema: "sales",
                table: "sale",
                newName: "IX_sale_sold_at");

            migrationBuilder.RenameIndex(
                name: "IX_sale_items_sale_id",
                schema: "sales",
                table: "sale_item",
                newName: "IX_sale_item_sale_id");

            migrationBuilder.RenameIndex(
                name: "IX_sale_items_product_id",
                schema: "sales",
                table: "sale_item",
                newName: "IX_sale_item_product_id");

            migrationBuilder.RenameIndex(
                name: "IX_products_name",
                schema: "sales",
                table: "product",
                newName: "IX_product_name");

            migrationBuilder.RenameIndex(
                name: "IX_products_category_id",
                schema: "sales",
                table: "product",
                newName: "IX_product_category_id");

            migrationBuilder.RenameIndex(
                name: "IX_categories_name",
                schema: "sales",
                table: "category",
                newName: "IX_category_name");

            migrationBuilder.AddPrimaryKey(
                name: "PK_user",
                schema: "sales",
                table: "user",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_sale",
                schema: "sales",
                table: "sale",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_sale_item",
                schema: "sales",
                table: "sale_item",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_product",
                schema: "sales",
                table: "product",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_category",
                schema: "sales",
                table: "category",
                column: "id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_product_stock_non_negative",
                schema: "sales",
                table: "product",
                sql: "stock >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_product_category_category_id",
                schema: "sales",
                table: "product",
                column: "category_id",
                principalSchema: "sales",
                principalTable: "category",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sale_item_sale_sale_id",
                schema: "sales",
                table: "sale_item",
                column: "sale_id",
                principalSchema: "sales",
                principalTable: "sale",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_product_category_category_id",
                schema: "sales",
                table: "product");

            migrationBuilder.DropForeignKey(
                name: "FK_sale_item_sale_sale_id",
                schema: "sales",
                table: "sale_item");

            migrationBuilder.DropPrimaryKey(
                name: "PK_user",
                schema: "sales",
                table: "user");

            migrationBuilder.DropPrimaryKey(
                name: "PK_sale_item",
                schema: "sales",
                table: "sale_item");

            migrationBuilder.DropPrimaryKey(
                name: "PK_sale",
                schema: "sales",
                table: "sale");

            migrationBuilder.DropPrimaryKey(
                name: "PK_product",
                schema: "sales",
                table: "product");

            migrationBuilder.DropCheckConstraint(
                name: "ck_product_stock_non_negative",
                schema: "sales",
                table: "product");

            migrationBuilder.DropPrimaryKey(
                name: "PK_category",
                schema: "sales",
                table: "category");

            migrationBuilder.RenameTable(
                name: "user",
                schema: "sales",
                newName: "users",
                newSchema: "sales");

            migrationBuilder.RenameTable(
                name: "sale_item",
                schema: "sales",
                newName: "sale_items",
                newSchema: "sales");

            migrationBuilder.RenameTable(
                name: "sale",
                schema: "sales",
                newName: "sales",
                newSchema: "sales");

            migrationBuilder.RenameTable(
                name: "product",
                schema: "sales",
                newName: "products",
                newSchema: "sales");

            migrationBuilder.RenameTable(
                name: "category",
                schema: "sales",
                newName: "categories",
                newSchema: "sales");

            migrationBuilder.RenameIndex(
                name: "IX_user_username",
                schema: "sales",
                table: "users",
                newName: "IX_users_username");

            migrationBuilder.RenameIndex(
                name: "IX_sale_item_sale_id",
                schema: "sales",
                table: "sale_items",
                newName: "IX_sale_items_sale_id");

            migrationBuilder.RenameIndex(
                name: "IX_sale_item_product_id",
                schema: "sales",
                table: "sale_items",
                newName: "IX_sale_items_product_id");

            migrationBuilder.RenameIndex(
                name: "IX_sale_sold_at",
                schema: "sales",
                table: "sales",
                newName: "IX_sales_sold_at");

            migrationBuilder.RenameIndex(
                name: "IX_product_name",
                schema: "sales",
                table: "products",
                newName: "IX_products_name");

            migrationBuilder.RenameIndex(
                name: "IX_product_category_id",
                schema: "sales",
                table: "products",
                newName: "IX_products_category_id");

            migrationBuilder.RenameIndex(
                name: "IX_category_name",
                schema: "sales",
                table: "categories",
                newName: "IX_categories_name");

            migrationBuilder.AddPrimaryKey(
                name: "PK_users",
                schema: "sales",
                table: "users",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_sale_items",
                schema: "sales",
                table: "sale_items",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_sales",
                schema: "sales",
                table: "sales",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_products",
                schema: "sales",
                table: "products",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_categories",
                schema: "sales",
                table: "categories",
                column: "id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_stock_non_negative",
                schema: "sales",
                table: "products",
                sql: "stock >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_products_categories_category_id",
                schema: "sales",
                table: "products",
                column: "category_id",
                principalSchema: "sales",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sale_items_sales_sale_id",
                schema: "sales",
                table: "sale_items",
                column: "sale_id",
                principalSchema: "sales",
                principalTable: "sales",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
