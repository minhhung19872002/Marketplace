using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OtpTemplatePlatformName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F7 (L111): the OTP texts seeded before carried the platform name written in; they now take it from
            // SITE.PLATFORM_NAME through {{platform}}. Only rows still holding the original wording are changed —
            // a text an admin already edited is left alone.
            migrationBuilder.Sql("""
                UPDATE sys.message_templates SET body = '{{platform}}: ' || substring(body FROM length('ShopHub: ') + 1),
                       placeholders = 'platform,' || placeholders
                WHERE key = 'OTP' AND channel = 'Sms' AND body LIKE 'ShopHub: %' AND placeholders NOT LIKE '%platform%';
                UPDATE sys.message_templates SET subject = replace(subject, 'Mã xác thực ShopHub:', 'Mã xác thực {{platform}}:'),
                       placeholders = 'platform,' || placeholders
                WHERE key = 'OTP' AND channel = 'Email' AND subject LIKE 'Mã xác thực ShopHub:%' AND placeholders NOT LIKE '%platform%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE sys.message_templates SET body = 'ShopHub: ' || substring(body FROM length('{{platform}}: ') + 1),
                       placeholders = replace(placeholders, 'platform,', '')
                WHERE key = 'OTP' AND channel = 'Sms' AND body LIKE '{{platform}}: %';
                UPDATE sys.message_templates SET subject = replace(subject, 'Mã xác thực {{platform}}:', 'Mã xác thực ShopHub:'),
                       placeholders = replace(placeholders, 'platform,', '')
                WHERE key = 'OTP' AND channel = 'Email' AND subject LIKE 'Mã xác thực {{platform}}:%';
                """);
        }
    }
}
