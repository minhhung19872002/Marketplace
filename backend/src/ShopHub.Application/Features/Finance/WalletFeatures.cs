using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Storefront;
using ShopHub.Application.Identity;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Common;
using ShopHub.Domain.Finance;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Finance;

/// <summary>The 6-digit Ví ShopHub PIN: hashed, 5 wrong tries → locked for 15 minutes; set / reset only after an OTP.</summary>
public sealed class WalletPins(IApplicationDbContext db, IPasswordHasher hasher, IClock clock)
{
    /// <summary>Checks the PIN in its own small transaction so a wrong try is remembered even when the caller fails.</summary>
    public async Task VerifyAsync(Guid userId, string? pin, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct)
                     ?? throw new ConflictException("Bạn chưa tạo mật khẩu Ví ShopHub.", "WALLET_NO_PIN");
        if (wallet.IsLocked(now))
            throw new ConflictException("Ví ShopHub đang tạm khoá do nhập sai mật khẩu nhiều lần. Vui lòng thử lại sau 15 phút.", "WALLET_LOCKED");
        if (!Wallet.IsValidPin(pin) || !hasher.Verify(pin!, wallet.PinHash))
        {
            var left = wallet.RecordFailedPin(now);
            await db.SaveChangesAsync(ct);
            throw new ConflictException(left == 0
                ? "Sai mật khẩu Ví ShopHub quá 5 lần, ví tạm khoá 15 phút."
                : $"Mật khẩu Ví ShopHub không đúng, còn {left} lần thử.", "WALLET_BAD_PIN");
        }
        if (wallet.FailedPinAttempts > 0)
        {
            wallet.RecordGoodPin();
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task SetAsync(Guid userId, string pin, CancellationToken ct)
    {
        if (!Wallet.IsValidPin(pin)) throw new BusinessRuleException("Mật khẩu ví gồm đúng 6 chữ số.");
        if (pin.Distinct().Count() == 1 || "0123456789".Contains(pin) || "9876543210".Contains(pin))
            throw new BusinessRuleException("Mật khẩu ví quá dễ đoán, vui lòng chọn dãy số khác.");
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is null) db.Wallets.Add(new Wallet(userId, hasher.Hash(pin), clock.UtcNow));
        else wallet.ChangePin(hasher.Hash(pin), clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }
}

internal static class FinanceOtp
{
    public static async Task<string> PhoneOfAsync(IApplicationDbContext db, Guid userId, CancellationToken ct) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.Phone).FirstOrDefaultAsync(ct)
        ?? throw new ConflictException("Tài khoản cần có số điện thoại để xác thực.", "NO_PHONE");

    public static async Task VerifyAsync(IApplicationDbContext db, OtpService otp, Guid userId, string code, CancellationToken ct)
    {
        var phone = await PhoneOfAsync(db, userId, ct);
        var row = await otp.VerifyAsync(phone, OtpPurpose.Finance, code, ct);
        await otp.ConsumeAsync(row, ct);
    }
}

// ---------- OTP for finance actions (sent to the caller's own phone) ----------

public record SendFinanceOtpCommand : IRequest<OtpIssued>;

public sealed class SendFinanceOtpHandler(IApplicationDbContext db, OtpService otp, ICurrentUser currentUser) : IRequestHandler<SendFinanceOtpCommand, OtpIssued>
{
    public async Task<OtpIssued> Handle(SendFinanceOtpCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        return await otp.IssueAsync(await FinanceOtp.PhoneOfAsync(db, userId, ct), OtpPurpose.Finance, deliver: true, ct);
    }
}

// ---------- wallet overview ----------

public record WalletEntryDto(Guid Id, string Kind, LedgerDirection Direction, long Amount, string Description, string RefType, Guid RefId, DateTimeOffset PostedAt);

public record BankAccountDto(Guid Id, string BankCode, string AccountNoLast4, string AccountName, bool Verified, bool IsDefault);

public record WalletDto(long Balance, long PendingWithdrawals, bool HasPin, bool Locked, IReadOnlyList<BankAccountDto> BankAccounts,
    PagedResult<WalletEntryDto> History, IReadOnlyList<WithdrawalDto> Withdrawals);

