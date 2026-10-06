using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Finance;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Finance;

/// <summary>
/// Giải ngân (spec 3.8, 3.9): a completed order's earnings move from "chờ giải ngân" to "khả dụng" once the return
/// window is over (RETURN.WINDOW_DAYS after delivery), FINANCE.RELEASE_HOLD_DAYS have passed since completion and no
/// return request of the order is still open. Each run makes one settlement per shop with a frozen breakdown per
/// order; the unique index on the order is the guard against releasing twice.
/// </summary>
public sealed class SettlementService(
    IApplicationDbContext db,
    OrderLedger orders,
    ISystemParameters parameters,
    IClock clock,
    ILogger<SettlementService> logger)
{
    public record RunResult(int Shops, int Orders, long Net);

    public async Task<RunResult> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var returnWindow = await parameters.GetIntAsync(ParameterKeys.ReturnWindowDays, ct);
        var hold = await parameters.GetIntAsync(ParameterKeys.FinanceReleaseHoldDays, ct);
        var deliveredBefore = now.AddDays(-returnWindow);
        var completedBefore = now.AddDays(-hold);

        var due = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Completed && o.CompletedAt <= completedBefore && o.DeliveredAt <= deliveredBefore)
            .Where(o => !db.SettlementItems.Any(s => s.OrderId == o.Id))
            .Where(o => !db.ReturnRequests.Any(r => r.OrderId == o.Id && r.Status != ReturnStatus.Refunded && r.Status != ReturnStatus.Closed
                                                    && r.Status != ReturnStatus.Cancelled))
            .OrderBy(o => o.CompletedAt).Select(o => new { o.Id, o.ShopId, o.CompletedAt }).Take(5_000).ToListAsync(ct);

        int shops = 0, released = 0;
        long net = 0;
        foreach (var group in due.GroupBy(o => o.ShopId))
        {
            try
            {
                var (count, amount) = await ReleaseShopAsync(group.Key, group.Select(o => o.Id).ToList(), group.Min(o => o.CompletedAt!.Value), now, ct);
                shops += count > 0 ? 1 : 0;
                released += count;
                net += amount;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One shop's problem must not stop everybody else's money
                db.ClearTracking();
                logger.LogError(ex, "Release for shop {ShopId} failed", group.Key);
            }
        }
        if (released > 0) logger.LogInformation("Released {Orders} orders of {Shops} shops, net {Net}", released, shops, net);
        return new RunResult(shops, released, net);
    }

    private async Task<(int Count, long Net)> ReleaseShopAsync(Guid shopId, IReadOnlyList<Guid> orderIds, DateTimeOffset from, DateTimeOffset now,
        CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"finance:release:{shopId}", ct);
        var settlement = new Settlement(shopId, NewCode(now), from, now, now);
        foreach (var orderId in orderIds)
        {
            // Re-check under the lock: a parallel run may have released it, a return may have just been opened
            if (await db.SettlementItems.AnyAsync(s => s.OrderId == orderId, ct)) continue;
            if (await db.ReturnRequests.AnyAsync(r => r.OrderId == orderId && r.Status != ReturnStatus.Refunded && r.Status != ReturnStatus.Closed
                                                      && r.Status != ReturnStatus.Cancelled, ct)) continue;
            // Accrue first (in case the completion event is still in the outbox), then move it to "khả dụng"
            var breakdown = await orders.SyncInTransactionAsync(orderId, LedgerKinds.OrderSync, ct);
            if (breakdown is null) continue;
            var item = new SettlementItem(settlement.Id, orderId, shopId, breakdown.Goods, breakdown.ShopDiscount, breakdown.RefundsBorne,
                breakdown.FixedFee, breakdown.PaymentFee, breakdown.ServiceFee, breakdown.Net, now);
            settlement.Add(item);
            db.SettlementItems.Add(item);
            await orders.SyncInTransactionAsync(orderId, LedgerKinds.Release, ct);
        }
        if (settlement.Items.Count == 0)
        {
            await tx.RollbackAsync(ct);
            db.ClearTracking();
            return (0, 0);
        }
        db.Settlements.Add(settlement);

        // Tell the owner (in-app, Ví / tài chính)
        var owner = await db.Shops.Where(s => s.Id == shopId).Select(s => s.OwnerId).SingleAsync(ct);
        db.Notifications.Add(new Notification(owner, NotificationCategory.Wallet, "Đã giải ngân",
            $"{settlement.Items.Count} đơn hàng đã được giải ngân, cộng ₫{settlement.Net:N0} vào số dư khả dụng (kỳ {settlement.Code}).",
            "/seller/tai-chinh", "settlement", settlement.Id, now, $"settlement:{settlement.Id}"));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (settlement.Items.Count, settlement.Net);
    }

    private static string NewCode(DateTimeOffset now) =>
        $"GN{VietnamTime.ToLocal(now):yyMMdd}{RandomNumberGenerator.GetHexString(6)}";
}

public record LedgerMismatch(Guid AccountId, LedgerOwnerType OwnerType, Guid OwnerId, LedgerAccountType Type, long Cached, long FromEntries);

public record LedgerCheckResult(int Accounts, IReadOnlyList<LedgerMismatch> Mismatches, long TotalDebits, long TotalCredits, int UnbalancedTransactions);

/// <summary>
/// The ledger's own audit (spec 3.9, section 8 rule 2): every cached balance recomputed from the entries, Σ debits =
/// Σ credits overall and per transaction.
/// </summary>
public sealed class LedgerCheckService(IApplicationDbContext db, ILogger<LedgerCheckService> logger)
{
    public async Task<LedgerCheckResult> RunAsync(CancellationToken ct)
    {
        var sums = await db.LedgerEntries.AsNoTracking().GroupBy(e => e.AccountId).Select(g => new
        {
            AccountId = g.Key,
            Debits = g.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount),
            Credits = g.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount),
        }).ToDictionaryAsync(x => x.AccountId, ct);
        var accounts = await db.LedgerAccounts.AsNoTracking().ToListAsync(ct);
        var mismatches = new List<LedgerMismatch>();
        foreach (var a in accounts)
        {
            var s = sums.GetValueOrDefault(a.Id);
            var fromEntries = s is null ? 0 : LedgerAccount.IsDebitNormal(a.Type) ? s.Debits - s.Credits : s.Credits - s.Debits;
            if (fromEntries != a.Balance) mismatches.Add(new LedgerMismatch(a.Id, a.OwnerType, a.OwnerId, a.Type, a.Balance, fromEntries));
        }
        var unbalanced = await db.LedgerEntries.AsNoTracking().GroupBy(e => e.TransactionId)
            .Where(g => g.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount) != g.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount))
            .CountAsync(ct);
        var debits = sums.Values.Sum(s => s.Debits);
        var credits = sums.Values.Sum(s => s.Credits);
        if (mismatches.Count > 0 || unbalanced > 0 || debits != credits)
            logger.LogError("Ledger check failed: {Mismatches} cached balances differ, {Unbalanced} unbalanced transactions, debits {Debits} credits {Credits}",
                mismatches.Count, unbalanced, debits, credits);
        return new LedgerCheckResult(accounts.Count, mismatches, debits, credits, unbalanced);
    }
}
