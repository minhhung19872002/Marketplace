using ShopHub.Domain.Common;

namespace ShopHub.Domain.Promo;

public enum VoucherOwner
{
    Platform,
    Shop,
}

public enum VoucherType
{
    Amount,        // giảm tiền
    Percent,       // giảm % có trần
    FreeShipping,  // miễn phí vận chuyển (tối đa N)
    CoinCashback,  // hoàn xu (% có trần), cộng khi đơn hoàn thành
}

public enum VoucherAudience
{
    Everyone,
    NewBuyer,       // chưa có đơn nào thành công
    ShopFollowers,  // người theo dõi shop (voucher shop)
    MemberGold,     // hạng Vàng trở lên
    MemberDiamond,  // hạng Kim cương
}

public enum VoucherChannel
{
    All,
    Web,
    App,
}

/// <summary>
/// Platform or shop voucher. Usage is counted with conditional UPDATEs (used_count &lt; total_quota, per-user
/// counter &lt; per_user_limit) so parallel checkouts can never exceed either limit (spec 6.2).
/// </summary>
public class Voucher : AuditableEntity
{
    public const int MaxCodeLength = 20;

    private Voucher() { }

    public Voucher(VoucherOwner owner, Guid? shopId, string code, string name)
    {
        if (owner == VoucherOwner.Shop && shopId is null) throw new BusinessRuleException("Voucher của shop phải gắn với một shop.");
        Owner = owner;
        ShopId = owner == VoucherOwner.Shop ? shopId : null;
        Code = NormalizeCode(code);
        Name = name.Trim();
        IsActive = true;
    }

    public static string NormalizeCode(string code)
    {
        var c = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (c.Length is < 3 or > MaxCodeLength || !c.All(char.IsAsciiLetterOrDigit))
            throw new BusinessRuleException($"Mã voucher gồm 3–{MaxCodeLength} chữ cái hoặc số, không dấu, không khoảng trắng.");
        return c;
    }

    public VoucherOwner Owner { get; private set; }
    public Guid? ShopId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public VoucherType Type { get; private set; }
    // Amount: VND off. Percent / CoinCashback: basis points (1% = 100). FreeShipping: unused (MaxDiscount caps it)
    public long DiscountValue { get; private set; }
    public int DiscountPercentBp { get; private set; }
    public long? MaxDiscount { get; private set; }
    public long MinOrder { get; private set; }
    public VoucherAudience Audience { get; private set; }
    // Optional scope: only these leaf categories / products count towards the voucher (empty = all)
    public List<Guid> CategoryIds { get; private set; } = [];
    public List<Guid> ProductIds { get; private set; } = [];
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }
    public int? TotalQuota { get; private set; }
    public int UsedCount { get; private set; }
    public int PerUserLimit { get; private set; } = 1;
    public bool IsPublic { get; private set; } = true;
    public VoucherChannel Channel { get; private set; }
    public bool IsActive { get; private set; }
    // Platform voucher that only covers shops in the matching programme (free shipping → Freeship Xtra, others → Voucher Xtra)
    public bool XtraOnly { get; private set; }

    public void SetXtraOnly(bool xtraOnly)
    {
        if (xtraOnly && Owner != VoucherOwner.Platform) throw new BusinessRuleException("Chỉ voucher của sàn mới gắn được Freeship+ / Voucher Plus.");
        if (xtraOnly && Type == VoucherType.CoinCashback) throw new BusinessRuleException("Voucher hoàn xu không thuộc chương trình Freeship+ / Voucher Plus.");
        XtraOnly = xtraOnly;
    }

    public void Configure(VoucherType type, long discountValue, int discountPercentBp, long? maxDiscount, long minOrder,
        VoucherAudience audience, IReadOnlyList<Guid> categoryIds, IReadOnlyList<Guid> productIds,
        DateTimeOffset startAt, DateTimeOffset endAt, int? totalQuota, int perUserLimit, bool isPublic, VoucherChannel channel)
    {
        if (endAt <= startAt) throw new BusinessRuleException("Thời gian kết thúc phải sau thời gian bắt đầu.");
        if (minOrder < 0) throw new BusinessRuleException("Giá trị đơn tối thiểu không được âm.");
        if (perUserLimit < 1) throw new BusinessRuleException("Mỗi người dùng được ít nhất 1 lượt.");
        if (totalQuota is < 1) throw new BusinessRuleException("Tổng lượt dùng phải từ 1 trở lên.");
        if (totalQuota is { } q && q < UsedCount) throw new BusinessRuleException($"Tổng lượt không được nhỏ hơn số lượt đã dùng ({UsedCount}).");
        if (Owner == VoucherOwner.Shop && type == VoucherType.FreeShipping)
            throw new BusinessRuleException("Voucher miễn phí vận chuyển chỉ do sàn phát hành.");
        if (Owner == VoucherOwner.Shop && type == VoucherType.CoinCashback)
            throw new BusinessRuleException("Voucher hoàn xu chỉ do sàn phát hành.");
        if (Owner == VoucherOwner.Platform && audience == VoucherAudience.ShopFollowers)
            throw new BusinessRuleException("Voucher của sàn không dành riêng cho người theo dõi shop.");
        switch (type)
        {
            case VoucherType.Amount when discountValue <= 0:
                throw new BusinessRuleException("Số tiền giảm phải lớn hơn 0.");
            case VoucherType.Percent or VoucherType.CoinCashback when discountPercentBp is <= 0 or > 10_000:
                throw new BusinessRuleException("Phần trăm giảm từ 0,01% đến 100%.");
            case VoucherType.Percent or VoucherType.CoinCashback or VoucherType.FreeShipping when maxDiscount is null or <= 0:
                throw new BusinessRuleException("Cần nhập mức giảm tối đa.");
        }

        Type = type;
        DiscountValue = type == VoucherType.Amount ? discountValue : 0;
        DiscountPercentBp = type is VoucherType.Percent or VoucherType.CoinCashback ? discountPercentBp : 0;
        MaxDiscount = type == VoucherType.Amount ? null : maxDiscount;
        MinOrder = minOrder;
        Audience = audience;
        CategoryIds = categoryIds.Distinct().ToList();
        ProductIds = productIds.Distinct().ToList();
        StartAt = startAt;
        EndAt = endAt;
        TotalQuota = totalQuota;
        PerUserLimit = perUserLimit;
        IsPublic = isPublic;
        Channel = channel;
    }

    public void Rename(string name) => Name = name.Trim();

    /// <summary>Stop early: the voucher can no longer be used (orders already placed keep their discount).</summary>
    public void Stop(DateTimeOffset now)
    {
        IsActive = false;
        if (EndAt > now) EndAt = now;
    }

    public bool IsRunning(DateTimeOffset now) => IsActive && StartAt <= now && now < EndAt;
}

