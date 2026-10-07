using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.SystemConfig;
using ShopHub.Application.Common;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Checkout;

/// <summary>
/// Voucher checks that need the database (time, quota, per-user limit, audience). Minimum order and product scope
/// are applied by <see cref="PricingEngine"/>.
/// </summary>
public sealed class VoucherEvaluator(IApplicationDbContext db, Marketing.Membership membership, IClock clock)
{
    /// <summary>Why this user cannot use the voucher now, or null when they can.</summary>
    public async Task<string?> ProblemAsync(Voucher v, Guid userId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (!v.IsActive || now >= v.EndAt) return "Mã đã hết hạn.";
        if (now < v.StartAt) return $"Mã có hiệu lực từ {VietnamTime.Format(v.StartAt)}.";
        if (v.TotalQuota is { } quota && v.UsedCount >= quota) return "Mã đã hết lượt sử dụng.";
        var used = await db.VoucherUserCounters.Where(c => c.VoucherId == v.Id && c.UserId == userId).Select(c => c.UsedCount).FirstOrDefaultAsync(ct);
        if (used >= v.PerUserLimit) return "Bạn đã dùng hết lượt của mã này.";
        switch (v.Audience)
        {
            case VoucherAudience.NewBuyer when await db.Orders.AnyAsync(o => o.BuyerId == userId && o.Status != OrderStatus.Cancelled, ct):
                return "Mã chỉ dành cho khách hàng lần đầu mua sắm.";
            case VoucherAudience.ShopFollowers when !await db.ShopFollowers.AnyAsync(f => f.ShopId == v.ShopId && f.UserId == userId, ct):
                return "Mã chỉ dành cho người theo dõi shop.";
            case VoucherAudience.MemberGold when (await membership.OfAsync(userId, ct)).Tier < Marketing.MemberTier.Gold:
                return "Mã dành cho thành viên hạng Vàng trở lên.";
            case VoucherAudience.MemberDiamond when (await membership.OfAsync(userId, ct)).Tier < Marketing.MemberTier.Diamond:
                return "Mã dành cho thành viên hạng Kim cương.";
        }
        return null;
    }

    /// <summary>Find by code (case-insensitive), or null.</summary>
    public Task<Voucher?> ByCodeAsync(string code, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return db.Vouchers.AsNoTracking().FirstOrDefaultAsync(v => v.Code == normalized, ct);
    }
}

/// <summary>Consumes and gives back voucher uses with conditional writes (never "read, then write").</summary>
public sealed class VoucherLedger(IApplicationDbContext db, IOutbox outbox, IClock clock)
{
    /// <summary>Takes one use of the voucher for the user, or throws 409 when the quota / per-user limit is gone.</summary>
    public async Task ConsumeAsync(Voucher v, Guid userId, Guid checkoutId, Guid? orderId, long amount, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var taken = await db.ExecuteSqlAsync($"""
            UPDATE promo.vouchers SET used_count = used_count + 1
            WHERE id = {v.Id} AND is_active AND start_at <= {now} AND end_at > {now}
              AND (total_quota IS NULL OR used_count < total_quota)
            """, ct);
        if (taken == 0) throw new ConflictException($"Mã {v.Code} đã hết lượt hoặc hết hạn, vui lòng chọn mã khác.", "VOUCHER_EXHAUSTED");

        // Insert the per-user counter, or bump it while still under the limit — one atomic statement
        var counted = await db.ExecuteSqlAsync($"""
            INSERT INTO promo.voucher_user_counters (voucher_id, user_id, used_count) VALUES ({v.Id}, {userId}, 1)
            ON CONFLICT (voucher_id, user_id) DO UPDATE SET used_count = promo.voucher_user_counters.used_count + 1
            WHERE promo.voucher_user_counters.used_count < {v.PerUserLimit}
            """, ct);
        if (counted == 0) throw new ConflictException($"Bạn đã dùng hết lượt của mã {v.Code}.", "VOUCHER_USER_LIMIT");

        db.VoucherUsages.Add(new VoucherUsage(v.Id, userId, checkoutId, orderId, amount, now));
        // The last use of a shop voucher: the shop's products leave the "Có voucher" filter (same transaction)
        if (v.ShopId is { } shopId && v.TotalQuota is { } quota
            && await db.Vouchers.AnyAsync(x => x.Id == v.Id && x.UsedCount >= quota, ct))
            outbox.Enqueue(OutboxTypes.SearchSyncShop, new SearchSyncShopPayload(shopId));
    }

    /// <summary>Give the uses back (order cancelled / payment expired) — only while the voucher is still valid (spec 3.6).</summary>
    public async Task RevertAsync(IReadOnlyCollection<VoucherUsage> usages, CancellationToken ct)
    {
        var now = clock.UtcNow;
        foreach (var usage in usages.Where(u => u.RevertedAt is null))
        {
            usage.Revert(now);
            var stillValid = await db.Vouchers.AnyAsync(v => v.Id == usage.VoucherId && v.IsActive && v.EndAt > now, ct);
            if (!stillValid) continue;
            var full = await db.Vouchers.AsNoTracking().Where(v => v.Id == usage.VoucherId && v.TotalQuota != null && v.UsedCount >= v.TotalQuota)
                .Select(v => v.ShopId).FirstOrDefaultAsync(ct);
            await db.ExecuteSqlAsync($"UPDATE promo.vouchers SET used_count = used_count - 1 WHERE id = {usage.VoucherId} AND used_count > 0", ct);
            // A use back on a used-up shop voucher offers it again
            if (full is { } shopId) outbox.Enqueue(OutboxTypes.SearchSyncShop, new SearchSyncShopPayload(shopId));
            await db.ExecuteSqlAsync($"""
                UPDATE promo.voucher_user_counters SET used_count = used_count - 1
                WHERE voucher_id = {usage.VoucherId} AND user_id = {usage.UserId} AND used_count > 0
                """, ct);
        }
    }
}

/// <summary>ShopHub Xu balance = sum of the ledger. Spending holds a per-user advisory lock inside the caller's transaction.</summary>
public sealed class CoinWallet(IApplicationDbContext db)
{
    public async Task<long> BalanceAsync(Guid userId, CancellationToken ct) =>
        await db.CoinLedger.Where(c => c.UserId == userId).SumAsync(c => (long?)c.Delta, ct) ?? 0;

    /// <summary>Must run inside a transaction: locks the user's coins, then re-reads the balance.</summary>
    public async Task<long> LockedBalanceAsync(Guid userId, CancellationToken ct)
    {
        await db.LockAsync($"coins:{userId}", ct);
        return await BalanceAsync(userId, ct);
    }
}
