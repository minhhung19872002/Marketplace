using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BannerTextInImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_text_in_image",
                schema: "promo",
                table: "banners",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "has_text_in_image",
                schema: "promo",
                table: "banners");
        }
    }
}
