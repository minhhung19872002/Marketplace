using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationPromoDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "promo_day",
                schema: "engage",
                table: "notifications",
                type: "date",
                nullable: true);

            // Existing promotions (broadcasts, wishlist news): the first of each person's Vietnam day takes the day; the voucher
            // reminder becomes a wallet notification, outside the promotion quota (L072)
            migrationBuilder.Sql("""
                UPDATE engage.notifications SET category = 'Wallet' WHERE category = 'Promotion' AND dedupe_key LIKE 'voucher-expiring:%';
                UPDATE engage.notifications n SET promo_day = f.day
                FROM (SELECT DISTINCT ON (user_id, (created_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date)
                             id, (created_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date AS day
                      FROM engage.notifications WHERE category = 'Promotion'
                      ORDER BY user_id, (created_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date, created_at, id) f
                WHERE n.id = f.id;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_notifications_promo_day",
                schema: "engage",
                table: "notifications",
                columns: new[] { "user_id", "promo_day" },
                unique: true,
                filter: "promo_day IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_notifications_promo_day",
                schema: "engage",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "promo_day",
                schema: "engage",
                table: "notifications");
        }
    }
}
