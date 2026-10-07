using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeletedAccountLeftovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // L074: accounts deleted before the fix kept their Google links, push devices and bank account numbers
            migrationBuilder.Sql("""
                DELETE FROM iam.user_identities i USING iam.users u WHERE u.id = i.user_id AND u.status = 'Deleted';
                DELETE FROM engage.device_tokens d USING iam.users u WHERE u.id = d.user_id AND u.status = 'Deleted';
                UPDATE finance.bank_accounts b SET account_no_encrypted = '', account_name = 'Người dùng đã xoá', deleted_at = COALESCE(b.deleted_at, u.deleted_at, now())
                FROM iam.users u WHERE u.id = b.user_id AND u.status = 'Deleted' AND b.account_no_encrypted <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
