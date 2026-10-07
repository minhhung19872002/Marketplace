using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HomeShortcutsDeals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // E3 (L105): databases seeded before get the shortcuts Mã Giảm Giá / Freeship / Deal Sốc in front (a fresh database gets
            // them from the seeder — nothing to do while there is no shortcut yet), and the "Freeship mọi đơn" banner opens the
            // freeship filter instead of "Bán chạy". Shortcuts an admin already pointed at those pages are not added twice.
            migrationBuilder.Sql("""
                WITH wanted(title, icon, link, ord) AS (
                    VALUES ('Mã Giảm Giá', '🎟️', '/tai-khoan/voucher', 0), ('Freeship', '🚚', '/tim-kiem?freeship=true', 1), ('Deal Sốc', '⚡', '/flash-sale', 2)),
                missing AS (
                    SELECT w.* FROM wanted w
                    WHERE EXISTS (SELECT 1 FROM promo.banners WHERE position = 'Shortcut')
                      AND NOT EXISTS (SELECT 1 FROM promo.banners b WHERE b.position = 'Shortcut' AND b.link = w.link)),
                shifted AS (
                    UPDATE promo.banners SET sort_order = sort_order + (SELECT count(*) FROM missing)
                    WHERE position = 'Shortcut' AND (SELECT count(*) FROM missing) > 0
                    RETURNING id)
                INSERT INTO promo.banners (id, position, title, image_url, link, category_id, start_at, end_at, sort_order, is_active, created_at)
                SELECT gen_random_uuid(), 'Shortcut', m.title, m.icon, m.link, NULL, now() - interval '1 minute', now() + interval '1 year',
                       row_number() OVER (ORDER BY m.ord) - 1, true, now()
                FROM missing m;

                UPDATE promo.banners SET link = '/tim-kiem?freeship=true'
                WHERE position = 'HomeSide' AND title = 'Freeship mọi đơn' AND link = '/tim-kiem?sort=BestSelling';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE promo.banners SET link = '/tim-kiem?sort=BestSelling'
                WHERE position = 'HomeSide' AND title = 'Freeship mọi đơn' AND link = '/tim-kiem?freeship=true';
                """);
        }
    }
}
