using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdminReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                schema: "shop",
                table: "shop_penalties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "given_by",
                schema: "shop",
                table: "shop_penalties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "revoke_reason",
                schema: "shop",
                table: "shop_penalties",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "revoked_at",
                schema: "shop",
                table: "shop_penalties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "engage",
                table: "product_views",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Direct");

            migrationBuilder.CreateTable(
                name: "cart_adds",
                schema: "engage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cart_adds", x => x.id);
                    table.CheckConstraint("ck_cart_adds_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_cart_adds_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cms_pages",
                schema: "sys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    topic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cms_pages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "message_templates",
                schema: "sys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    placeholders = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "product_reports",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    handled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    handled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_reports_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cart_adds_at",
                schema: "engage",
                table: "cart_adds",
                column: "added_at");

            migrationBuilder.CreateIndex(
                name: "ix_cart_adds_product",
                schema: "engage",
                table: "cart_adds",
                columns: new[] { "product_id", "added_at" });

            migrationBuilder.CreateIndex(
                name: "ux_cms_pages_slug",
                schema: "sys",
                table: "cms_pages",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_message_templates_key",
                schema: "sys",
                table: "message_templates",
                columns: new[] { "key", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_reports_status",
                schema: "catalog",
                table: "product_reports",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_product_reports_open",
                schema: "catalog",
                table: "product_reports",
                columns: new[] { "product_id", "reporter_id" },
                unique: true,
                filter: "status = 'Open'");

            // Automatic penalties given before expiry existed now expire like new ones (SHOP.PENALTY_EXPIRY_DAYS default 90)
            migrationBuilder.Sql("UPDATE shop.shop_penalties SET expires_at = created_at + interval '90 days' WHERE expires_at IS NULL AND given_by IS NULL;");
            migrationBuilder.Sql(@"UPDATE shop.shops s SET penalty_points = COALESCE((SELECT SUM(points) FROM shop.shop_penalties p
                WHERE p.shop_id = s.id AND p.revoked_at IS NULL AND (p.expires_at IS NULL OR p.expires_at > now())), 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cart_adds",
                schema: "engage");

            migrationBuilder.DropTable(
                name: "cms_pages",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "message_templates",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "product_reports",
                schema: "catalog");

            migrationBuilder.DropColumn(
                name: "expires_at",
                schema: "shop",
                table: "shop_penalties");

            migrationBuilder.DropColumn(
                name: "given_by",
                schema: "shop",
                table: "shop_penalties");

            migrationBuilder.DropColumn(
                name: "revoke_reason",
                schema: "shop",
                table: "shop_penalties");

            migrationBuilder.DropColumn(
                name: "revoked_at",
                schema: "shop",
                table: "shop_penalties");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "engage",
                table: "product_views");
        }
    }
}