public record GetWalletQuery(int Page = 1, int PageSize = 20) : IRequest<WalletDto>, IPagedRequest;

public sealed class GetWalletHandler(IApplicationDbContext db, Ledger ledger, ICurrentUser currentUser, IClock clock) : IRequestHandler<GetWalletQuery, WalletDto>
{
    public async Task<WalletDto> Handle(GetWalletQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var wallet = await db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.UserId == userId, ct);
        var key = AccountKey.Wallet(userId);
        var history = await FinanceViews.EntriesAsync(db, key, request.Page, request.PageSize, ct);
        var withdrawals = await db.Withdrawals.AsNoTracking().Where(w => w.OwnerType == LedgerOwnerType.Buyer && w.OwnerId == userId)
            .OrderByDescending(w => w.CreatedAt).Take(20).ToListAsync(ct);
        var banks = await db.BankAccounts.AsNoTracking().Where(b => b.UserId == userId && b.DeletedAt == null).OrderBy(b => b.CreatedAt)
            .Select(b => new BankAccountDto(b.Id, b.BankCode, b.AccountNoLast4, b.AccountName, true, false)).ToListAsync(ct);
        return new WalletDto(await ledger.BalanceAsync(key, ct),
            withdrawals.Where(w => w.Status is WithdrawalStatus.Pending or WithdrawalStatus.Processing).Sum(w => w.Amount),
            wallet is not null, wallet?.IsLocked(clock.UtcNow) ?? false, banks, history,
            withdrawals.Select(w => WithdrawalService.ToDto(w, null)).ToList());
    }
}

internal static class FinanceViews
{
    public static async Task<PagedResult<WalletEntryDto>> EntriesAsync(IApplicationDbContext db, AccountKey key, int page, int pageSize, CancellationToken ct)
    {
        var accountId = await db.LedgerAccounts.AsNoTracking()
            .Where(a => a.OwnerType == key.OwnerType && a.OwnerId == key.OwnerId && a.Type == key.Type).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
        if (accountId is null) return new PagedResult<WalletEntryDto>([], 0, page, pageSize);
        var query = from e in db.LedgerEntries.AsNoTracking()
                    join t in db.LedgerTransactions.AsNoTracking() on e.TransactionId equals t.Id
                    where e.AccountId == accountId
                    orderby e.PostedAt descending, e.Id
                    select new WalletEntryDto(e.Id, t.Kind, e.Direction, e.Amount, e.Description, e.RefType, e.RefId, e.PostedAt);
        return new PagedResult<WalletEntryDto>(await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct), await query.CountAsync(ct), page, pageSize);
    }
}

// ---------- PIN ----------

public record SetWalletPinCommand(string OtpCode, string Pin) : IRequest<Unit>;

public sealed class SetWalletPinValidator : AbstractValidator<SetWalletPinCommand>
{
    public SetWalletPinValidator()
    {
        RuleFor(x => x.OtpCode).NotNull().WithMessage("Vui lòng nhập mã xác thực.").Matches(@"^\d{6}$").WithMessage("Mã xác thực gồm 6 chữ số.");
        RuleFor(x => x.Pin).NotNull().WithMessage("Vui lòng nhập mật khẩu ví.").Matches(@"^\d{6}$").WithMessage("Mật khẩu ví gồm đúng 6 chữ số.");
    }
}

public sealed class SetWalletPinHandler(IApplicationDbContext db, OtpService otp, WalletPins pins, ICurrentUser currentUser)
    : IRequestHandler<SetWalletPinCommand, Unit>
{
    public async Task<Unit> Handle(SetWalletPinCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        await FinanceOtp.VerifyAsync(db, otp, userId, request.OtpCode, ct);
        await pins.SetAsync(userId, request.Pin, ct);
        return Unit.Value;
    }
}

// ---------- top-up through the gateway ----------

public record TopupStarted(Guid TopupId, Guid PaymentId, string RedirectUrl);

public record GatewayOptionDto(PaymentMethod Method, string Name);

public record TopupGatewaysQuery : IRequest<IReadOnlyList<GatewayOptionDto>>;

