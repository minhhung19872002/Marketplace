using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Storefront;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Common;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Marketing;

public enum MemberTier
{
    Silver,   // Bạc
    Gold,     // Vàng
    Diamond,  // Kim cương
}

public record MembershipDto(MemberTier Tier, string TierLabel, long Spend, int WindowDays, MemberTier? NextTier, long? NextTierSpend);

/// <summary>Hạng thành viên (spec VIII): spending on completed orders over MEMBER.WINDOW_DAYS, recomputed from the orders every time.</summary>
public sealed class Membership(IApplicationDbContext db, ISystemParameters parameters, IClock clock)
{
    public static string Label(MemberTier tier) => tier switch { MemberTier.Gold => "Vàng", MemberTier.Diamond => "Kim cương", _ => "Bạc" };

    public async Task<MembershipDto> OfAsync(Guid userId, CancellationToken ct)
    {
        var days = (int)await parameters.GetIntAsync(ParameterKeys.MemberWindowDays, ct);
        var gold = await parameters.GetIntAsync(ParameterKeys.MemberGoldMinSpend, ct);
        var diamond = await parameters.GetIntAsync(ParameterKeys.MemberDiamondMinSpend, ct);
        var since = clock.UtcNow.AddDays(-days);
        var spend = await db.Orders.Where(o => o.BuyerId == userId && o.Status == OrderStatus.Completed && o.CompletedAt >= since)
            .SumAsync(o => (long?)o.GrandTotal, ct) ?? 0;
        var tier = spend >= diamond ? MemberTier.Diamond : spend >= gold ? MemberTier.Gold : MemberTier.Silver;
        var (next, need) = tier switch
        {
            MemberTier.Silver => ((MemberTier?)MemberTier.Gold, (long?)gold),
            MemberTier.Gold => (MemberTier.Diamond, diamond),
            _ => (null, null),
        };
        return new MembershipDto(tier, Label(tier), spend, days, next, need);
    }
}

public record MembershipQuery : IRequest<MembershipDto>;

public sealed class MembershipHandler(Membership membership, ICurrentUser currentUser) : IRequestHandler<MembershipQuery, MembershipDto>
{
    public Task<MembershipDto> Handle(MembershipQuery request, CancellationToken ct) => membership.OfAsync(UserGuard.Require(currentUser), ct);
}

// ---------- daily check-in (điểm danh 7 ngày) ----------

public record CheckInDayDto(int Day, long Coins, bool Done, bool Today);

public record CheckInStatusDto(bool DoneToday, int Streak, IReadOnlyList<CheckInDayDto> Days);

internal static class CheckIns
{
    public static async Task<long[]> RewardsAsync(ISystemParameters parameters, CancellationToken ct)
    {
        var values = (await parameters.GetStringAsync(ParameterKeys.CheckInRewards, ct))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(long.Parse).ToArray();
        return values.Length == 7 ? values : [100, 100, 100, 200, 200, 200, 500];
    }

    /// <summary>Today's position in the cycle: yesterday's + 1 (after 7 a new cycle), or 1 when a day was missed.</summary>
    public static async Task<(CheckIn? Today, int NextDay)> StateAsync(IApplicationDbContext db, Guid userId, DateOnly today, CancellationToken ct)
    {
        var recent = await db.CheckIns.AsNoTracking().Where(c => c.UserId == userId && c.Day >= today.AddDays(-1)).ToListAsync(ct);
        var todayRow = recent.FirstOrDefault(c => c.Day == today);
        var yesterday = recent.FirstOrDefault(c => c.Day == today.AddDays(-1));
        return (todayRow, yesterday is null ? 1 : yesterday.StreakDay % 7 + 1);
    }
}

public record CheckInStatusQuery : IRequest<CheckInStatusDto>;

