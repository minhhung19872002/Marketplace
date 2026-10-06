using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Common;
using ShopHub.Domain.Finance;

namespace ShopHub.Application.Features.Finance;

public record BankTransferRequest(Guid WithdrawalId, string BankCode, string AccountNo, string AccountName, long Amount, string Description);

public record BankTransferResult(bool Ok, string? Reference, string? Error);

/// <summary>Pays money out to a bank account (spec 3.9). Simulated until a real bank / payout API is configured.</summary>
public interface IBankPayout
{
    Task<BankTransferResult> TransferAsync(BankTransferRequest request, CancellationToken ct);
}

public record WithdrawalDto(Guid Id, LedgerOwnerType OwnerType, Guid OwnerId, string? OwnerName, long Amount, WithdrawalStatus Status, string StatusLabel,
    string BankCode, string AccountLast4, string AccountName, string? RejectReason, string? BankRef, DateTimeOffset CreatedAt, DateTimeOffset? ProcessedAt);

/// <summary>
/// Rút tiền (spec 3.9, IV, 6.2): to a verified account only, at least FINANCE.WITHDRAW_MIN, at most
/// FINANCE.WITHDRAW_PER_WEEK requests per 7 days, never more than the available balance — the money leaves the
/// balance when the request is made (conditional UPDATE + CHECK), so parallel requests cannot overdraw it. Small
/// amounts (≤ FINANCE.WITHDRAW_AUTO_APPROVE_MAX) are paid out at once, the rest waits for an admin.
/// </summary>
public sealed class WithdrawalService(
    IApplicationDbContext db,
    Ledger ledger,
    IBankPayout bank,
    IDataEncryptor encryptor,
    ISystemParameters parameters,
    IClock clock,
    ILogger<WithdrawalService> logger)
{
    public static AccountKey SourceOf(LedgerOwnerType ownerType, Guid ownerId) => ownerType == LedgerOwnerType.Shop
        ? AccountKey.Shop(ownerId, LedgerAccountType.ShopAvailable)
        : AccountKey.Wallet(ownerId);

    public record Destination(Guid BankAccountId, string BankCode, string AccountNoEncrypted, string Last4, string AccountName);

    public async Task<Withdrawal> RequestAsync(LedgerOwnerType ownerType, Guid ownerId, Destination to, long amount, Guid requestedBy, CancellationToken ct)
    {
        var min = await parameters.GetIntAsync(ParameterKeys.FinanceWithdrawMin, ct);
        if (amount < min) throw new BusinessRuleException($"Số tiền rút tối thiểu là ₫{min:N0}.");
        var perWeek = await parameters.GetIntAsync(ParameterKeys.FinanceWithdrawPerWeek, ct);
        var autoMax = await parameters.GetIntAsync(ParameterKeys.FinanceWithdrawAutoApproveMax, ct);
        var now = clock.UtcNow;

        Withdrawal withdrawal;
        await using (var tx = await db.BeginTransactionAsync(ct))
        {
            // Serialises the weekly count; the balance itself is protected by the conditional UPDATE in the ledger
            await db.LockAsync($"finance:withdraw:{ownerType}:{ownerId}", ct);
            var since = now.AddDays(-7);
            var recent = await db.Withdrawals.CountAsync(w => w.OwnerType == ownerType && w.OwnerId == ownerId && w.CreatedAt > since
                                                              && w.Status != WithdrawalStatus.Rejected, ct);
            if (recent >= perWeek) throw new ConflictException($"Mỗi 7 ngày chỉ được rút tối đa {perWeek} lần.", "WITHDRAW_LIMIT");

            withdrawal = new Withdrawal(ownerType, ownerId, to.BankAccountId, to.BankCode, to.Last4, to.AccountName, amount, requestedBy, now);
            db.Withdrawals.Add(withdrawal);
            try
            {
                await ledger.PostAsync(LedgerKinds.WithdrawRequest, LedgerRefs.Withdrawal, withdrawal.Id, $"Yêu cầu rút ₫{amount:N0}",
                [
                    new LedgerLine(SourceOf(ownerType, ownerId), LedgerDirection.Debit, amount),
                    new LedgerLine(AccountKey.Platform(LedgerAccountType.WithdrawalPayable), LedgerDirection.Credit, amount),
                ], $"withdraw-request:{withdrawal.Id}", ct);
            }
            catch (ConflictException e) when (e.Code == "INSUFFICIENT_BALANCE")
            {
                throw new ConflictException("Số dư khả dụng không đủ để rút số tiền này.", "INSUFFICIENT_BALANCE");
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        logger.LogInformation("Withdrawal {WithdrawalId} requested by {OwnerType} {OwnerId}: {Amount}", withdrawal.Id, ownerType, ownerId, amount);

        if (autoMax > 0 && amount <= autoMax) await ApproveAsync(withdrawal.Id, null, to.AccountNoEncrypted, ct);
        return await db.Withdrawals.AsNoTracking().SingleAsync(w => w.Id == withdrawal.Id, ct);
    }

    /// <summary>Admin approval (or automatic): pay out through the bank; a refused transfer gives the money back.</summary>
    public async Task ApproveAsync(Guid withdrawalId, Guid? adminId, string? accountNoEncrypted, CancellationToken ct)
    {
        var now = clock.UtcNow;
        Withdrawal w;
        await using (var tx = await db.BeginTransactionAsync(ct))
        {
            await db.LockAsync($"finance:withdrawal:{withdrawalId}", ct);
            w = await db.Withdrawals.FirstOrDefaultAsync(x => x.Id == withdrawalId, ct) ?? throw new NotFoundException("Không tìm thấy yêu cầu rút tiền.");
            w.StartProcessing(adminId);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        accountNoEncrypted ??= await AccountNoAsync(w, ct);
        var result = accountNoEncrypted is null
            ? new BankTransferResult(false, null, "Tài khoản nhận không còn tồn tại.")
            : await bank.TransferAsync(new BankTransferRequest(w.Id, w.BankCode, encryptor.Decrypt(accountNoEncrypted), w.AccountName, w.Amount,
                $"ShopHub rut tien {w.Id.ToString("N")[..8]}"), ct);

        await using (var tx = await db.BeginTransactionAsync(ct))
        {
            await db.LockAsync($"finance:withdrawal:{withdrawalId}", ct);
            w = await db.Withdrawals.SingleAsync(x => x.Id == withdrawalId, ct);
            if (result.Ok)
            {
                w.Complete(result.Reference!, now);
                await ledger.PostAsync(LedgerKinds.WithdrawPaid, LedgerRefs.Withdrawal, w.Id, $"Đã chuyển ₫{w.Amount:N0} về {w.BankCode} ***{w.AccountLast4}",
                [
                    new LedgerLine(AccountKey.Platform(LedgerAccountType.WithdrawalPayable), LedgerDirection.Debit, w.Amount),
                    new LedgerLine(AccountKey.Platform(LedgerAccountType.PlatformCash), LedgerDirection.Credit, w.Amount),
                ], $"withdraw-paid:{w.Id}", ct);
            }
            else
                await ReverseAsync(w, adminId, $"Ngân hàng không nhận lệnh chuyển: {result.Error}", now, ct);
            Notify(w, now);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        logger.LogInformation("Withdrawal {WithdrawalId}: {Status}", w.Id, w.Status);
    }

    public async Task RejectAsync(Guid withdrawalId, Guid adminId, string reason, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"finance:withdrawal:{withdrawalId}", ct);
        var w = await db.Withdrawals.FirstOrDefaultAsync(x => x.Id == withdrawalId, ct) ?? throw new NotFoundException("Không tìm thấy yêu cầu rút tiền.");
        if (w.Status != WithdrawalStatus.Pending) throw new ConflictException("Chỉ từ chối được yêu cầu đang chờ duyệt.", "WITHDRAWAL_DONE");
        await ReverseAsync(w, adminId, reason, now, ct);
        Notify(w, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task ReverseAsync(Withdrawal w, Guid? by, string reason, DateTimeOffset now, CancellationToken ct)
    {
        w.Reject(by, reason, now);
        await ledger.PostAsync(LedgerKinds.WithdrawReversed, LedgerRefs.Withdrawal, w.Id, $"Hoàn lại ₫{w.Amount:N0} do rút tiền không thành công",
        [
            new LedgerLine(AccountKey.Platform(LedgerAccountType.WithdrawalPayable), LedgerDirection.Debit, w.Amount),
            new LedgerLine(SourceOf(w.OwnerType, w.OwnerId), LedgerDirection.Credit, w.Amount),
        ], $"withdraw-reversed:{w.Id}", ct);
    }

    private async Task<string?> AccountNoAsync(Withdrawal w, CancellationToken ct) => w.OwnerType == LedgerOwnerType.Shop
        ? await db.ShopBankAccounts.Where(b => b.Id == w.BankAccountId).Select(b => b.AccountNoEncrypted).FirstOrDefaultAsync(ct)
        : await db.BankAccounts.Where(b => b.Id == w.BankAccountId).Select(b => b.AccountNoEncrypted).FirstOrDefaultAsync(ct);

    private void Notify(Withdrawal w, DateTimeOffset now)
    {
        var (title, body) = w.Status == WithdrawalStatus.Done
            ? ("Rút tiền thành công", $"₫{w.Amount:N0} đã được chuyển về {w.BankCode} ***{w.AccountLast4}.")
            : ("Rút tiền không thành công", $"Yêu cầu rút ₫{w.Amount:N0} bị từ chối: {w.RejectReason}. Tiền đã được hoàn lại số dư.");
        var userId = w.RequestedBy;
        db.Notifications.Add(new Notification(userId, NotificationCategory.Wallet, title, body,
            w.OwnerType == LedgerOwnerType.Shop ? "/seller/tai-chinh" : "/tai-khoan/vi", LedgerRefs.Withdrawal, w.Id, now, $"withdrawal:{w.Id}:{w.Status}"));
    }

    public static WithdrawalDto ToDto(Withdrawal w, string? ownerName) => new(w.Id, w.OwnerType, w.OwnerId, ownerName, w.Amount, w.Status,
        Withdrawal.Label(w.Status), w.BankCode, w.AccountLast4, w.AccountName, w.RejectReason, w.BankRef, w.CreatedAt, w.ProcessedAt);
}
