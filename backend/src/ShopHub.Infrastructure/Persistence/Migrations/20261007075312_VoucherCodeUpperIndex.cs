using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VoucherCodeUpperIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_voucher_code",
                schema: "promo",
                table: "vouchers");

            // Spec 4.10: unique on upper(code), so "sale12" and "SALE12" can never both exist (L080)
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_voucher_code ON promo.vouchers (upper(code)) WHERE deleted_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX promo.ux_voucher_code;");

            migrationBuilder.CreateIndex(
                name: "ux_voucher_code",
                schema: "promo",
                table: "vouchers",
                column: "code",
                unique: true,
                filter: "deleted_at IS NULL");
        }
    }
}
