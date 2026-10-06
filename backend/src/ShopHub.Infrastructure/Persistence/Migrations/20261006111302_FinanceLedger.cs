using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payments_checkout_sessions_checkout_id",
                schema: "sales",
                table: "payments");

            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.AddColumn<string>(
                name: "purpose",
                schema: "sales",
                table: "payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Checkout");

            migrationBuilder.CreateTable(
                name: "bank_accounts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    account_no_encrypted = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    account_no_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    account_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_accounts_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_rules",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fee_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rate_bp = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_rules", x => x.id);
                    table.CheckConstraint("ck_fee_rules_rate", "rate_bp BETWEEN 0 AND 5000");
                    table.CheckConstraint("ck_fee_rules_window", "valid_to IS NULL OR valid_to > valid_from");
                    table.ForeignKey(
                        name: "fk_fee_rules_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_accounts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    owner_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    balance = table.Column<long>(type: "bigint", nullable: false),
                    allow_negative = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_accounts", x => x.id);
                    table.CheckConstraint("ck_ledger_accounts_balance", "allow_negative OR balance >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ledger_transactions",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ref_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ref_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    dedupe_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settlements",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    period_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    gross = table.Column<long>(type: "bigint", nullable: false),
                    fees = table.Column<long>(type: "bigint", nullable: false),
                    net = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    file_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settlements", x => x.id);
                    table.ForeignKey(
                        name: "fk_settlements_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shop",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wallet_topups",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_topups", x => x.id);
                    table.CheckConstraint("ck_wallet_topups_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_wallet_topups_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wallets",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pin_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    failed_pin_attempts = table.Column<int>(type: "integer", nullable: false),
                    pin_locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pin_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallets", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallets_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "withdrawals",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    owner_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    account_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    account_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    bank_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_withdrawals", x => x.id);
                    table.CheckConstraint("ck_withdrawals_amount", "amount > 0");
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    ref_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ref_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_ledger_entries_ledger_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "finance",
                        principalTable: "ledger_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_ledger_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalSchema: "finance",
                        principalTable: "ledger_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "settlement_items",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    goods = table.Column<long>(type: "bigint", nullable: false),
                    shop_discount = table.Column<long>(type: "bigint", nullable: false),
                    refunds_borne = table.Column<long>(type: "bigint", nullable: false),
                    fixed_fee = table.Column<long>(type: "bigint", nullable: false),
                    payment_fee = table.Column<long>(type: "bigint", nullable: false),
                    service_fee = table.Column<long>(type: "bigint", nullable: false),
                    net = table.Column<long>(type: "bigint", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settlement_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_settlement_items_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "sales",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_settlement_items_settlements_settlement_id",
                        column: x => x.settlement_id,
                        principalSchema: "finance",
                        principalTable: "settlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_user",
                schema: "finance",
                table: "bank_accounts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_fee_rules_open",
                schema: "finance",
                table: "fee_rules",
                columns: new[] { "category_id", "fee_type" },
                unique: true,
                filter: "valid_to IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ux_ledger_accounts_owner",
                schema: "finance",
                table: "ledger_accounts",
                columns: new[] { "owner_type", "owner_id", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_account",
                schema: "finance",
                table: "ledger_entries",
                columns: new[] { "account_id", "posted_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_ref",
                schema: "finance",
                table: "ledger_entries",
                columns: new[] { "ref_type", "ref_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_transaction_id",
                schema: "finance",
                table: "ledger_entries",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_transactions_ref",
                schema: "finance",
                table: "ledger_transactions",
                columns: new[] { "ref_type", "ref_id" });

            migrationBuilder.CreateIndex(
                name: "ux_ledger_transactions_dedupe",
                schema: "finance",
                table: "ledger_transactions",
                column: "dedupe_key",
                unique: true,
                filter: "dedupe_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_items_settlement_id",
                schema: "finance",
                table: "settlement_items",
                column: "settlement_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_items_shop",
                schema: "finance",
                table: "settlement_items",
                columns: new[] { "shop_id", "released_at" });

            migrationBuilder.CreateIndex(
                name: "ux_settlement_items_order",
                schema: "finance",
                table: "settlement_items",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_settlements_shop",
                schema: "finance",
                table: "settlements",
                columns: new[] { "shop_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_settlements_code",
                schema: "finance",
                table: "settlements",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wallet_topups_user",
                schema: "finance",
                table: "wallet_topups",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_wallets_user",
                schema: "finance",
                table: "wallets",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_withdrawals_owner",
                schema: "finance",
                table: "withdrawals",
                columns: new[] { "owner_type", "owner_id", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_withdrawals_status",
                schema: "finance",
                table: "withdrawals",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_accounts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "fee_rules",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "settlement_items",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "wallet_topups",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "wallets",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "withdrawals",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "ledger_accounts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "ledger_transactions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "settlements",
                schema: "finance");

            migrationBuilder.DropColumn(
                name: "purpose",
                schema: "sales",
                table: "payments");

            migrationBuilder.AddForeignKey(
                name: "fk_payments_checkout_sessions_checkout_id",
                schema: "sales",
                table: "payments",
                column: "checkout_id",
                principalSchema: "sales",
                principalTable: "checkout_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
