using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class XtraPrograms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FeeType.Service became the two programmes; a rate an admin may have set for it was the Freeship Xtra one
            migrationBuilder.Sql("UPDATE finance.fee_rules SET fee_type = 'FreeshipXtra' WHERE fee_type = 'Service';");

            migrationBuilder.AddColumn<bool>(
                name: "xtra_only",
                schema: "promo",
                table: "vouchers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "freeship_xtra_since",
                schema: "shop",
                table: "shops",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "voucher_xtra_since",
                schema: "shop",
                table: "shops",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "freeship_xtra",
                schema: "sales",
                table: "orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "voucher_xtra",
                schema: "sales",
                table: "orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM finance.fee_rules WHERE fee_type = 'VoucherXtra'; UPDATE finance.fee_rules SET fee_type = 'Service' WHERE fee_type = 'FreeshipXtra';");

            migrationBuilder.DropColumn(
                name: "xtra_only",
                schema: "promo",
                table: "vouchers");

            migrationBuilder.DropColumn(
                name: "freeship_xtra_since",
                schema: "shop",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "voucher_xtra_since",
                schema: "shop",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "freeship_xtra",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "voucher_xtra",
                schema: "sales",
                table: "orders");
        }
    }
}
