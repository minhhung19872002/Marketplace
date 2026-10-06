using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Finance;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Account;

// Spec I.2: "yêu cầu xoá tài khoản — chặn khi còn đơn chưa hoàn tất / còn số dư". Each guard explains what to finish first.

/// <summary>Orders still moving (not completed, cancelled or returned) and return requests still open.</summary>
public sealed class OpenOrdersDeletionGuard(IApplicationDbContext db) : IAccountDeletionGuard
{
    private static readonly OrderStatus[] Finished = [OrderStatus.Completed, OrderStatus.Cancelled, OrderStatus.Returned];
    private static readonly ReturnStatus[] Settled = [ReturnStatus.Refunded, ReturnStatus.Closed, ReturnStatus.Cancelled];

    public async Task<string?> GetBlockingReasonAsync(Guid userId, CancellationToken ct)
    {
        var open = await db.Orders.CountAsync(o => o.BuyerId == userId && !Finished.Contains(o.Status), ct);
        if (open > 0) return $"Bạn còn {open} đơn hàng chưa hoàn tất. Vui lòng chờ đơn hoàn thành hoặc huỷ đơn trước khi xoá tài khoản.";
        var returns = await db.ReturnRequests.CountAsync(r => r.BuyerId == userId && !Settled.Contains(r.Status), ct);
        return returns > 0 ? $"Bạn còn {returns} yêu cầu trả hàng / hoàn tiền đang xử lý." : null;
    }
}

/// <summary>Money in Ví ShopHub (or on its way to the bank) would be lost with the account.</summary>
public sealed class WalletBalanceDeletionGuard(IApplicationDbContext db) : IAccountDeletionGuard
{
    public async Task<string?> GetBlockingReasonAsync(Guid userId, CancellationToken ct)
    {
        var balance = await db.LedgerAccounts.AsNoTracking()
            .Where(a => a.OwnerType == LedgerOwnerType.Buyer && a.OwnerId == userId && a.Type == LedgerAccountType.BuyerWallet)
            .Select(a => (long?)a.Balance).FirstOrDefaultAsync(ct) ?? 0;
        if (balance > 0) return $"Ví ShopHub còn ₫{balance:N0}. Vui lòng rút hết số dư về tài khoản ngân hàng trước khi xoá tài khoản.";
        var withdrawing = await db.Withdrawals.AnyAsync(w => w.OwnerType == LedgerOwnerType.Buyer && w.OwnerId == userId
                                                             && (w.Status == WithdrawalStatus.Pending || w.Status == WithdrawalStatus.Processing), ct);
        return withdrawing ? "Bạn có yêu cầu rút tiền đang xử lý. Vui lòng chờ hoàn tất trước khi xoá tài khoản." : null;
    }
}

/// <summary>A shop owner cannot disappear under their shop: the shop has buyers, orders and money of its own.</summary>
public sealed class ShopOwnerDeletionGuard(IApplicationDbContext db) : IAccountDeletionGuard
{
    public async Task<string?> GetBlockingReasonAsync(Guid userId, CancellationToken ct)
    {
        var shop = await db.Shops.AsNoTracking().Where(s => s.OwnerId == userId && s.Status != ShopStatus.Rejected)
            .Select(s => s.Name).FirstOrDefaultAsync(ct);
        return shop is null ? null : $"Bạn đang là chủ shop \"{shop}\". Vui lòng liên hệ sàn để đóng shop trước khi xoá tài khoản.";
    }
}
