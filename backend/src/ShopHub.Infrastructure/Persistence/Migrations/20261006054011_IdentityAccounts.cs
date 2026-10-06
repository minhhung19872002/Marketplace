using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentityAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_divisions",
                schema: "iam",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    level = table.Column<short>(type: "smallint", nullable: false),
                    parent_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_divisions", x => x.code);
                    table.ForeignKey(
                        name: "fk_admin_divisions_admin_divisions_parent_code",
                        column: x => x.parent_code,
                        principalSchema: "iam",
                        principalTable: "admin_divisions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "otp_codes",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    target = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    purpose = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    verification_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otp_codes", x => x.id);
                    table.CheckConstraint("ck_otp_codes_attempts", "attempts >= 0");
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                schema: "iam",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    module = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "simulated_sms",
                schema: "sys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    to = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_simulated_sms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    phone = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    username = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    password_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    avatar_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    lock_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    phone_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    email_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_login_count = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    consented_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "iam",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_code });
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "iam",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "addresses",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receiver_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    province_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    district_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ward_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    street = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: true),
                    lng = table.Column<double>(type: "double precision", nullable: true),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_addresses", x => x.id);
                    table.ForeignKey(
                        name: "fk_addresses_admin_divisions_district_code",
                        column: x => x.district_code,
                        principalSchema: "iam",
                        principalTable: "admin_divisions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_addresses_admin_divisions_province_code",
                        column: x => x.province_code,
                        principalSchema: "iam",
                        principalTable: "admin_divisions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_addresses_admin_divisions_ward_code",
                        column: x => x.ward_code,
                        principalSchema: "iam",
                        principalTable: "admin_divisions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_addresses_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoke_reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "iam",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "iam",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_addresses_district_code",
                schema: "iam",
                table: "addresses",
                column: "district_code");

            migrationBuilder.CreateIndex(
                name: "ix_addresses_province_code",
                schema: "iam",
                table: "addresses",
                column: "province_code");

            migrationBuilder.CreateIndex(
                name: "ix_addresses_ward_code",
                schema: "iam",
                table: "addresses",
                column: "ward_code");

            migrationBuilder.CreateIndex(
                name: "ux_addresses_default",
                schema: "iam",
                table: "addresses",
                column: "user_id",
                unique: true,
                filter: "is_default AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_admin_divisions_parent",
                schema: "iam",
                table: "admin_divisions",
                column: "parent_code");

            migrationBuilder.CreateIndex(
                name: "ix_otp_codes_target_created",
                schema: "iam",
                table: "otp_codes",
                columns: new[] { "target", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_family",
                schema: "iam",
                table: "refresh_tokens",
                columns: new[] { "user_id", "family_id" });

            migrationBuilder.CreateIndex(
                name: "ux_refresh_tokens_hash",
                schema: "iam",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_roles_code",
                schema: "iam",
                table: "roles",
                column: "code",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_simulated_sms_to",
                schema: "sys",
                table: "simulated_sms",
                columns: new[] { "to", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                schema: "iam",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_created",
                schema: "iam",
                table: "users",
                columns: new[] { "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_users_email",
                schema: "iam",
                table: "users",
                column: "email",
                unique: true,
                filter: "deleted_at IS NULL AND email IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_users_phone",
                schema: "iam",
                table: "users",
                column: "phone",
                unique: true,
                filter: "deleted_at IS NULL AND phone IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_users_username",
                schema: "iam",
                table: "users",
                column: "username",
                unique: true,
                filter: "deleted_at IS NULL AND username IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "addresses",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "otp_codes",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "permissions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "simulated_sms",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "admin_divisions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "users",
                schema: "iam");
        }
    }
}
