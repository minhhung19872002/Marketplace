using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerfIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_products_best_selling",
                schema: "catalog",
                table: "products",
                columns: new[] { "sold_count", "rating_avg", "published_at", "id" },
                descending: new[] { true, true, true, false },
                filter: "status = 'Active' AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_products_best_selling",
                schema: "catalog",
                table: "products");
        }
    }
}
