using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleStockFlow.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SaleAuthorWithReferentialIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The rename comes first and on its own: with the identifier arriving beside it,
            // "sold_by" stops saying which of the two fields it is (T-12). Type and nullability
            // do not move -- varchar(120) NOT NULL, exactly as before.
            migrationBuilder.RenameColumn(
                name: "sold_by",
                schema: "sales",
                table: "sale",
                newName: "sold_by_username");

            // Nullable, and not the NOT NULL with an empty-Guid default the scaffolder wrote.
            // T-12's brief says sale holds zero rows and the column lands NOT NULL for free; that
            // stopped being true with the first real sale. The default would have stamped every
            // existing sale with 00000000-0000-0000-0000-000000000000 -- an author that does not
            // exist, in an accounting record, which the brief itself calls worse than no field.
            migrationBuilder.AddColumn<Guid>(
                name: "sold_by_user_id",
                schema: "sales",
                table: "sale",
                type: "uuid",
                nullable: true);

            // Every existing sale resolves to a real user by the name it froze -- verified against
            // the engine before this was written, 18 of 18, none invented. If one did not, the
            // NOT NULL below would refuse and this migration would stop rather than make one up.
            migrationBuilder.Sql("""
                UPDATE sales.sale AS sale
                SET sold_by_user_id = account.id
                FROM sales."user" AS account
                WHERE account.username = sale.sold_by_username
                  AND sale.sold_by_user_id IS NULL;
                """);

            // Only now, with every row filled, and with no default: a default would let a future
            // writer forget the author and never be told.
            migrationBuilder.Sql("""
                ALTER TABLE sales.sale ALTER COLUMN sold_by_user_id SET NOT NULL;
                """);

            // EF's convention indexes every foreign key. T-12 asks for no index here -- plan.md
            // §3.3 rules it out because no access pattern reads by author and DP-02 closed the
            // report to any breakdown by seller. Removing it by hand would leave the model
            // snapshot disagreeing with the database and the next migration would put it back,
            // so it stays and the deviation is declared instead of hidden.
            migrationBuilder.CreateIndex(
                name: "IX_sale_sold_by_user_id",
                schema: "sales",
                table: "sale",
                column: "sold_by_user_id");

            // FK-4. RESTRICT and not CASCADE: deleting a user must never delete the sales they
            // registered, and the authorship of an accounting record must never be left orphaned.
            migrationBuilder.AddForeignKey(
                name: "FK_sale_user_sold_by_user_id",
                schema: "sales",
                table: "sale",
                column: "sold_by_user_id",
                principalSchema: "sales",
                principalTable: "user",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sale_user_sold_by_user_id",
                schema: "sales",
                table: "sale");

            migrationBuilder.DropIndex(
                name: "IX_sale_sold_by_user_id",
                schema: "sales",
                table: "sale");

            migrationBuilder.DropColumn(
                name: "sold_by_user_id",
                schema: "sales",
                table: "sale");

            migrationBuilder.RenameColumn(
                name: "sold_by_username",
                schema: "sales",
                table: "sale",
                newName: "sold_by");
        }
    }
}
