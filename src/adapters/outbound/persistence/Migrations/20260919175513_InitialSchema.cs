using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SimpleStockFlow.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sales");

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sales",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sold_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sold_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    stock = table.Column<int>(type: "integer", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                    table.ForeignKey(
                        name: "FK_products_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "sales",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_items",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_sale_items_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "sales",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "sales",
                table: "categories",
                columns: new[] { "id", "name" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-4111-8111-111111111111"), "General" },
                    { new Guid("22222222-2222-4222-8222-222222222222"), "Herramientas" },
                    { new Guid("33333333-3333-4333-8333-333333333333"), "Electricidad" },
                    { new Guid("44444444-4444-4444-8444-444444444444"), "Fontaneria" },
                    { new Guid("55555555-5555-4555-8555-555555555555"), "Pinturas" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_categories_name",
                schema: "sales",
                table: "categories",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_category_id",
                schema: "sales",
                table: "products",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_products_name",
                schema: "sales",
                table: "products",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_sale_items_product_id",
                schema: "sales",
                table: "sale_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_sale_items_sale_id",
                schema: "sales",
                table: "sale_items",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_sold_at",
                schema: "sales",
                table: "sales",
                column: "sold_at");

            migrationBuilder.CreateIndex(
                name: "IX_users_username",
                schema: "sales",
                table: "users",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "products",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sale_items",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "users",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sales",
                schema: "sales");
        }
    }
}
