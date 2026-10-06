using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Seller;

public enum PenaltyLevel
{
    None,
    Restricted,    // hạn chế hiển thị: out of "Gợi ý hôm nay", search ranks it last
    CampaignBan,   // + cannot register platform Flash Sale / campaigns
    Locked,        // + the shop is locked
}

public record PenaltyStatus(int Points, PenaltyLevel Level, string Consequence, int RestrictAt, int CampaignBanAt, int LockAt);

/// <summary>
/// Điểm phạt (spec VI.3): the shop's total is the sum of its penalties that still count (not expired, not revoked),
/// recomputed from shop_penalties on every change — never incremented. Thresholds (SHOP.PENALTY_*_POINTS) decide the
/// consequence; reaching the lock threshold locks the shop.
/// </summary>
public sealed class ShopPenaltyService(IApplicationDbContext db, ISystemParameters parameters, IOutbox outbox, IClock clock, ILogger<ShopPenaltyService> logger)
{
    public async Task<PenaltyStatus> StatusAsync(int points, CancellationToken ct)
    {
        var restrict = await parameters.GetIntAsync(ParameterKeys.ShopPenaltyRestrictPoints, ct);
        var ban = await parameters.GetIntAsync(ParameterKeys.ShopPenaltyCampaignBanPoints, ct);
        var lockAt = await parameters.GetIntAsync(ParameterKeys.ShopPenaltyLockPoints, ct);
        var level = points >= lockAt ? PenaltyLevel.Locked : points >= ban ? PenaltyLevel.CampaignBan : points >= restrict ? PenaltyLevel.Restricted : PenaltyLevel.None;
        var text = level switch
        {
            PenaltyLevel.Locked => "Shop bị khoá do điểm phạt vượt ngưỡng.",
            PenaltyLevel.CampaignBan => "Hạn chế hiển thị và không được đăng ký Flash Sale / chiến dịch của sàn.",
            PenaltyLevel.Restricted => "Sản phẩm bị hạn chế hiển thị (không xuất hiện ở Gợi ý hôm nay, xếp sau trong tìm kiếm).",
            _ => "Không bị hạn chế.",
        };
        return new PenaltyStatus(points, level, text, (int)restrict, (int)ban, (int)lockAt);
    }

    public async Task<PenaltyStatus> OfShopAsync(Guid shopId, CancellationToken ct) =>
        await StatusAsync(await db.Shops.Where(s => s.Id == shopId).Select(s => s.PenaltyPoints).SingleAsync(ct), ct);

    /// <summary>Recompute one shop's points and apply the lock threshold. Call after any penalty change (same unit of work).</summary>
    public async Task<PenaltyStatus> RecomputeAsync(Guid shopId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        var points = await db.ShopPenalties.Where(p => p.ShopId == shopId && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now))
            .SumAsync(p => (int?)p.Points, ct) ?? 0;
        var shop = await db.Shops.SingleAsync(s => s.Id == shopId, ct);
        var before = await StatusAsync(shop.PenaltyPoints, ct);
        shop.SetPenaltyPoints(points);
        var status = await StatusAsync(points, ct);
        // Search ranks restricted shops last: re-index the shop's products when it crosses that threshold
        if ((before.Level >= PenaltyLevel.Restricted) != (status.Level >= PenaltyLevel.Restricted))
            outbox.Enqueue(OutboxTypes.SearchSyncShop, new SearchSyncShopPayload(shopId));
        if (status.Level == PenaltyLevel.Locked && shop.Status is ShopStatus.Active or ShopStatus.Vacation)
        {
            shop.Lock($"Điểm phạt đạt {points} (ngưỡng khoá {status.LockAt}).");
            logger.LogWarning("Shop {ShopId} locked by penalty points ({Points})", shopId, points);
        }
        await db.SaveChangesAsync(ct);
        return status;
    }

    /// <summary>Daily: shops whose penalties expired since yesterday get their total lowered.</summary>
    public async Task<int> RecomputeExpiredAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var shops = await db.ShopPenalties.Where(p => p.ExpiresAt != null && p.ExpiresAt <= now && p.ExpiresAt > now.AddDays(-2))
            .Select(p => p.ShopId).Distinct().ToListAsync(ct);
        foreach (var id in shops) await RecomputeAsync(id, ct);
        return shops.Count;
    }

    /// <summary>Shops at or above a level (for the storefront / campaign filters).</summary>
    public async Task<IQueryable<Guid>> ShopsAtLeastAsync(PenaltyLevel level, CancellationToken ct)
    {
        var threshold = level switch
        {
            PenaltyLevel.Restricted => await parameters.GetIntAsync(ParameterKeys.ShopPenaltyRestrictPoints, ct),
            PenaltyLevel.CampaignBan => await parameters.GetIntAsync(ParameterKeys.ShopPenaltyCampaignBanPoints, ct),
            _ => await parameters.GetIntAsync(ParameterKeys.ShopPenaltyLockPoints, ct),
        };
        return db.Shops.AsNoTracking().Where(s => s.PenaltyPoints >= threshold).Select(s => s.Id);
    }
}
