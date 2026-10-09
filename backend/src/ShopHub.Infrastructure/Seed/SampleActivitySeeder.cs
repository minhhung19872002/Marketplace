using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.SystemConfig;
using ShopHub.Infrastructure.Outbox;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Seed;

/// <summary>
/// What the sample people did besides ordering. Product views for the sample data (G3 A3): without them every shop showed "0 lượt xem · 0 người xem · chuyển đổi 0%"
/// next to real orders. Two parts, both before "now":
/// <list type="bullet">
/// <item>each sample order line: its buyer viewed the product once or twice in the two days before ordering (the funnel
/// xem → đặt then holds: a buyer never orders what they did not see);</item>
/// <item>anonymous visitors (session keys "seed-…") over the last 90 days, more on products that sell, spread over the
/// traffic sources.</item>
/// </list>
/// The products' view_count is not written here: <c>ICounterRecomputer.RecomputeAllAsync</c> counts it from these rows
/// afterwards. Then the notifications the order commands produced are marked read as their owner would have (G3 A4:
/// sample buyers had 150 unread): everything older than three days, and all but the five latest per person, read
/// a little after it arrived. Guard: skipped when any sample session view exists. Deterministic (setseed).
/// </summary>
public sealed class SampleActivitySeeder(ShopHubDbContext db, OutboxDispatcher outbox, ILogger<SampleActivitySeeder> logger)
{
    // Weighted traffic sources (cumulative thresholds over random()), names as stored by the ViewSource enum
    private const string Source = """
        case when r < 0.35 then 'Search' when r < 0.60 then 'Home' when r < 0.75 then 'Category' when r < 0.85 then 'Shop'
             when r < 0.90 then 'Campaign' when r < 0.97 then 'Direct' else 'External' end
        """;

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.ProductViews.AnyAsync(v => v.SessionKey != null && v.SessionKey.StartsWith("seed-"), ct)) return;
        if (!await db.Orders.AnyAsync(ct)) return;

        // The order events' notifications first (dated by their events), so the read marks below find them. Only order
        // events: search-sync messages must wait until the index is configured (L169)
        while ((await outbox.DispatchAsync(ct, [OutboxTypes.OrderEvent])).Processed > 0) db.ChangeTracker.Clear();
        db.ChangeTracker.Clear();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("select setseed(0.20261009)", ct);

        // 1. The buyer of every order line looked at the product before ordering it
        var buyerViews = await db.Database.ExecuteSqlRawAsync($"""
            insert into engage.product_views (user_id, product_id, viewed_at, source)
            select v.buyer_id, v.product_id, v.viewed_at, {Source}
            from (
                -- random() in the select list / join condition runs per row (a lateral without outer references would not)
                select o.buyer_id, i.product_id, o.created_at - make_interval(secs => 60 + random() * 172800) as viewed_at, random() as r
                from sales.order_items i
                join sales.orders o on o.id = i.order_id
                join generate_series(1, 2) g on g = 1 or random() < 0.4
            ) v
            """, ct);

        // 2. Anonymous traffic: 4–16 views a product plus ~6 per unit sold, at random moments of the last 90 days
        var visitorViews = await db.Database.ExecuteSqlRawAsync($"""
            insert into engage.product_views (session_key, product_id, viewed_at, source)
            select v.session_key, v.product_id, v.viewed_at, {Source}
            from (
                select 'seed-' || (random() * 6000)::int as session_key, n.id as product_id,
                       now() - make_interval(secs => random() * 7776000) as viewed_at, random() as r
                from (
                    select p.id, 4 + (random() * 12)::int + 6 * coalesce((
                        select sum(i.quantity) from sales.order_items i where i.product_id = p.id), 0)::int as views
                    from catalog.products p
                    where p.deleted_at is null and p.status = 'Active'
                ) n
                cross join lateral generate_series(1, n.views) g
            ) v
            """, ct);

        // 3. Notifications already read: older than 3 days, or beyond each person's 5 latest unread
        var read = await db.Database.ExecuteSqlRawAsync("""
            update engage.notifications n
            set is_read = true, read_at = least(now(), n.created_at + make_interval(secs => 300 + random() * 86400))
            from (
                select id, row_number() over (partition by user_id order by created_at desc, id) as rn
                from engage.notifications where not is_read
            ) x
            where n.id = x.id and (n.created_at < now() - interval '3 days' or x.rn > 5)
            """, ct);

        await tx.CommitAsync(ct);
        logger.LogInformation("SEED product views: {Buyer} by buyers before ordering, {Visitors} by anonymous visitors; {Read} notifications marked read",
            buyerViews, visitorViews, read);
    }
}