public sealed class TopupGatewaysHandler(IPaymentGatewayRegistry gateways) : IRequestHandler<TopupGatewaysQuery, IReadOnlyList<GatewayOptionDto>>
{
    public Task<IReadOnlyList<GatewayOptionDto>> Handle(TopupGatewaysQuery request, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GatewayOptionDto>>(gateways.Online.Select(g => new GatewayOptionDto(g.Method, g.DisplayName)).ToList());
}

// Method: which online gateway pays the top-up (null = the first one switched on)
public record CreateTopupCommand(long Amount, PaymentMethod? Method = null) : IRequest<TopupStarted>;

public sealed class CreateTopupHandler(
    IApplicationDbContext db,
    IPaymentGatewayRegistry gateways,
    ISystemParameters parameters,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<CreateTopupCommand, TopupStarted>
{
    public async Task<TopupStarted> Handle(CreateTopupCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var min = await parameters.GetIntAsync(ParameterKeys.FinanceTopupMin, ct);
        var max = await parameters.GetIntAsync(ParameterKeys.FinanceTopupMax, ct);
        if (request.Amount < min || request.Amount > max)
            throw new BusinessRuleException($"Số tiền nạp phải từ ₫{min:N0} đến ₫{max:N0}.");
        var gateway = request.Method is { } wanted
            ? gateways.Online.FirstOrDefault(g => g.Method == wanted) ?? throw new ConflictException("Cổng thanh toán này hiện không khả dụng.", "NO_GATEWAY")
            : gateways.Online.FirstOrDefault() ?? throw new ConflictException("Cổng thanh toán đang tắt, chưa nạp được tiền.", "NO_GATEWAY");

        var now = clock.UtcNow;
        var timeout = await parameters.GetIntAsync(ParameterKeys.PaymentTimeoutMinutes, ct);
        var topup = new WalletTopup(userId, request.Amount, now);
        var payment = new Payment(topup.Id, gateway.Method, request.Amount, now.AddMinutes(timeout), now, PaymentPurpose.WalletTopup);
        topup.Attach(payment.Id);
        db.WalletTopups.Add(topup);
        db.Payments.Add(payment);
        var start = await gateway.CreatePaymentAsync(
            new GatewayPaymentRequest(payment.Id, topup.Id, payment.Amount, $"Nạp ₫{request.Amount:N0} vào Ví ShopHub", payment.ExpiresAt,
                $"/tai-khoan/vi?topup={topup.Id}", currentUser.IpAddress, payment.CreatedAt), ct);
        payment.SetRedirect(start.RedirectUrl);
        await db.SaveChangesAsync(ct);
        return new TopupStarted(topup.Id, payment.Id, start.RedirectUrl);
    }
}

public record TopupDto(Guid Id, long Amount, TopupStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public record GetTopupQuery(Guid TopupId) : IRequest<TopupDto>;

public sealed class GetTopupHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<GetTopupQuery, TopupDto>
{
    public async Task<TopupDto> Handle(GetTopupQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        return await db.WalletTopups.AsNoTracking().Where(t => t.Id == request.TopupId && t.UserId == userId)
                   .Select(t => new TopupDto(t.Id, t.Amount, t.Status, t.CreatedAt, t.CompletedAt)).FirstOrDefaultAsync(ct)
               ?? throw new NotFoundException("Không tìm thấy lệnh nạp tiền.");
    }
}

/// <summary>Called by the payment processor inside the webhook transaction: credit the wallet exactly once.</summary>
public sealed class TopupProcessor(IApplicationDbContext db, Ledger ledger, IClock clock)
{
    public async Task ApplyAsync(Payment payment, bool success, CancellationToken ct)
    {
        var topup = await db.WalletTopups.SingleAsync(t => t.Id == payment.CheckoutId, ct);
        if (topup.Status == TopupStatus.Succeeded) return;
        var now = clock.UtcNow;
        if (!success)
        {
            topup.Fail(now);
            return;
        }
        topup.Succeed(now);
        await ledger.PostAsync(LedgerKinds.Topup, LedgerRefs.Topup, topup.Id, $"Nạp ₫{topup.Amount:N0} vào Ví ShopHub",
        [
            new LedgerLine(AccountKey.Platform(LedgerAccountType.PlatformCash), LedgerDirection.Debit, topup.Amount),
            new LedgerLine(AccountKey.Wallet(topup.UserId), LedgerDirection.Credit, topup.Amount),
        ], $"topup:{topup.Id}", ct);
        db.Notifications.Add(new Domain.Engage.Notification(topup.UserId, Domain.Engage.NotificationCategory.Wallet, "Nạp tiền thành công",
            $"Đã nạp ₫{topup.Amount:N0} vào Ví ShopHub.", "/tai-khoan/vi", LedgerRefs.Topup, topup.Id, now, $"topup:{topup.Id}"));
    }
}

// ---------- buyer bank accounts & withdrawals ----------

public record AddBankAccountCommand(string BankCode, string AccountNo, string AccountName, string OtpCode) : IRequest<Guid>;

public sealed class AddBankAccountValidator : AbstractValidator<AddBankAccountCommand>
{
    public AddBankAccountValidator()
    {
        RuleFor(x => x.BankCode).NotEmpty().WithMessage("Vui lòng chọn ngân hàng.").MaximumLength(20).WithMessage("Mã ngân hàng không hợp lệ.");
        RuleFor(x => x.AccountNo).NotNull().WithMessage("Vui lòng nhập số tài khoản.").Matches(@"^\d{6,20}$").WithMessage("Số tài khoản gồm 6–20 chữ số.");
        RuleFor(x => x.AccountName).NotEmpty().WithMessage("Vui lòng nhập tên chủ tài khoản.").MaximumLength(100).WithMessage("Tên tối đa 100 ký tự.");
        RuleFor(x => x.OtpCode).NotNull().WithMessage("Vui lòng nhập mã xác thực.").Matches(@"^\d{6}$").WithMessage("Mã xác thực gồm 6 chữ số.");
    }
}

public sealed class AddBankAccountHandler(IApplicationDbContext db, OtpService otp, IDataEncryptor encryptor, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<AddBankAccountCommand, Guid>
{
    public async Task<Guid> Handle(AddBankAccountCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        if (await db.BankAccounts.CountAsync(b => b.UserId == userId && b.DeletedAt == null, ct) >= 5)
            throw new ConflictException("Mỗi tài khoản lưu tối đa 5 tài khoản ngân hàng.", "BANK_LIMIT");
        await FinanceOtp.VerifyAsync(db, otp, userId, request.OtpCode, ct);
        var no = request.AccountNo.Trim();
        var account = new BankAccount(userId, request.BankCode, encryptor.Encrypt(no), no[^4..], request.AccountName, clock.UtcNow);
        db.BankAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account.Id;
    }
}

public record RemoveBankAccountCommand(Guid BankAccountId) : IRequest<Unit>;

public sealed class RemoveBankAccountHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock) : IRequestHandler<RemoveBankAccountCommand, Unit>
{
    public async Task<Unit> Handle(RemoveBankAccountCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var account = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == request.BankAccountId && b.UserId == userId && b.DeletedAt == null, ct)
                      ?? throw new NotFoundException("Không tìm thấy tài khoản ngân hàng.");
        account.Remove(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public record WalletWithdrawCommand(Guid BankAccountId, long Amount, string Pin) : IRequest<WithdrawalDto>;

public sealed class WalletWithdrawHandler(IApplicationDbContext db, WalletPins pins, WithdrawalService withdrawals, ICurrentUser currentUser)
    : IRequestHandler<WalletWithdrawCommand, WithdrawalDto>
{
    public async Task<WithdrawalDto> Handle(WalletWithdrawCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var bank = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BankAccountId && b.UserId == userId && b.DeletedAt == null, ct)
                   ?? throw new NotFoundException("Không tìm thấy tài khoản ngân hàng.");
        await pins.VerifyAsync(userId, request.Pin, ct);
        var w = await withdrawals.RequestAsync(LedgerOwnerType.Buyer, userId,
            new WithdrawalService.Destination(bank.Id, bank.BankCode, bank.AccountNoEncrypted, bank.AccountNoLast4, bank.AccountName), request.Amount, userId, ct);
        return WithdrawalService.ToDto(w, null);
    }
}
