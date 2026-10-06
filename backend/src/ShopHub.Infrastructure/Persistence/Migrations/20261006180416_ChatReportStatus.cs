using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChatReportStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "resolution",
                schema: "engage",
                table: "chat_reports",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resolved_at",
                schema: "engage",
                table: "chat_reports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "resolved_by",
                schema: "engage",
                table: "chat_reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "engage",
                table: "chat_reports",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Open");

            migrationBuilder.CreateIndex(
                name: "ix_chat_reports_status",
                schema: "engage",
                table: "chat_reports",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chat_reports_status",
                schema: "engage",
                table: "chat_reports");

            migrationBuilder.DropColumn(
                name: "resolution",
                schema: "engage",
                table: "chat_reports");

            migrationBuilder.DropColumn(
                name: "resolved_at",
                schema: "engage",
                table: "chat_reports");

            migrationBuilder.DropColumn(
                name: "resolved_by",
                schema: "engage",
                table: "chat_reports");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "engage",
                table: "chat_reports");
        }
    }
}
