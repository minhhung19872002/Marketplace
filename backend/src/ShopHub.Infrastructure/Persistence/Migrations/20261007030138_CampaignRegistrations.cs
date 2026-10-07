using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CampaignRegistrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "campaign_registrations",
                schema: "promo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reject_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_registrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_campaign_registrations_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalSchema: "promo",
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_campaign_registrations_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_campaign_registrations_product_id",
                schema: "promo",
                table: "campaign_registrations",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_registrations_shop",
                schema: "promo",
                table: "campaign_registrations",
                columns: new[] { "shop_id", "campaign_id" });

            migrationBuilder.CreateIndex(
                name: "ix_campaign_registrations_status",
                schema: "promo",
                table: "campaign_registrations",
                columns: new[] { "campaign_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_campaign_registrations",
                schema: "promo",
                table: "campaign_registrations",
                columns: new[] { "campaign_id", "product_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "campaign_registrations",
                schema: "promo");
        }
    }
}
