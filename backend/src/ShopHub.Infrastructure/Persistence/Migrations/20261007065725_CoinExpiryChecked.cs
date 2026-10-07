using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoinExpiryChecked : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expiry_checked_at",
                schema: "promo",
                table: "coin_ledger",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_coin_ledger_expiry_todo",
                schema: "promo",
                table: "coin_ledger",
                columns: new[] { "user_id", "expires_at" },
                filter: "delta > 0 AND expires_at IS NOT NULL AND expiry_checked_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_coin_ledger_expiry_todo",
                schema: "promo",
                table: "coin_ledger");

            migrationBuilder.DropColumn(
                name: "expiry_checked_at",
                schema: "promo",
                table: "coin_ledger");
        }
    }
}
