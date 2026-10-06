using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShopCategoriesAndDecoration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shop_categories",
                schema: "shop",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_visible = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_shop_categories_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shop",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shop_decorations",
                schema: "shop",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layout = table.Column<string>(type: "jsonb", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_decorations", x => x.id);
                    table.ForeignKey(
                        name: "fk_shop_decorations_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shop",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shop_category_products",
                schema: "shop",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    shop_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_category_products", x => x.id);
                    table.ForeignKey(
                        name: "fk_shop_category_products_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shop_category_products_shop_categories_shop_category_id",
                        column: x => x.shop_category_id,
                        principalSchema: "shop",
                        principalTable: "shop_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shop_categories_shop",
                schema: "shop",
                table: "shop_categories",
                columns: new[] { "shop_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_shop_category_products_product",
                schema: "shop",
                table: "shop_category_products",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ux_shop_category_products",
                schema: "shop",
                table: "shop_category_products",
                columns: new[] { "shop_category_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_shop_decorations_shop",
                schema: "shop",
                table: "shop_decorations",
                column: "shop_id",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shop_category_products",
                schema: "shop");

            migrationBuilder.DropTable(
                name: "shop_decorations",
                schema: "shop");

            migrationBuilder.DropTable(
                name: "shop_categories",
                schema: "shop");
        }
    }
}
