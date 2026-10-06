using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CartCheckoutAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "logistics");

            migrationBuilder.EnsureSchema(
                name: "sales");

            migrationBuilder.EnsureSchema(
                name: "promo");

            migrationBuilder.CreateTable(
                name: "carriers",
                schema: "logistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    supports_cod = table.Column<bool>(type: "boolean", nullable: false),
                    same_province_only = table.Column<bool>(type: "boolean", nullable: false),
                    days_same_province = table.Column<int>(type: "integer", nullable: false),
                    days_same_region = table.Column<int>(type: "integer", nullable: false),
                    days_cross_region = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carriers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "carts",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guest_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carts", x => x.id);
                    table.CheckConstraint("ck_carts_owner", "(user_id IS NULL) <> (guest_token IS NULL)");
                    table.ForeignKey(
                        name: "fk_carts_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "checkout_sessions",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subtotal = table.Column<long>(type: "bigint", nullable: false),
                    shipping_fee = table.Column<long>(type: "bigint", nullable: false),
                    shipping_discount = table.Column<long>(type: "bigint", nullable: false),
                    discount_total = table.Column<long>(type: "bigint", nullable: false),
                    coin_used = table.Column<long>(type: "bigint", nullable: false),
                    grand_total = table.Column<long>(type: "bigint", nullable: false),
                    platform_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    freeship_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checkout_sessions", x => x.id);
                    table.CheckConstraint("ck_checkout_totals", "subtotal >= 0 AND shipping_fee >= 0 AND shipping_discount >= 0 AND discount_total >= 0 AND coin_used >= 0 AND grand_total >= 0");
                    table.ForeignKey(
                        name: "fk_checkout_sessions_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "coin_ledger",
                schema: "promo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delta = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ref_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ref_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coin_ledger", x => x.id);
                    table.CheckConstraint("ck_coin_ledger_delta", "delta <> 0");
                    table.ForeignKey(
                        name: "fk_coin_ledger_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_webhook_events",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_webhook_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "simulated_payments",
                schema: "sys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    txn_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    refunded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_simulated_payments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vouchers",
                schema: "promo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    owner = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    discount_value = table.Column<long>(type: "bigint", nullable: false),
                    discount_percent_bp = table.Column<int>(type: "integer", nullable: false),
                    max_discount = table.Column<long>(type: "bigint", nullable: true),
                    min_order = table.Column<long>(type: "bigint", nullable: false),
                    audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    category_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    product_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total_quota = table.Column<int>(type: "integer", nullable: true),
                    used_count = table.Column<int>(type: "integer", nullable: false),
                    per_user_limit = table.Column<int>(type: "integer", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vouchers", x => x.id);
                    table.CheckConstraint("ck_vouchers_owner", "(owner = 'Shop') = (shop_id IS NOT NULL)");
                    table.CheckConstraint("ck_vouchers_period", "end_at > start_at");
                    table.CheckConstraint("ck_vouchers_quota", "used_count >= 0 AND (total_quota IS NULL OR used_count <= total_quota)");
                    table.ForeignKey(
                        name: "fk_vouchers_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shop",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipping_rates",
                schema: "logistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    carrier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    weight_from_g = table.Column<int>(type: "integer", nullable: false),
                    weight_to_g = table.Column<int>(type: "integer", nullable: true),
                    fee = table.Column<long>(type: "bigint", nullable: false),
                    extra_per500g = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipping_rates", x => x.id);
                    table.CheckConstraint("ck_shipping_rates_band", "weight_from_g >= 0 AND (weight_to_g IS NULL OR weight_to_g > weight_from_g) AND fee >= 0");
                    table.ForeignKey(
                        name: "fk_shipping_rates_carriers_carrier_id",
                        column: x => x.carrier_id,
                        principalSchema: "logistics",
                        principalTable: "carriers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cart_items",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    cart_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    price_at_add = table.Column<long>(type: "bigint", nullable: false),
                    is_selected = table.Column<bool>(type: "boolean", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cart_items", x => x.id);
                    table.CheckConstraint("ck_cart_items_quantity", "quantity BETWEEN 1 AND 999");
                    table.ForeignKey(
                        name: "fk_cart_items_carts_cart_id",
                        column: x => x.cart_id,
                        principalSchema: "sales",
                        principalTable: "carts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cart_items_skus_sku_id",
                        column: x => x.sku_id,
                        principalSchema: "catalog",
                        principalTable: "skus",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    checkout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    payment_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    subtotal = table.Column<long>(type: "bigint", nullable: false),
                    shop_discount = table.Column<long>(type: "bigint", nullable: false),
                    platform_discount = table.Column<long>(type: "bigint", nullable: false),
                    shipping_fee = table.Column<long>(type: "bigint", nullable: false),
                    shipping_discount = table.Column<long>(type: "bigint", nullable: false),
                    coin_used = table.Column<long>(type: "bigint", nullable: false),
                    grand_total = table.Column<long>(type: "bigint", nullable: false),
                    shop_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expected_delivery_days = table.Column<int>(type: "integer", nullable: false),
                    buyer_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_by = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    auto_complete_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_amounts", "subtotal >= 0 AND shop_discount >= 0 AND platform_discount >= 0 AND shipping_fee >= 0 AND shipping_discount >= 0 AND coin_used >= 0 AND grand_total >= 0 AND shipping_discount <= shipping_fee");
                    table.CheckConstraint("ck_orders_total", "grand_total = subtotal - shop_discount - platform_discount + shipping_fee - shipping_discount - coin_used");
                    table.ForeignKey(
                        name: "fk_orders_checkout_sessions_checkout_id",
                        column: x => x.checkout_id,
                        principalSchema: "sales",
                        principalTable: "checkout_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shop",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_users_buyer_id",
                        column: x => x.buyer_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    checkout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_txn_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    raw = table.Column<string>(type: "jsonb", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    redirect_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refunded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_payments_checkout_sessions_checkout_id",
                        column: x => x.checkout_id,
                        principalSchema: "sales",
                        principalTable: "checkout_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "voucher_claims",
                schema: "promo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voucher_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_voucher_claims_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_voucher_claims_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalSchema: "promo",
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "voucher_usages",
                schema: "promo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checkout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reverted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voucher_usages", x => x.id);
                    table.CheckConstraint("ck_voucher_usages_amount", "amount >= 0");
                    table.ForeignKey(
                        name: "fk_voucher_usages_checkout_sessions_checkout_id",
                        column: x => x.checkout_id,
                        principalSchema: "sales",
                        principalTable: "checkout_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_voucher_usages_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalSchema: "promo",
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "voucher_user_counters",
                schema: "promo",
                columns: table => new
                {
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    used_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voucher_user_counters", x => new { x.voucher_id, x.user_id });
                    table.CheckConstraint("ck_voucher_user_counters_used", "used_count >= 0");
                    table.ForeignKey(
                        name: "fk_voucher_user_counters_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalSchema: "promo",
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_snapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    variant_snapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    image_snapshot = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    unit_price = table.Column<long>(type: "bigint", nullable: false),
                    original_price = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    line_total = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_items", x => x.id);
                    table.CheckConstraint("ck_order_items_quantity", "quantity > 0");
                    table.CheckConstraint("ck_order_items_total", "line_total = unit_price * quantity AND unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "sales",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_order_items_skus_sku_id",
                        column: x => x.sku_id,
                        principalSchema: "catalog",
                        principalTable: "skus",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_status_history",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_status_history_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "sales",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_item_discounts",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ref_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_item_discounts", x => x.id);
                    table.CheckConstraint("ck_order_item_discounts_amount", "amount >= 0");
                    table.ForeignKey(
                        name: "fk_order_item_discounts_order_items_order_item_id",
                        column: x => x.order_item_id,
                        principalSchema: "sales",
                        principalTable: "order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_carriers_code",
                schema: "logistics",
                table: "carriers",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cart_items_sku_id",
                schema: "sales",
                table: "cart_items",
                column: "sku_id");

            migrationBuilder.CreateIndex(
                name: "ux_cart_items_cart_sku",
                schema: "sales",
                table: "cart_items",
                columns: new[] { "cart_id", "sku_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_carts_guest",
                schema: "sales",
                table: "carts",
                column: "guest_token",
                unique: true,
                filter: "guest_token IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_carts_user",
                schema: "sales",
                table: "carts",
                column: "user_id",
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_checkout_awaiting",
                schema: "sales",
                table: "checkout_sessions",
                columns: new[] { "status", "payment_expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_checkout_idem",
                schema: "sales",
                table: "checkout_sessions",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coin_ledger_user",
                schema: "promo",
                table: "coin_ledger",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_order_item_discounts_order_item_id",
                schema: "sales",
                table: "order_item_discounts",
                column: "order_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_order_id",
                schema: "sales",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_sku",
                schema: "sales",
                table: "order_items",
                column: "sku_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_status_history_order",
                schema: "sales",
                table: "order_status_history",
                columns: new[] { "order_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_buyer",
                schema: "sales",
                table: "orders",
                columns: new[] { "buyer_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_orders_checkout",
                schema: "sales",
                table: "orders",
                column: "checkout_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_shop_status",
                schema: "sales",
                table: "orders",
                columns: new[] { "shop_id", "status", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_orders_code",
                schema: "sales",
                table: "orders",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_webhook_event",
                schema: "sales",
                table: "payment_webhook_events",
                columns: new[] { "provider", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_checkout",
                schema: "sales",
                table: "payments",
                column: "checkout_id");

            migrationBuilder.CreateIndex(
                name: "ux_payment_txn",
                schema: "sales",
                table: "payments",
                columns: new[] { "method", "provider_txn_id" },
                unique: true,
                filter: "provider_txn_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_shipping_rates_band",
                schema: "logistics",
                table: "shipping_rates",
                columns: new[] { "carrier_id", "zone", "weight_from_g" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_simulated_payments_payment",
                schema: "sys",
                table: "simulated_payments",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_voucher_claims_user",
                schema: "promo",
                table: "voucher_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_voucher_claims",
                schema: "promo",
                table: "voucher_claims",
                columns: new[] { "voucher_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_voucher_usages_checkout",
                schema: "promo",
                table: "voucher_usages",
                column: "checkout_id");

            migrationBuilder.CreateIndex(
                name: "ix_voucher_usages_user",
                schema: "promo",
                table: "voucher_usages",
                columns: new[] { "voucher_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_owner",
                schema: "promo",
                table: "vouchers",
                columns: new[] { "owner", "shop_id", "end_at" });

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_shop_id",
                schema: "promo",
                table: "vouchers",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ux_voucher_code",
                schema: "promo",
                table: "vouchers",
                column: "code",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cart_items",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "coin_ledger",
                schema: "promo");

            migrationBuilder.DropTable(
                name: "order_item_discounts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "order_status_history",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "payment_webhook_events",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "shipping_rates",
                schema: "logistics");

            migrationBuilder.DropTable(
                name: "simulated_payments",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "voucher_claims",
                schema: "promo");

            migrationBuilder.DropTable(
                name: "voucher_usages",
                schema: "promo");

            migrationBuilder.DropTable(
                name: "voucher_user_counters",
                schema: "promo");

            migrationBuilder.DropTable(
                name: "carts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "order_items",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "carriers",
                schema: "logistics");

            migrationBuilder.DropTable(
                name: "vouchers",
                schema: "promo");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "checkout_sessions",
                schema: "sales");
        }
    }
}
