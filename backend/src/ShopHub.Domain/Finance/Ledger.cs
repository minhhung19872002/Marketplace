using ShopHub.Domain.Common;

namespace ShopHub.Domain.Finance;

public enum LedgerOwnerType
{
    Platform,
    Shop,
    Buyer,
}

/// <summary>
/// Kinds of ledger account (spec 3.9, 4.7). Debit-normal accounts (what the platform holds or spends) grow with
/// debits; credit-normal ones (what the platform owes or earned) grow with credits.
/// </summary>
public enum LedgerAccountType
{
    ShopPending,        // CHỜ GIẢI NGÂN — shop earnings of completed orders not released yet
    ShopAvailable,      // KHẢ DỤNG — released, can be withdrawn
    BuyerWallet,        // VÍ — Ví ShopHub of a buyer
    PlatformCash,       // money the platform holds at gateways / banks / carriers (COD)
    PlatformEscrow,     // buyer money held for orders that are not completed yet
    PlatformSubsidy,    // what the platform pays for its own vouchers, xu and freeship
    FeeFixed,           // phí cố định (% theo ngành)
    FeePayment,         // phí thanh toán
    FeeService,         // phí dịch vụ (Freeship Xtra / Voucher Xtra)
    CarrierPayable,     // shipping fees owed to carriers
    WithdrawalPayable,  // withdrawals approved / requested but not paid out yet
}

public enum LedgerDirection
{
    Debit,
    Credit,
}

public class LedgerAccount : Entity
{
    private LedgerAccount() { }

    public LedgerAccount(LedgerOwnerType ownerType, Guid ownerId, LedgerAccountType type, DateTimeOffset now)
    {
        OwnerType = ownerType;
        OwnerId = ownerId;
        Type = type;
        AllowNegative = ownerType == LedgerOwnerType.Platform;
        CreatedAt = now;
    }

    public LedgerOwnerType OwnerType { get; private set; }
    // Guid.Empty for the platform's own accounts
    public Guid OwnerId { get; private set; }
    public LedgerAccountType Type { get; private set; }
    /// <summary>
    /// Cached sum of the entries on the normal side (debits − credits for debit-normal accounts, the reverse for
    /// the others). Updated in the same statement as each posting; CHECK (allow_negative OR balance &gt;= 0) is the
    /// database backstop against overdrawing; the ledger check job recomputes it from the entries.
    /// </summary>
    public long Balance { get; private set; }
    public bool AllowNegative { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static bool IsDebitNormal(LedgerAccountType type) => type is LedgerAccountType.PlatformCash or LedgerAccountType.PlatformSubsidy;

    public static string Label(LedgerAccountType type) => type switch
    {
        LedgerAccountType.ShopPending => "Chờ giải ngân",
        LedgerAccountType.ShopAvailable => "Số dư khả dụng",
        LedgerAccountType.BuyerWallet => "Ví ShopHub",
        LedgerAccountType.PlatformCash => "Tiền của sàn (cổng / ngân hàng / COD)",
        LedgerAccountType.PlatformEscrow => "Tiền người mua đang tạm giữ",
        LedgerAccountType.PlatformSubsidy => "Sàn trợ giá (voucher sàn, xu, freeship)",
        LedgerAccountType.FeeFixed => "Doanh thu phí cố định",
        LedgerAccountType.FeePayment => "Doanh thu phí thanh toán",
        LedgerAccountType.FeeService => "Doanh thu phí dịch vụ",
        LedgerAccountType.CarrierPayable => "Phải trả đơn vị vận chuyển",
        LedgerAccountType.WithdrawalPayable => "Rút tiền chờ chi",
        _ => type.ToString(),
    };
}

/// <summary>One balanced posting: Σ debits = Σ credits over its entries.</summary>
public class LedgerTransaction : Entity
{
    private LedgerTransaction() { }

    public LedgerTransaction(string kind, string refType, Guid refId, string description, string? dedupeKey, DateTimeOffset now)
    {
        Kind = kind;
        RefType = refType;
        RefId = refId;
        Description = description;
        DedupeKey = dedupeKey;
        PostedAt = now;
    }

    public string Kind { get; private set; } = string.Empty;
    public string RefType { get; private set; } = string.Empty;
    public Guid RefId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    // Unique when set: a one-off posting (top-up, withdrawal step) can never be written twice
    public string? DedupeKey { get; private set; }
    public DateTimeOffset PostedAt { get; private set; }

