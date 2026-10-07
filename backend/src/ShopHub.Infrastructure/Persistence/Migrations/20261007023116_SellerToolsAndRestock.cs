using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SellerToolsAndRestock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sold_out_seen_at",
                schema: "engage",
                table: "wishlists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "low_stock_threshold",
                schema: "shop",
                table: "shops",
                type: "integer",
                nullable: true);

            // Reminders now also tell "có hàng lại": hourly instead of daily, unless an admin already changed the schedule
            migrationBuilder.Sql("UPDATE sys.system_parameters SET value = '0 * * * *' WHERE key = 'JOB.REMINDERS_CRON' AND value = '0 1 * * *';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sold_out_seen_at",
                schema: "engage",
                table: "wishlists");

            migrationBuilder.DropColumn(
                name: "low_stock_threshold",
                schema: "shop",
                table: "shops");
        }
    }
}
