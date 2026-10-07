using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackgroundExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "output",
                schema: "sys",
                table: "background_tasks",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "output_name",
                schema: "sys",
                table: "background_tasks",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "output_type",
                schema: "sys",
                table: "background_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_background_tasks_owner",
                schema: "sys",
                table: "background_tasks",
                columns: new[] { "owner_user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_background_tasks_owner",
                schema: "sys",
                table: "background_tasks");

            migrationBuilder.DropColumn(
                name: "output",
                schema: "sys",
                table: "background_tasks");

            migrationBuilder.DropColumn(
                name: "output_name",
                schema: "sys",
                table: "background_tasks");

            migrationBuilder.DropColumn(
                name: "output_type",
                schema: "sys",
                table: "background_tasks");
        }
    }
}
