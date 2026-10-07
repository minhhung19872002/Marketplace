using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoinRefundExpiryTodo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_coin_ledger_refund_todo",
                schema: "promo",
                table: "coin_ledger",
                column: "user_id",
                filter: "reason = 'CheckoutRefund' AND expiry_checked_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_coin_ledger_refund_todo",
                schema: "promo",
                table: "coin_ledger");
        }
    }
}
