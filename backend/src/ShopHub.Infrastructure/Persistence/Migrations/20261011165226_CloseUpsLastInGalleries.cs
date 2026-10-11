using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CloseUpsLastInGalleries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // G-VIS round 3 (L198): the sample seed turned close-ups (m001-zoom1, cut from a photo, edges lost) into the first
            // picture of about a third of the listings — the card showed half an object. In every gallery holding a seed
            // close-up the close-ups move to the end, the other pictures keep their order. Seller photos never match.
            migrationBuilder.Sql("""
                WITH galleries AS (
                    SELECT DISTINCT product_id FROM catalog.product_media WHERE url ~ '/product/seed/m[0-9]{3}-zoom[0-9]+_'),
                ranked AS (
                    SELECT m.id, row_number() OVER (PARTITION BY m.product_id
                               ORDER BY (m.url ~ '/product/seed/m[0-9]{3}-zoom[0-9]+_'), m.sort_order, m.id) - 1 AS new_order
                    FROM catalog.product_media m JOIN galleries g ON g.product_id = m.product_id)
                UPDATE catalog.product_media m SET sort_order = r.new_order
                FROM ranked r WHERE m.id = r.id AND m.sort_order <> r.new_order;

                -- The search index keeps each product's first picture: re-sync those galleries (200 products a message)
                INSERT INTO sys.outbox_messages (id, type, payload, occurred_at, attempts)
                SELECT gen_random_uuid(), 'search.sync.products', jsonb_build_object('productIds', jsonb_agg(product_id)), now(), 0
                FROM (SELECT product_id, (row_number() OVER (ORDER BY product_id) - 1) / 200 AS chunk
                      FROM (SELECT DISTINCT product_id FROM catalog.product_media WHERE url ~ '/product/seed/m[0-9]{3}-zoom[0-9]+_') d) g
                GROUP BY chunk;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The earlier order (close-ups first in some galleries) was a defect: nothing to restore
        }
    }
}
