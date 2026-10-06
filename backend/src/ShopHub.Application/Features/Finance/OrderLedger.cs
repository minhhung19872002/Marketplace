using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Finance;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Finance;

/// <summary>
/// Keeps the ledger of one order equal to what the order's state says (spec 3.9) — "recompute from the source, post the
/// difference", so it can run after every order event, any number of times, in any order:
/// <list type="bullet">
/// <item>money collected (online payment, COD handed to the carrier, Ví ShopHub) → platform cash / buyer wallet ⇒ escrow;</item>
/// <item>each completed refund → escrow ⇒ the payment source (gateway) or the buyer's wallet (COD, wallet);</item>
/// <item>order completed → escrow + platform subsidy ⇒ shop "chờ giải ngân" + fees + carrier payable;</item>
/// <item>order released (<see cref="SettlementItem"/> exists) → the shop's part sits in "khả dụng" instead.</item>
/// </list>
/// A COD / wallet refund waiting for the wallet is completed here (credited in the same posting).
/// </summary>
public sealed class OrderLedger(IApplicationDbContext db, Ledger ledger, FeeSchedule fees, IClock clock, ILogger<OrderLedger> logger)
{
    /// <summary>Runs in its own transaction holding the order's ledger lock.</summary>
    public async Task SyncAsync(Guid orderId, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        await SyncInTransactionAsync(orderId, LedgerKinds.OrderSync, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Inside the caller's transaction (the caller saves).</summary>
    public async Task<SettlementBreakdown?> SyncInTransactionAsync(Guid orderId, string kind, CancellationToken ct)
    {
        await db.LockAsync($"finance:order:{orderId}", ct);
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).ThenInclude(i => i.Discounts).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return null;

        var target = new Dictionary<AccountKey, long>();
        void Add(AccountKey key, long netCredit) => target[key] = target.GetValueOrDefault(key) + netCredit;
        var escrow = AccountKey.Platform(LedgerAccountType.PlatformEscrow);
        var wallet = AccountKey.Wallet(order.BuyerId);
        var cash = AccountKey.Platform(LedgerAccountType.PlatformCash);

        var collected = order.PaymentStatus is OrderPaymentStatus.Paid or OrderPaymentStatus.Refunded && order.GrandTotal > 0;
        if (collected)
        {
            // Debit the source (cash grows / wallet shrinks), credit escrow
            Add(order.PaymentMethod == PaymentMethod.Wallet ? wallet : cash, -order.GrandTotal);
            Add(escrow, order.GrandTotal);
        }

        var now = clock.UtcNow;
        foreach (var refund in await db.Refunds.Where(r => r.OrderId == order.Id).ToListAsync(ct))
        {
            if (refund.Status == RefundStatus.Pending && refund.Destination == RefundDestination.Wallet)
                refund.Complete(true, "VI-SHOPHUB", now);
            if (refund.Status != RefundStatus.Succeeded) continue;
            Add(escrow, -refund.Amount);
            // Wallet credit grows the wallet; a gateway refund is cash leaving the platform (credit cash)
            Add(refund.Destination == RefundDestination.Wallet ? wallet : cash, refund.Amount);
        }

        SettlementBreakdown? breakdown = null;
        if (order.Status == OrderStatus.Completed && collected)
        {
            breakdown = await BreakdownAsync(order, ct);
            var released = await db.SettlementItems.AnyAsync(s => s.OrderId == order.Id, ct)
                           || db.SettlementItems.Local.Any(s => s.OrderId == order.Id);
            Add(escrow, -breakdown.MoneyKept);
            Add(AccountKey.Platform(LedgerAccountType.PlatformSubsidy), -breakdown.Subsidy);
            Add(AccountKey.Shop(order.ShopId, released ? LedgerAccountType.ShopAvailable : LedgerAccountType.ShopPending), breakdown.Net);
            Add(AccountKey.Platform(LedgerAccountType.FeeFixed), breakdown.FixedFee);
            Add(AccountKey.Platform(LedgerAccountType.FeePayment), breakdown.PaymentFee);
            Add(AccountKey.Platform(LedgerAccountType.FeeService), breakdown.ServiceFee);
            Add(AccountKey.Platform(LedgerAccountType.CarrierPayable), breakdown.ShippingFee);
        }
        else if (order.Status == OrderStatus.Completed)
            logger.LogWarning("Completed order {OrderCode} has no collected payment: not accrued", order.Code);

        var posted = await ledger.SyncAsync(kind, LedgerRefs.Order, order.Id, Describe(kind, order.Code), target, ct);
        if (posted is not null) logger.LogInformation("Ledger {Kind} for order {OrderCode}: {Lines} lines", kind, order.Code, posted.Entries.Count);
        return breakdown;
    }

    private static string Describe(string kind, string code) => kind switch
    {
        LedgerKinds.Release => $"Giải ngân đơn {code}",
        LedgerKinds.WalletPay => $"Thanh toán đơn {code} bằng Ví ShopHub",
        _ => $"Cập nhật tiền đơn {code}",
    };

    /// <summary>The shop's earnings for a completed order as it stands now (also used for the "chờ giải ngân" list).</summary>
    public async Task<SettlementBreakdown> BreakdownAsync(Order order, CancellationToken ct)
    {
        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
        var categories = await db.Products.AsNoTracking().IgnoreQueryFilters().Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.CategoryId, ct);
        var lines = new List<SettlementLine>();
        foreach (var item in order.Items.OrderBy(i => i.Id))
        {
            var category = categories.GetValueOrDefault(item.ProductId);
            lines.Add(new SettlementLine(item.Id, item.LineTotal, item.Quantity,
                // The shop bears its voucher and its combos
                item.Discounts.Where(d => d.Source is DiscountSource.Shop or DiscountSource.Combo).Sum(d => d.Amount),
                item.Discounts.Where(d => d.Source == DiscountSource.Platform).Sum(d => d.Amount),
                await fees.RateAsync(FeeType.Fixed, category, order.CreatedAt, ct),
                // Service fee only for the programmes the shop was in when the order was placed
                (order.FreeshipXtra ? await fees.RateAsync(FeeType.FreeshipXtra, category, order.CreatedAt, ct) : 0)
                + (order.VoucherXtra ? await fees.RateAsync(FeeType.VoucherXtra, category, order.CreatedAt, ct) : 0)));
        }

        var returns = await db.ReturnRequests.AsNoTracking().Include(r => r.Items)
            .Where(r => r.OrderId == order.Id && r.Status == ReturnStatus.Refunded).ToListAsync(ct);
        var input = new SettlementInput(order.GrandTotal, order.ShippingFee, order.ShippingDiscount, order.PlatformDiscount, order.CoinUsed,
            await fees.RateAsync(FeeType.Payment, null, order.CreatedAt, ct), lines,
            returns.Select(r => new SettlementReturn(r.Id, r.RefundedAt ?? r.UpdatedAt, r.RequestedAmount, r.RequestedCoins, r.RefundAmount ?? 0,
                r.RefundCoins ?? 0, r.Items.Select(i => new SettlementReturnItem(i.OrderItemId, i.Quantity, i.RefundAmount, i.RefundCoins)).ToList())).ToList());
        return SettlementCalculator.Compute(input);
    }
}