    public List<LedgerEntry> Entries { get; private set; } = [];
}

public class LedgerEntry : Entity
{
    private LedgerEntry() { }

    public LedgerEntry(Guid transactionId, Guid accountId, LedgerDirection direction, long amount, string refType, Guid refId, string description,
        DateTimeOffset now)
    {
        if (amount <= 0) throw new BusinessRuleException("Bút toán phải có số tiền dương.");
        TransactionId = transactionId;
        AccountId = accountId;
        Direction = direction;
        Amount = amount;
        RefType = refType;
        RefId = refId;
        Description = description;
        PostedAt = now;
    }

    public Guid TransactionId { get; private set; }
    public Guid AccountId { get; private set; }
    public LedgerDirection Direction { get; private set; }
    public long Amount { get; private set; }
    public string RefType { get; private set; } = string.Empty;
    public Guid RefId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public DateTimeOffset PostedAt { get; private set; }
}

public enum FeeType
{
    Fixed,    // CỐ ĐỊNH — % of the goods value after the shop's own discounts, by category
    Payment,  // THANH TOÁN — % of the money the buyer paid
    FreeshipXtra,  // DỊCH VỤ — Freeship Xtra, only on orders of shops that joined
    VoucherXtra,   // DỊCH VỤ — Voucher Xtra, only on orders of shops that joined
}

/// <summary>Fee schedule row (spec 4.7, VI.7): a category (null = every category) and a validity window.</summary>
public class FeeRule : Entity
{
    private FeeRule() { }

    public FeeRule(Guid? categoryId, FeeType feeType, int rateBp, DateTimeOffset validFrom, string? note, DateTimeOffset now)
    {
        if (rateBp is < 0 or > 5_000) throw new BusinessRuleException("Tỉ lệ phí phải từ 0 đến 50%.");
        if (feeType == FeeType.Payment && categoryId is not null)
            throw new BusinessRuleException("Phí thanh toán áp dụng cho mọi ngành hàng, không đặt theo danh mục.");
        CategoryId = categoryId;
        FeeType = feeType;
        RateBp = rateBp;
        // timestamptz columns only take UTC
        ValidFrom = validFrom.ToUniversalTime();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        CreatedAt = now;
    }

    public Guid? CategoryId { get; private set; }
    public FeeType FeeType { get; private set; }
    public int RateBp { get; private set; }
    public DateTimeOffset ValidFrom { get; private set; }
    public DateTimeOffset? ValidTo { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool AppliesAt(DateTimeOffset at) => ValidFrom <= at && (ValidTo is null || at < ValidTo);

    /// <summary>A newer rule for the same scope starts: this one stops right then.</summary>
    public void EndAt(DateTimeOffset at)
    {
        if (at <= ValidFrom) throw new BusinessRuleException("Biểu phí mới phải bắt đầu sau biểu phí đang áp dụng.");
        ValidTo = at.ToUniversalTime();
    }
}

public enum SettlementStatus
{
    Released,  // ĐÃ GIẢI NGÂN vào số dư khả dụng
}

/// <summary>One release run for a shop: the orders whose earnings moved from "chờ giải ngân" to "khả dụng".</summary>
public class Settlement : Entity
{
    private Settlement() { }

    public Settlement(Guid shopId, string code, DateTimeOffset periodFrom, DateTimeOffset periodTo, DateTimeOffset now)
    {
        ShopId = shopId;
        Code = code;
        PeriodFrom = periodFrom;
        PeriodTo = periodTo;
        Status = SettlementStatus.Released;
        CreatedAt = now;
    }

    public Guid ShopId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public DateTimeOffset PeriodFrom { get; private set; }
    public DateTimeOffset PeriodTo { get; private set; }
    public long Gross { get; private set; }
    public long Fees { get; private set; }
    public long Net { get; private set; }
    public SettlementStatus Status { get; private set; }
    public string? FileUrl { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public List<SettlementItem> Items { get; private set; } = [];

    public void Add(SettlementItem item)
    {
        Items.Add(item);
        Gross += item.Gross;
        Fees += item.FixedFee + item.PaymentFee + item.ServiceFee;
        Net += item.Net;
    }
}

/// <summary>Frozen breakdown of one order at release (unique per order: an order is released once).</summary>
public class SettlementItem : Entity
{
    private SettlementItem() { }

