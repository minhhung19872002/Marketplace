using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EndShopCoinCashbackVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // L132: xu are the platform's to give — a shop "hoàn xu" voucher (it could only be made through the API) ends now
            migrationBuilder.Sql("""
                UPDATE promo.vouchers SET end_at = now()
                WHERE owner = 'Shop' AND type = 'CoinCashback' AND start_at < now() AND end_at > now() AND deleted_at IS NULL;
                -- not started yet: an end before the start breaks ck_vouchers_period, so it is withdrawn instead
                UPDATE promo.vouchers SET deleted_at = now()
                WHERE owner = 'Shop' AND type = 'CoinCashback' AND start_at >= now() AND deleted_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
