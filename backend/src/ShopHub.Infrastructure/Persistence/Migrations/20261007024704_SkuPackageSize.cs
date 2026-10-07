using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SkuPackageSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "height_mm",
                schema: "catalog",
                table: "skus",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "length_mm",
                schema: "catalog",
                table: "skus",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "width_mm",
                schema: "catalog",
                table: "skus",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "height_mm",
                schema: "catalog",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "length_mm",
                schema: "catalog",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "width_mm",
                schema: "catalog",
                table: "skus");
        }
    }
}
