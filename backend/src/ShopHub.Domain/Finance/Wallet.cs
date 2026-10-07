using ShopHub.Domain.Common;

namespace ShopHub.Domain.Finance;

/// <summary>
/// Ví ShopHub settings of a buyer (spec IV): the 6-digit payment PIN (hashed) and its lockout. The money itself is
/// the ledger account <see cref="LedgerAccountType.BuyerWallet"/> — balance = sum of the entries.
/// </summary>
public class Wallet : Entity
{
    public const int MaxPinAttempts = 5;
    public static readonly TimeSpan PinLockout = TimeSpan.FromMinutes(15);

    private Wallet() { }

    public Wallet(Guid userId, string pinHash, DateTimeOffset now)
    {
        UserId = userId;
        PinHash = pinHash;
        CreatedAt = now;
        PinChangedAt = now;
    }

    public Guid UserId { get; private set; }
    public string PinHash { get; private set; } = string.Empty;
    public int FailedPinAttempts { get; private set; }
    public DateTimeOffset? PinLockedUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset PinChangedAt { get; private set; }
    public uint Version { get; private set; }

    public static bool IsValidPin(string? pin) => pin is { Length: 6 } && pin.All(char.IsAsciiDigit);

    public bool IsLocked(DateTimeOffset now) => PinLockedUntil is { } until && until > now;

    public void ChangePin(string pinHash, DateTimeOffset now)
    {
        PinHash = pinHash;
        PinChangedAt = now;
        FailedPinAttempts = 0;
        PinLockedUntil = null;
    }

    /// <returns>Attempts left before the lockout.</returns>
    public int RecordFailedPin(DateTimeOffset now)
    {
        FailedPinAttempts++;
        if (FailedPinAttempts >= MaxPinAttempts)
        {
            PinLockedUntil = now + PinLockout;
            FailedPinAttempts = 0;
            return 0;
        }
        return MaxPinAttempts - FailedPinAttempts;
    }

    public void RecordGoodPin() => FailedPinAttempts = 0;
}

public enum TopupStatus
{
    Pending,
    Succeeded,
    Failed,
    Expired,
}

/// <summary>Nạp tiền vào Ví ShopHub through a payment gateway; credited only by the gateway's webhook.</summary>
public class WalletTopup : Entity
{
    private WalletTopup() { }

    public WalletTopup(Guid userId, long amount, DateTimeOffset now)
    {
        if (amount <= 0) throw new BusinessRuleException("Số tiền nạp phải lớn hơn 0.");
        UserId = userId;
        Amount = amount;
        Status = TopupStatus.Pending;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }
    public long Amount { get; private set; }
    public TopupStatus Status { get; private set; }
    public Guid? PaymentId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public void Attach(Guid paymentId) => PaymentId = paymentId;

    /// <summary>The gateway says the money arrived — even after the window closed, it is credited (never kept by the platform).</summary>
    public void Succeed(DateTimeOffset now)
    {
        if (Status == TopupStatus.Succeeded) throw new BusinessRuleException("Lệnh nạp tiền đã được ghi nhận.");
        Status = TopupStatus.Succeeded;
        CompletedAt = now;
    }

    public void Fail(DateTimeOffset now)
    {
        if (Status != TopupStatus.Pending) return;
        Status = TopupStatus.Failed;
        CompletedAt = now;
    }

    public void Expire(DateTimeOffset now)
    {
        if (Status != TopupStatus.Pending) return;
        Status = TopupStatus.Expired;
        CompletedAt = now;
    }
}

/// <summary>A buyer's bank account for wallet withdrawals; verified by an OTP to the account owner's phone when added.</summary>
public class BankAccount : Entity
{
    private BankAccount() { }

    public BankAccount(Guid userId, string bankCode, string accountNoEncrypted, string accountNoLast4, string accountName, DateTimeOffset now)
    {
        UserId = userId;
        BankCode = bankCode.Trim().ToUpperInvariant();
        AccountNoEncrypted = accountNoEncrypted;
        AccountNoLast4 = accountNoLast4;
        AccountName = accountName.Trim().ToUpperInvariant();
        CreatedAt = now;
        VerifiedAt = now;
    }

    public Guid UserId { get; private set; }
    public string BankCode { get; private set; } = string.Empty;
    public string AccountNoEncrypted { get; private set; } = string.Empty;
    public string AccountNoLast4 { get; private set; } = string.Empty;
    public string AccountName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset VerifiedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public void Remove(DateTimeOffset now) => DeletedAt = now;

    /// <summary>Account deleted (Nghị định 13/2023): the number and holder name go; the last 4 digits stay for past withdrawals.</summary>
    public void Anonymise(DateTimeOffset now)
    {
        AccountNoEncrypted = string.Empty;
        AccountName = "Người dùng đã xoá";
        DeletedAt ??= now;
    }
}
