using System;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopHub.Infrastructure.Logging;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "iam");

            migrationBuilder.EnsureSchema(
                name: "sys");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");

            // unaccent() is only STABLE; index expressions need an IMMUTABLE wrapper (used by trigram search indexes)
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.immutable_unaccent(text) RETURNS text
                LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                AS $$ SELECT public.unaccent('public.unaccent'::regdictionary, $1) $$;
                """);

            // Serilog warning/error sink table (columns must match LogTableColumns.Writers)
            migrationBuilder.Sql(LogTableColumns.CreateTableSql);

            migrationBuilder.CreateTable(
                name: "audit_logs",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    old_value = table.Column<string>(type: "jsonb", nullable: true),
                    new_value = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "sys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_parameters",
                schema: "sys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    data_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    group = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_parameters", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity",
                schema: "iam",
                table: "audit_logs",
                columns: new[] { "entity", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_occurred",
                schema: "iam",
                table: "audit_logs",
                columns: new[] { "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user",
                schema: "iam",
                table: "audit_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "sys",
                table: "outbox_messages",
                columns: new[] { "occurred_at", "id" },
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_system_parameters_key",
                schema: "sys",
                table: "system_parameters",
                column: "key",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS sys.logs;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.immutable_unaccent(text);");

            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "system_parameters",
                schema: "sys");
        }
    }
}
