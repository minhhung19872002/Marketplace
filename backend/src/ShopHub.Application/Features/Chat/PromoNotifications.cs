using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;

namespace ShopHub.Application.Features.Chat;

/// <summary>A promotion notification to send (broadcast, wishlist on sale / back in stock).</summary>
public record PromoNotice(Guid UserId, string Title, string Body, string? Link, string RefType, Guid? RefId, string DedupeKey);

/// <summary>
/// Promotion notifications (spec VII, VI.6): at most one a day (Vietnam day) per person and one per campaign — both kept by
/// unique indexes (<c>ux_notifications_promo_day</c>, <c>ux_notifications_dedupe</c>), so two sends at the same moment can
/// never both reach someone; whoever turned promotions off in the app is skipped. Insert-select with ON CONFLICT DO NOTHING:
/// the count returned is exactly who got one.
/// </summary>
public static class PromoNotifications
{
    public static async Task<int> SendAsync(IApplicationDbContext db, IReadOnlyList<PromoNotice> notices, DateTimeOffset now, CancellationToken ct)
    {
        var day = VietnamTime.Today(now);
        var sent = 0;
        foreach (var chunk in notices.DistinctBy(n => n.UserId).Chunk(1_000))
        {
            sent += await db.ExecuteSqlAsync($"""
                INSERT INTO engage.notifications (id, user_id, category, title, body, link, ref_type, ref_id, dedupe_key, is_read, created_at, promo_day)
                SELECT gen_random_uuid(), r.user_id, 'Promotion', r.title, r.body, r.link, r.ref_type, r.ref_id, r.dedupe_key, false, {now}, {day}
                FROM unnest({chunk.Select(n => n.UserId).ToArray()}, {chunk.Select(n => n.Title).ToArray()}, {chunk.Select(n => n.Body).ToArray()},
                            {chunk.Select(n => n.Link).ToArray()}, {chunk.Select(n => n.RefType).ToArray()}, {chunk.Select(n => n.RefId).ToArray()},
                            {chunk.Select(n => n.DedupeKey).ToArray()})
                     AS r(user_id, title, body, link, ref_type, ref_id, dedupe_key)
                WHERE NOT EXISTS (SELECT 1 FROM engage.notification_prefs p
                                  WHERE p.user_id = r.user_id AND p.category = 'Promotion' AND p.channel = 'InApp' AND NOT p.enabled)
                ON CONFLICT DO NOTHING
                """, ct);
        }
        return sent;
    }
}
