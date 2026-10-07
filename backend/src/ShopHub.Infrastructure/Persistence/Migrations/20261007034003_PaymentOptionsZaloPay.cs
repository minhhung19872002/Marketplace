using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentOptionsZaloPay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "option",
                schema: "sales",
                table: "payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Default");

            migrationBuilder.AddColumn<string>(
                name: "payment_option",
                schema: "sales",
                table: "checkout_sessions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Default");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "option",
                schema: "sales",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "payment_option",
                schema: "sales",
                table: "checkout_sessions");
        }
    }
}
