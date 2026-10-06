using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Marketing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "gift_promotion_id",
                schema: "sales",
                table: "order_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "price_ref_id",
                schema: "sales",
                table: "order_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "price_source",
                schema: "sales",
                table: "order_items",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "banners",
                schema: "promo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    position = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_banners", x => x.id);
                    table.CheckConstraint("ck_banners_period", "end_at > start_at");
                });

            migrationBuilder.CreateTable(
                name: "campaigns",
                schema: "promo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    blocks = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaigns", x => x.id);
                    table.CheckConstraint("ck_campaigns_period", "end_at > start_at");
                });

            migrationBuilder.CreateTable(
                name: "check_ins",
                schema: "engage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    streak_day = table.Column<int>(type: "integer", nullable: false),
                    coins = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_ins", x => x.id);
                    table.CheckConstraint("ck_check_ins_streak", "streak_day BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "fk_check_ins_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flash_sale_slots",
                schema: "promo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    owner = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    min_discount_bp = table.Column<int>(type: "integer", nullable: false),
                    min_rating = table.Column<double>(type: "double precision", nullable: false),
                    category_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flash_sale_slots", x => x.id);
                    table.CheckConstraint("ck_flash_sale_slots_period", "end_at > start_at");
                    table.ForeignKey(
                        name: "fk_flash_sale_slots_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shop",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "price_programs",
                schema: "promo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ref_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price = table.Column<long>(type: "bigint", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_programs", x => x.id);
                    table.CheckConstraint("ck_price_programs_period", "end_at > start_at");
                    table.CheckConstraint("ck_price_programs_price", "price > 0");
                    table.ForeignKey(
                        name: "fk_price_programs_skus_sku_id",
                        column: x => x.sku_id,
                        principalSchema: "catalog",
                        principalTable: "skus",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "promotions",
                schema: "promo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    min_quantity = table.Column<int>(type: "integer", nullable: false),
                    discount_bp = table.Column<int>(type: "integer", nullable: false),
                    discount_amount = table.Column<long>(type: "bigint", nullable: false),
                    max_add_on_quantity = table.Column<int>(type: "integer", nullable: false),
                    min_spend = table.Column<long>(type: "bigint", nullable: false),
                    gift_sku_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gift_quantity = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stopped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promotions", x => x.id);
                    table.CheckConstraint("ck_promotions_period", "end_at > start_at");
                    table.ForeignKey(
                        name: "fk_promotions_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shop",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "flash_sale_items",
                schema: "promo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    slot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    flash_price = table.Column<long>(type: "bigint", nullable: false),
                    quota = table.Column<int>(type: "integer", nullable: false),
                    sold = table.Column<int>(type: "integer", nullable: false),
                    per_user_limit = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reject_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flash_sale_items", x => x.id);
                    table.CheckConstraint("ck_flash_sale_items_sold", "sold >= 0 AND sold <= quota");
                    table.ForeignKey(
                        name: "fk_flash_sale_items_flash_sale_slots_slot_id",
                        column: x => x.slot_id,
                        principalSchema: "promo",
                        principalTable: "flash_sale_slots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_flash_sale_items_skus_sku_id",
                        column: x => x.sku_id,
                        principalSchema: "catalog",
                        principalTable: "skus",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "promotion_products",
                schema: "promo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promotion_products", x => x.id);
                    table.ForeignKey(
                        name: "fk_promotion_products_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_promotion_products_promotions_promotion_id",
                        column: x => x.promotion_id,
                        principalSchema: "promo",
                        principalTable: "promotions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "promotion_skus",
                schema: "promo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price = table.Column<long>(type: "bigint", nullable: false),
                    per_user_limit = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promotion_skus", x => x.id);
                    table.CheckConstraint("ck_promotion_skus_price", "price > 0");
                    table.ForeignKey(
                        name: "fk_promotion_skus_promotions_promotion_id",
                        column: x => x.promotion_id,
                        principalSchema: "promo",
                        principalTable: "promotions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_promotion_skus_skus_sku_id",
                        column: x => x.sku_id,
                        principalSchema: "catalog",
                        principalTable: "skus",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "flash_sale_buyers",
                schema: "promo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flash_sale_buyers", x => x.id);
                    table.CheckConstraint("ck_flash_sale_buyers_quantity", "quantity >= 0");
                    table.ForeignKey(
                        name: "fk_flash_sale_buyers_flash_sale_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "promo",
                        principalTable: "flash_sale_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_flash_sale_buyers_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_banners_position",
                schema: "promo",
                table: "banners",
                columns: new[] { "position", "start_at", "end_at" });

            migrationBuilder.CreateIndex(
                name: "ux_campaigns_slug",
                schema: "promo",
                table: "campaigns",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_check_ins_user_day",
                schema: "engage",
                table: "check_ins",
                columns: new[] { "user_id", "day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_flash_sale_buyers_user_id",
                schema: "promo",
                table: "flash_sale_buyers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_flash_sale_buyers",
                schema: "promo",
                table: "flash_sale_buyers",
                columns: new[] { "item_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_flash_sale_items_shop",
                schema: "promo",
                table: "flash_sale_items",
                columns: new[] { "shop_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_flash_sale_items_sku_id",
                schema: "promo",
                table: "flash_sale_items",
                column: "sku_id");

            migrationBuilder.CreateIndex(
                name: "ux_flash_sale_items_slot_sku",
                schema: "promo",
                table: "flash_sale_items",
                columns: new[] { "slot_id", "sku_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_flash_sale_slots_shop_id",
                schema: "promo",
                table: "flash_sale_slots",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_flash_sale_slots_start",
                schema: "promo",
                table: "flash_sale_slots",
                columns: new[] { "owner", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_price_programs_ref",
                schema: "promo",
                table: "price_programs",
                columns: new[] { "kind", "ref_id" });

            migrationBuilder.CreateIndex(
                name: "ix_price_programs_sku",
                schema: "promo",
                table: "price_programs",
                columns: new[] { "sku_id", "start_at", "end_at" },
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_promotion_products_product",
                schema: "promo",
                table: "promotion_products",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ux_promotion_products",
                schema: "promo",
                table: "promotion_products",
                columns: new[] { "promotion_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_promotion_skus_sku",
                schema: "promo",
                table: "promotion_skus",
                column: "sku_id");

            migrationBuilder.CreateIndex(
                name: "ux_promotion_skus",
                schema: "promo",
                table: "promotion_skus",
                columns: new[] { "promotion_id", "sku_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_promotions_shop",
                schema: "promo",
                table: "promotions",
                columns: new[] { "shop_id", "status", "end_at" });

            // One price programme per SKU at any moment (spec 3.10, 6.2): discount, shop flash sale and platform flash sale share it
            migrationBuilder.Sql("""
                ALTER TABLE promo.price_programs ADD CONSTRAINT ex_price_programs_sku_period
                EXCLUDE USING gist (sku_id WITH =, tstzrange(start_at, end_at) WITH &&) WHERE (is_active);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE promo.price_programs DROP CONSTRAINT IF EXISTS ex_price_programs_sku_period;");

            migrationBuilder.DropTable(
                name: "banners",
                schema: "promo");

            migrationBuilder.DropTable(
                name: "campaigns",
                schema: "promo");

            migrationBuilder.DropTable(
                name: "check_ins",
                schema: "engage");

            migrationBuilder.DropTable(
                name: "flash_sale_buyers",
                schema: "promo");

            migrationBuilder.DropTable(
                name: "price_programs",
                schema: "promo");

            migrationBuilder.DropTable(
                name: "promotion_products",
                schema: "promo");

            migrationBuilder.DropTable(
                name: "promotion_skus",
                schema: "promo");

            migrationBuilder.DropTable(
                name: "flash_sale_items",
                schema: "promo");

            migrationBuilder.DropTable(
                name: "promotions",
                schema: "promo");

            migrationBuilder.DropTable(
                name: "flash_sale_slots",
                schema: "promo");

            migrationBuilder.DropColumn(
                name: "gift_promotion_id",
                schema: "sales",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "price_ref_id",
                schema: "sales",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "price_source",
                schema: "sales",
                table: "order_items");
        }
    }
}
