using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PromotionSkuQuotaAndBuyerLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "quota",
                schema: "promo",
                table: "promotion_skus",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sold",
                schema: "promo",
                table: "promotion_skus",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "promotion_sku_buyers",
                schema: "promo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    promotion_sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promotion_sku_buyers", x => x.id);
                    table.CheckConstraint("ck_promotion_sku_buyers_quantity", "quantity >= 0");
                    table.ForeignKey(
                        name: "fk_promotion_sku_buyers_promotion_skus_promotion_sku_id",
                        column: x => x.promotion_sku_id,
                        principalSchema: "promo",
                        principalTable: "promotion_skus",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_promotion_sku_buyers_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_promotion_skus_sold",
                schema: "promo",
                table: "promotion_skus",
                sql: "sold >= 0 AND (quota IS NULL OR sold <= quota)");

            migrationBuilder.CreateIndex(
                name: "ix_promotion_sku_buyers_user_id",
                schema: "promo",
                table: "promotion_sku_buyers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_promotion_sku_buyers",
                schema: "promo",
                table: "promotion_sku_buyers",
                columns: new[] { "promotion_sku_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "promotion_sku_buyers",
                schema: "promo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_promotion_skus_sold",
                schema: "promo",
                table: "promotion_skus");

            migrationBuilder.DropColumn(
                name: "quota",
                schema: "promo",
                table: "promotion_skus");

            migrationBuilder.DropColumn(
                name: "sold",
                schema: "promo",
                table: "promotion_skus");
        }
    }
}