    public SettlementItem(Guid settlementId, Guid orderId, Guid shopId, long goods, long shopDiscount, long refundsBorne, long fixedFee,
        long paymentFee, long serviceFee, long net, DateTimeOffset releasedAt)
    {
        if (net != goods - shopDiscount - refundsBorne - fixedFee - paymentFee - serviceFee)
            throw new BusinessRuleException("Số tiền giải ngân không khớp với các khoản của đơn.");
        SettlementId = settlementId;
        OrderId = orderId;
        ShopId = shopId;
        Goods = goods;
        ShopDiscount = shopDiscount;
        RefundsBorne = refundsBorne;
        FixedFee = fixedFee;
        PaymentFee = paymentFee;
        ServiceFee = serviceFee;
        Net = net;
        ReleasedAt = releasedAt;
    }

    public Guid SettlementId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ShopId { get; private set; }
    public long Goods { get; private set; }
    public long ShopDiscount { get; private set; }
    public long RefundsBorne { get; private set; }
    public long FixedFee { get; private set; }
    public long PaymentFee { get; private set; }
    public long ServiceFee { get; private set; }
    public long Net { get; private set; }
    public DateTimeOffset ReleasedAt { get; private set; }

    // Goods − shop discounts − refunds the shop bears: the base the fees are taken from
    public long Gross => Goods - ShopDiscount - RefundsBorne;
}

public enum WithdrawalStatus
{
    Pending,     // CHỜ duyệt
    Processing,  // ĐANG XỬ LÝ — sent to the bank
    Done,        // XONG
    Rejected,    // TỪ CHỐI — money back to the balance
}

/// <summary>Money out of a shop balance or a buyer wallet to a verified bank account (spec 3.9, IV).</summary>
public class Withdrawal : Entity
{
    private Withdrawal() { }

    public Withdrawal(LedgerOwnerType ownerType, Guid ownerId, Guid bankAccountId, string bankCode, string accountLast4, string accountName,
        long amount, Guid requestedBy, DateTimeOffset now)
    {
        if (amount <= 0) throw new BusinessRuleException("Số tiền rút phải lớn hơn 0.");
        if (ownerType == LedgerOwnerType.Platform) throw new BusinessRuleException("Sàn không rút tiền qua yêu cầu rút.");
        OwnerType = ownerType;
        OwnerId = ownerId;
        BankAccountId = bankAccountId;
        BankCode = bankCode;
        AccountLast4 = accountLast4;
        AccountName = accountName;
        Amount = amount;
        RequestedBy = requestedBy;
        CreatedAt = now;
        Status = WithdrawalStatus.Pending;
    }

    public LedgerOwnerType OwnerType { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid BankAccountId { get; private set; }
    // Snapshot of the destination (the account may be removed later)
    public string BankCode { get; private set; } = string.Empty;
    public string AccountLast4 { get; private set; } = string.Empty;
    public string AccountName { get; private set; } = string.Empty;
    public long Amount { get; private set; }
    public WithdrawalStatus Status { get; private set; }
    public Guid RequestedBy { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public string? RejectReason { get; private set; }
    public string? BankRef { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public uint Version { get; private set; }

    public void StartProcessing(Guid? by)
    {
        if (Status != WithdrawalStatus.Pending) throw new BusinessRuleException("Yêu cầu rút tiền này đã được xử lý.");
        Status = WithdrawalStatus.Processing;
        DecidedBy = by;
    }

    public void Complete(string bankRef, DateTimeOffset now)
    {
        if (Status != WithdrawalStatus.Processing) throw new BusinessRuleException("Yêu cầu rút tiền chưa được duyệt.");
        Status = WithdrawalStatus.Done;
        BankRef = bankRef;
        ProcessedAt = now;
    }

    public void Reject(Guid? by, string reason, DateTimeOffset now)
    {
        if (Status is not (WithdrawalStatus.Pending or WithdrawalStatus.Processing))
            throw new BusinessRuleException("Yêu cầu rút tiền này đã được xử lý.");
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Vui lòng nhập lý do từ chối.");
        Status = WithdrawalStatus.Rejected;
        DecidedBy = by;
        RejectReason = reason.Trim();
        ProcessedAt = now;
    }

    public static string Label(WithdrawalStatus status) => status switch
    {
        WithdrawalStatus.Pending => "Chờ duyệt",
        WithdrawalStatus.Processing => "Đang xử lý",
        WithdrawalStatus.Done => "Đã chuyển",
        WithdrawalStatus.Rejected => "Từ chối",
        _ => status.ToString(),
    };
}