/// <summary>Voucher saved into the buyer's voucher wallet.</summary>
public class VoucherClaim : Entity
{
    private VoucherClaim() { }

    public VoucherClaim(Guid voucherId, Guid userId, DateTimeOffset now)
    {
        VoucherId = voucherId;
        UserId = userId;
        ClaimedAt = now;
    }

    public Guid VoucherId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
}

/// <summary>A voucher applied to a checkout (and the order it belongs to for shop vouchers). Reverted when cancelled while still valid.</summary>
public class VoucherUsage : Entity
{
    private VoucherUsage() { }

    public VoucherUsage(Guid voucherId, Guid userId, Guid checkoutId, Guid? orderId, long amount, DateTimeOffset now)
    {
        VoucherId = voucherId;
        UserId = userId;
        CheckoutId = checkoutId;
        OrderId = orderId;
        Amount = amount;
        UsedAt = now;
    }

    public Guid VoucherId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid CheckoutId { get; private set; }
    public Guid? OrderId { get; private set; }
    public long Amount { get; private set; }
    public DateTimeOffset UsedAt { get; private set; }
    public DateTimeOffset? RevertedAt { get; private set; }

    public void Revert(DateTimeOffset now) => RevertedAt ??= now;
}

/// <summary>Per-user usage counter — incremented with a conditional upsert so the per-user limit holds under concurrency.</summary>
public class VoucherUserCounter
{
    private VoucherUserCounter() { }

    public Guid VoucherId { get; private set; }
    public Guid UserId { get; private set; }
    public int UsedCount { get; private set; }
}

public enum CoinReason
{
    AdminGrant,
    CheckoutSpend,
    CheckoutRefund,
    ReviewReward,
    VoucherCashback,
    Expired,
    CheckIn,
}

/// <summary>ShopHub Xu ledger: the balance is the sum of the rows, never a stored counter.</summary>
public class CoinEntry : Entity
{
    private CoinEntry() { }

    public CoinEntry(Guid userId, long delta, CoinReason reason, string? refType, Guid? refId, DateTimeOffset? expiresAt, string? note, DateTimeOffset now)
    {
        if (delta == 0) throw new BusinessRuleException("Số xu phải khác 0.");
        UserId = userId;
        Delta = delta;
        Reason = reason;
        RefType = refType;
        RefId = refId;
        ExpiresAt = expiresAt;
        Note = note;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }
    public long Delta { get; private set; }
    public CoinReason Reason { get; private set; }
    public string? RefType { get; private set; }
    public Guid? RefId { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    // Set on an expired credit once the expiry job has accounted for it, so the job never visits it again
    public DateTimeOffset? ExpiryCheckedAt { get; private set; }
}
