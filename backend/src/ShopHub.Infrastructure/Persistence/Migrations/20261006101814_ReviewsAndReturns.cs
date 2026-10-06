using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewsAndReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "rating_avg",
                schema: "shop",
                table: "shops",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "rating_count",
                schema: "shop",
                table: "shops",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "return_id",
                schema: "logistics",
                table: "shipments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "return_requests",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_amount = table.Column<long>(type: "bigint", nullable: false),
                    requested_coins = table.Column<long>(type: "bigint", nullable: false),
                    offered_amount = table.Column<long>(type: "bigint", nullable: true),
                    refund_amount = table.Column<long>(type: "bigint", nullable: true),
                    refund_coins = table.Column<long>(type: "bigint", nullable: true),
                    shop_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    respond_by = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    restock = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    refunded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_requests", x => x.id);
                    table.CheckConstraint("ck_returns_amounts", "requested_amount >= 0 AND requested_coins >= 0 AND (refund_amount IS NULL OR refund_amount <= requested_amount)");
                    table.ForeignKey(
                        name: "fk_return_requests_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "sales",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_requests_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shop",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                schema: "engage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_snapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    is_anonymous = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    seller_reply = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    replied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replied_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_hidden = table.Column<bool>(type: "boolean", nullable: false),
                    hidden_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    rewarded = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reviews", x => x.id);
                    table.CheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_reviews_order_items_order_item_id",
                        column: x => x.order_item_id,
                        principalSchema: "sales",
                        principalTable: "order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_users_buyer_id",
                        column: x => x.buyer_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "disputes",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    refund_amount = table.Column<long>(type: "bigint", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disputes", x => x.id);
                    table.ForeignKey(
                        name: "fk_disputes_return_requests_return_id",
                        column: x => x.return_id,
                        principalSchema: "sales",
                        principalTable: "return_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "return_evidences",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_evidences", x => x.id);
                    table.ForeignKey(
                        name: "fk_return_evidences_return_requests_return_id",
                        column: x => x.return_id,
                        principalSchema: "sales",
                        principalTable: "return_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "return_history",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    by = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_return_history_return_requests_return_id",
                        column: x => x.return_id,
                        principalSchema: "sales",
                        principalTable: "return_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "return_items",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    refund_amount = table.Column<long>(type: "bigint", nullable: false),
                    refund_coins = table.Column<long>(type: "bigint", nullable: false),
                    is_open = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_items", x => x.id);
                    table.CheckConstraint("ck_return_items", "quantity > 0 AND refund_amount >= 0 AND refund_coins >= 0");
                    table.ForeignKey(
                        name: "fk_return_items_order_items_order_item_id",
                        column: x => x.order_item_id,
                        principalSchema: "sales",
                        principalTable: "order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_items_return_requests_return_id",
                        column: x => x.return_id,
                        principalSchema: "sales",
                        principalTable: "return_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_media",
                schema: "engage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_media", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_media_reviews_review_id",
                        column: x => x.review_id,
                        principalSchema: "engage",
                        principalTable: "reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_reports",
                schema: "engage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_reports_reviews_review_id",
                        column: x => x.review_id,
                        principalSchema: "engage",
                        principalTable: "reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_return",
                schema: "logistics",
                table: "shipments",
                column: "return_id");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_open",
                schema: "sales",
                table: "disputes",
                column: "closed_at");

            migrationBuilder.CreateIndex(
                name: "ux_disputes_return",
                schema: "sales",
                table: "disputes",
                column: "return_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_evidences_return_id",
                schema: "sales",
                table: "return_evidences",
                column: "return_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_history_return_id",
                schema: "sales",
                table: "return_history",
                column: "return_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_items_return_id",
                schema: "sales",
                table: "return_items",
                column: "return_id");

            migrationBuilder.CreateIndex(
                name: "ux_return_items_open",
                schema: "sales",
                table: "return_items",
                column: "order_item_id",
                unique: true,
                filter: "is_open");

            migrationBuilder.CreateIndex(
                name: "ix_return_requests_order_id",
                schema: "sales",
                table: "return_requests",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_returns_buyer",
                schema: "sales",
                table: "return_requests",
                columns: new[] { "buyer_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_returns_due",
                schema: "sales",
                table: "return_requests",
                columns: new[] { "status", "respond_by" });

            migrationBuilder.CreateIndex(
                name: "ix_returns_shop",
                schema: "sales",
                table: "return_requests",
                columns: new[] { "shop_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_returns_code",
                schema: "sales",
                table: "return_requests",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_review_media_review_id",
                schema: "engage",
                table: "review_media",
                column: "review_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_reports_status",
                schema: "engage",
                table: "review_reports",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_review_reports",
                schema: "engage",
                table: "review_reports",
                columns: new[] { "review_id", "reporter_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reviews_buyer_id",
                schema: "engage",
                table: "reviews",
                column: "buyer_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_product",
                schema: "engage",
                table: "reviews",
                columns: new[] { "product_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_shop",
                schema: "engage",
                table: "reviews",
                columns: new[] { "shop_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_review_item",
                schema: "engage",
                table: "reviews",
                column: "order_item_id",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "disputes",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "return_evidences",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "return_history",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "return_items",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "review_media",
                schema: "engage");

            migrationBuilder.DropTable(
                name: "review_reports",
                schema: "engage");

            migrationBuilder.DropTable(
                name: "return_requests",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "reviews",
                schema: "engage");

            migrationBuilder.DropIndex(
                name: "ix_shipments_return",
                schema: "logistics",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "rating_avg",
                schema: "shop",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "rating_count",
                schema: "shop",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "return_id",
                schema: "logistics",
                table: "shipments");
        }
    }
}
