using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StorefrontAndSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_categories_parent_slug",
                schema: "catalog",
                table: "categories");

            migrationBuilder.EnsureSchema(
                name: "engage");

            migrationBuilder.AddColumn<int>(
                name: "follower_count",
                schema: "shop",
                table: "shops",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "product_count",
                schema: "shop",
                table: "shops",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "product_views",
                schema: "engage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    viewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_views", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_views_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "search_logs",
                schema: "engage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    keyword = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_count = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shop_followers",
                schema: "shop",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_followers", x => x.id);
                    table.ForeignKey(
                        name: "fk_shop_followers_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shop",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shop_followers_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wishlists",
                schema: "engage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wishlists", x => x.id);
                    table.ForeignKey(
                        name: "fk_wishlists_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_wishlists_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent_id",
                schema: "catalog",
                table: "categories",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ux_categories_slug",
                schema: "catalog",
                table: "categories",
                column: "slug",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_product_views_product",
                schema: "engage",
                table: "product_views",
                columns: new[] { "product_id", "viewed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_product_views_session",
                schema: "engage",
                table: "product_views",
                columns: new[] { "session_key", "viewed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_product_views_user",
                schema: "engage",
                table: "product_views",
                columns: new[] { "user_id", "viewed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_search_logs_keyword",
                schema: "engage",
                table: "search_logs",
                column: "keyword")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_search_logs_time",
                schema: "engage",
                table: "search_logs",
                columns: new[] { "occurred_at", "keyword" });

            migrationBuilder.CreateIndex(
                name: "ix_shop_followers_user",
                schema: "shop",
                table: "shop_followers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_shop_followers_shop_user",
                schema: "shop",
                table: "shop_followers",
                columns: new[] { "shop_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wishlists_product",
                schema: "engage",
                table: "wishlists",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ux_wishlists_user_product",
                schema: "engage",
                table: "wishlists",
                columns: new[] { "user_id", "product_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_views",
                schema: "engage");

            migrationBuilder.DropTable(
                name: "search_logs",
                schema: "engage");

            migrationBuilder.DropTable(
                name: "shop_followers",
                schema: "shop");

            migrationBuilder.DropTable(
                name: "wishlists",
                schema: "engage");

            migrationBuilder.DropIndex(
                name: "ix_categories_parent_id",
                schema: "catalog",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "ux_categories_slug",
                schema: "catalog",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "follower_count",
                schema: "shop",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "product_count",
                schema: "shop",
                table: "shops");

            migrationBuilder.CreateIndex(
                name: "ux_categories_parent_slug",
                schema: "catalog",
                table: "categories",
                columns: new[] { "parent_id", "slug" },
                unique: true,
                filter: "deleted_at IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