public sealed class CheckInStatusHandler(IApplicationDbContext db, ISystemParameters parameters, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<CheckInStatusQuery, CheckInStatusDto>
{
    public async Task<CheckInStatusDto> Handle(CheckInStatusQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var today = VietnamTime.Today(clock.UtcNow);
        var rewards = await CheckIns.RewardsAsync(parameters, ct);
        var (todayRow, next) = await CheckIns.StateAsync(db, userId, today, ct);
        var current = todayRow?.StreakDay ?? next;
        var doneUpTo = todayRow?.StreakDay ?? next - 1;
        return new CheckInStatusDto(todayRow is not null, doneUpTo,
            Enumerable.Range(1, 7).Select(d => new CheckInDayDto(d, rewards[d - 1], d <= doneUpTo, d == current)).ToList());
    }
}

public record CheckInCommand : IRequest<CheckInStatusDto>;

/// <summary>One check-in per Vietnam day (unique index); the xu of the streak day go to the ledger with an expiry.</summary>
public sealed class CheckInHandler(IApplicationDbContext db, ISystemParameters parameters, ISender sender, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<CheckInCommand, CheckInStatusDto>
{
    public async Task<CheckInStatusDto> Handle(CheckInCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var now = clock.UtcNow;
        var today = VietnamTime.Today(now);
        var (todayRow, next) = await CheckIns.StateAsync(db, userId, today, ct);
        if (todayRow is not null) throw new ConflictException("Hôm nay bạn đã điểm danh rồi.", "CHECKED_IN");
        var coins = (await CheckIns.RewardsAsync(parameters, ct))[next - 1];
        var expiry = await parameters.GetIntAsync(ParameterKeys.CoinExpiryDays, ct);
        var row = new CheckIn(userId, today, next, coins, now);
        db.CheckIns.Add(row);
        db.CoinLedger.Add(new CoinEntry(userId, coins, CoinReason.CheckIn, "checkin", row.Id, now.AddDays(expiry), $"Điểm danh ngày {next}/7", now));
        await db.SaveChangesAsync(ct);
        return await sender.Send(new CheckInStatusQuery(), ct);
    }
}

// ---------- voucher cash-back in xu, coin expiry ----------

/// <summary>
/// Hoàn xu từ voucher (spec VIII): the checkout's cash-back (a CoinCashback platform voucher) is shared between its
/// orders by goods value and paid when each order completes; a refund takes back the refunded share. Recomputed from
/// the orders and posted as the difference, so a redelivered event pays nothing twice.
/// </summary>
public sealed class CashbackService(IApplicationDbContext db, ISystemParameters parameters, IClock clock)
{
    public async Task SyncAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return;
        var cashback = await (from u in db.VoucherUsages.AsNoTracking()
                              join v in db.Vouchers.AsNoTracking() on u.VoucherId equals v.Id
                              where u.CheckoutId == order.CheckoutId && v.Type == VoucherType.CoinCashback && u.RevertedAt == null
                              select u.Amount).FirstOrDefaultAsync(ct);
        if (cashback <= 0) return;
        await db.LockAsync($"coins:{order.BuyerId}", ct);

        var siblings = await db.Orders.AsNoTracking().Where(o => o.CheckoutId == order.CheckoutId).OrderBy(o => o.Id)
            .Select(o => new { o.Id, Goods = o.Subtotal - o.ShopDiscount }).ToListAsync(ct);
        var shares = Money.Vnd(cashback).Allocate(siblings.Select(s => Math.Max(0, s.Goods)).ToList());
        var share = shares[siblings.FindIndex(s => s.Id == order.Id)].Value;
        long target = 0;
        if (order.Status == OrderStatus.Completed && share > 0)
        {
            var goods = siblings.Single(s => s.Id == order.Id).Goods;
            var refunded = await db.ReturnRequests.AsNoTracking().Where(r => r.OrderId == order.Id && r.Status == ReturnStatus.Refunded)
                .SumAsync(r => (long?)(r.RefundAmount ?? 0) + (r.RefundCoins ?? 0), ct) ?? 0;
            target = goods <= 0 ? 0 : share - (long)Math.Floor((decimal)share * Math.Min(refunded, goods) / goods);
        }
        var paid = await db.CoinLedger.Where(c => c.UserId == order.BuyerId && c.Reason == CoinReason.VoucherCashback && c.RefType == "order" && c.RefId == order.Id)
            .SumAsync(c => (long?)c.Delta, ct) ?? 0;
        var diff = target - paid;
        if (diff == 0) return;
        var now = clock.UtcNow;
        var expiry = await parameters.GetIntAsync(ParameterKeys.CoinExpiryDays, ct);
        db.CoinLedger.Add(new CoinEntry(order.BuyerId, diff, CoinReason.VoucherCashback, "order", order.Id, diff > 0 ? now.AddDays(expiry) : null,
            diff > 0 ? $"Hoàn xu từ voucher, đơn {order.Code}" : $"Thu hồi xu hoàn từ voucher, đơn {order.Code} đã được hoàn tiền", now));
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Xu hết hạn (spec VIII): spends are taken from the credits that expire first; whatever is left of an expired credit is
/// written off once (an "Expired" entry for the difference with what was already written off).
/// </summary>
public sealed class CoinExpiryService(IApplicationDbContext db, IClock clock, ILogger<CoinExpiryService> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var users = await db.CoinLedger.AsNoTracking().Where(c => c.Delta > 0 && c.ExpiresAt != null && c.ExpiresAt <= now)
            .Select(c => c.UserId).Distinct().Take(5_000).ToListAsync(ct);
        var touched = 0;
        foreach (var userId in users)
        {
            await using var tx = await db.BeginTransactionAsync(ct);
            await db.LockAsync($"coins:{userId}", ct);
            var entries = await db.CoinLedger.AsNoTracking().Where(c => c.UserId == userId).ToListAsync(ct);
            var toExpire = Expirable(entries, now) - -entries.Where(e => e.Reason == CoinReason.Expired).Sum(e => e.Delta);
            if (toExpire > 0)
            {
                db.CoinLedger.Add(new CoinEntry(userId, -toExpire, CoinReason.Expired, null, null, null, "Xu hết hạn", now));
                await db.SaveChangesAsync(ct);
                touched++;
            }
            await tx.CommitAsync(ct);
            db.ClearTracking();
        }
        if (touched > 0) logger.LogInformation("Expired xu of {Users} users", touched);
        return touched;
    }

    /// <summary>Unspent amount of the credits that expired (FIFO by expiry, never-expiring credits last).</summary>
    public static long Expirable(IReadOnlyList<CoinEntry> entries, DateTimeOffset now)
    {
        var spent = -entries.Where(e => e.Delta < 0 && e.Reason != CoinReason.Expired).Sum(e => e.Delta);
        long expired = 0;
        foreach (var credit in entries.Where(e => e.Delta > 0).OrderBy(e => e.ExpiresAt ?? DateTimeOffset.MaxValue).ThenBy(e => e.CreatedAt))
        {
            var used = Math.Min(spent, credit.Delta);
            spent -= used;
            if (credit.ExpiresAt is { } at && at <= now) expired += credit.Delta - used;
        }
        return expired;
    }
}
