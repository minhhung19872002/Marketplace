using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChatAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "broadcasts",
                schema: "engage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    body = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    segment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recipients = table.Column<int>(type: "integer", nullable: false),
                    skipped_today = table.Column<int>(type: "integer", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broadcasts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                schema: "engage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_message_preview = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    buyer_unread = table.Column<int>(type: "integer", nullable: false),
                    shop_unread = table.Column<int>(type: "integer", nullable: false),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: true),
                    blocked_by_buyer = table.Column<bool>(type: "boolean", nullable: false),
                    awaiting_reply_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                    table.CheckConstraint("ck_conversations_unread", "buyer_unread >= 0 AND shop_unread >= 0");
                    table.ForeignKey(
                        name: "fk_conversations_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shop",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversations_users_buyer_id",
                        column: x => x.buyer_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_tokens",
                schema: "engage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    token = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_device_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_prefs",
                schema: "engage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_prefs", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_prefs_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quick_replies",
                schema: "engage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shortcut = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    content = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quick_replies", x => x.id);
                    table.ForeignKey(
                        name: "fk_quick_replies_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shop",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shop_chat_settings",
                schema: "engage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    auto_reply_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    auto_reply_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    open_from = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    open_to = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_chat_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_shop_chat_settings_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shop",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_reports",
                schema: "engage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_reports_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "engage",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "engage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sender_role = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    flagged = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "engage",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_reports_conversation_id",
                schema: "engage",
                table: "chat_reports",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_buyer",
                schema: "engage",
                table: "conversations",
                columns: new[] { "buyer_id", "last_message_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_shop",
                schema: "engage",
                table: "conversations",
                columns: new[] { "shop_id", "last_message_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_conversations_pair",
                schema: "engage",
                table: "conversations",
                columns: new[] { "buyer_id", "shop_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_tokens_user_id",
                schema: "engage",
                table: "device_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_device_tokens_token",
                schema: "engage",
                table: "device_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation",
                schema: "engage",
                table: "messages",
                columns: new[] { "conversation_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_notification_prefs",
                schema: "engage",
                table: "notification_prefs",
                columns: new[] { "user_id", "category", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_quick_replies_shortcut",
                schema: "engage",
                table: "quick_replies",
                columns: new[] { "shop_id", "shortcut" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_shop_chat_settings_shop",
                schema: "engage",
                table: "shop_chat_settings",
                column: "shop_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "broadcasts",
                schema: "engage");

            migrationBuilder.DropTable(
                name: "chat_reports",
                schema: "engage");

            migrationBuilder.DropTable(
                name: "device_tokens",
                schema: "engage");

            migrationBuilder.DropTable(
                name: "messages",
                schema: "engage");

            migrationBuilder.DropTable(
                name: "notification_prefs",
                schema: "engage");

            migrationBuilder.DropTable(
                name: "quick_replies",
                schema: "engage");

            migrationBuilder.DropTable(
                name: "shop_chat_settings",
                schema: "engage");

            migrationBuilder.DropTable(
                name: "conversations",
                schema: "engage");
        }
    }
}
