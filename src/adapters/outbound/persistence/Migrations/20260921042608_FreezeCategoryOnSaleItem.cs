using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleStockFlow.Adapters.Persistence.Migrations
{
    /// <summary>
    /// T-11. The line of a sale already freezes the product's name and its unit price; from here it
    /// freezes the category's label too, so the report can group BY it instead of resolving it
    /// against the live catalogue, which ADR-004 forbids because it would rewrite closed periods.
    /// </summary>
    public partial class FreezeCategoryOnSaleItem : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable first, and not the NOT NULL the scaffolder wrote with an empty-string default.
            // T-11's own brief said sale_item held zero rows and the column could land NOT NULL for
            // free; that window closed with the first real sale. An empty default would have filled
            // every existing line with the very blank this task exists to remove.
            migrationBuilder.AddColumn<string>(
                name: "category_name",
                schema: "sales",
                table: "sale_item",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            // The only honest value available for a line already written: the category its product
            // carries TODAY. Nobody recorded the one it carried when it was sold, so these rows are
            // an approximation and are declared as such in data-model.md. Every line written from
            // here on freezes the real label at the instant of the sale.
            migrationBuilder.Sql("""
                UPDATE sales.sale_item AS item
                SET category_name = category.name
                FROM sales.product AS product
                JOIN sales.category AS category ON category.id = product.category_id
                WHERE product.id = item.product_id AND item.category_name IS NULL;
                """);

            // FK-3 is RESTRICT, so a line whose product vanished cannot exist and this cannot fire.
            // It stays because a migration that trips halfway through a deployment is worse than a
            // label that admits it does not know, and the cost of being wrong here is one UPDATE.
            migrationBuilder.Sql("""
                UPDATE sales.sale_item
                SET category_name = 'Sin categoría'
                WHERE category_name IS NULL;
                """);

            // Only now, with every row filled. No column default: the schema declares none anywhere,
            // and a default here would let a future writer forget the label and never be told.
            migrationBuilder.Sql("""
                ALTER TABLE sales.sale_item ALTER COLUMN category_name SET NOT NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "category_name",
                schema: "sales",
                table: "sale_item");
        }
    }
}
