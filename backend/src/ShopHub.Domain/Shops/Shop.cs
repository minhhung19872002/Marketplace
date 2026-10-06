using ShopHub.Domain.Common;

namespace ShopHub.Domain.Shops;

public enum ShopType
{
    Personal,
    Business,
    Mall,
}

public enum ShopStatus
{
    PendingReview,
    Active,
    Vacation,
    Locked,
    Rejected,
}

public class Shop : AuditableEntity
{
    private Shop() { }

    public Shop(Guid ownerId, string name, string slug, ShopType type)
    {
        if (type == ShopType.Mall) throw new BusinessRuleException("Nhãn Mall do sàn cấp, không tự đăng ký.");
        OwnerId = ownerId;
        Rename(name, slug);
        Type = type;
        Status = ShopStatus.PendingReview;
    }

    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? LogoUrl { get; private set; }
    public string? CoverUrl { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public ShopType Type { get; private set; }
    public ShopStatus Status { get; private set; }
    public bool IsMall => Type == ShopType.Mall;
    public bool IsPreferred { get; private set; }
    public DateTimeOffset? VacationUntil { get; private set; }
    public string? RejectReason { get; private set; }
    public string? LockReason { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public int PenaltyPoints { get; private set; }

    // Denormalised, recomputed from source rows (followers, active products) — never incremented in place
    public int FollowerCount { get; private set; }
    public int ProductCount { get; private set; }
    public uint Version { get; private set; }

    public bool CanSell => Status == ShopStatus.Active;

    public void Rename(string name, string slug)
    {
        var n = name.Trim();
        if (n.Length is < 3 or > 50) throw new BusinessRuleException("Tên shop phải có từ 3 đến 50 ký tự.");
        Name = n;
        Slug = slug;
    }

    public void UpdateProfile(string description, string? logoUrl, string? coverUrl)
    {
        Description = description.Trim();
        LogoUrl = logoUrl;
        CoverUrl = coverUrl;
    }

    public void Approve(DateTimeOffset now)
    {
        if (Status != ShopStatus.PendingReview) throw new BusinessRuleException("Chỉ duyệt được shop đang chờ duyệt.");
        Status = ShopStatus.Active;
        ApprovedAt = now;
        RejectReason = null;
    }

    public void Reject(string reason)
    {
        if (Status != ShopStatus.PendingReview) throw new BusinessRuleException("Chỉ từ chối được shop đang chờ duyệt.");
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Cần ghi lý do từ chối.");
        Status = ShopStatus.Rejected;
        RejectReason = reason.Trim();
    }

    /// <summary>A rejected applicant fixes the documents and applies again.</summary>
    public void Resubmit()
    {
        if (Status != ShopStatus.Rejected) throw new BusinessRuleException("Chỉ gửi lại được hồ sơ đã bị từ chối.");
        Status = ShopStatus.PendingReview;
    }

    public void Lock(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Cần ghi lý do khoá.");
        Status = ShopStatus.Locked;
        LockReason = reason.Trim();
    }

    public void Unlock()
    {
        if (Status != ShopStatus.Locked) throw new BusinessRuleException("Shop không bị khoá.");
        Status = ShopStatus.Active;
        LockReason = null;
    }

    public void StartVacation(DateTimeOffset until, DateTimeOffset now)
    {
        if (Status != ShopStatus.Active) throw new BusinessRuleException("Chỉ shop đang hoạt động mới bật được chế độ tạm nghỉ.");
        if (until <= now) throw new BusinessRuleException("Ngày kết thúc tạm nghỉ phải ở tương lai.");
        Status = ShopStatus.Vacation;
        VacationUntil = until;
    }

    public void EndVacation()
    {
        if (Status != ShopStatus.Vacation) throw new BusinessRuleException("Shop không ở chế độ tạm nghỉ.");
        Status = ShopStatus.Active;
        VacationUntil = null;
    }

    public void SetLabels(bool mall, bool preferred)
    {
        Type = mall ? ShopType.Mall : Type == ShopType.Mall ? ShopType.Business : Type;
        IsPreferred = preferred;
    }
}

public enum KycStatus
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>Identity / business documents. Files live in the private sh-kyc bucket (signed, expiring URLs only).</summary>
public class ShopKyc : AuditableEntity
{
    private ShopKyc() { }

    public ShopKyc(Guid shopId)
    {
        ShopId = shopId;
        Status = KycStatus.Pending;
    }

    public Guid ShopId { get; private set; }
    public string LegalName { get; private set; } = string.Empty;
    public string? TaxCode { get; private set; }
    public string? IdCardNumberEncrypted { get; private set; }
    public string? IdCardFrontKey { get; private set; }
    public string? IdCardBackKey { get; private set; }
    public string? BusinessLicenseKey { get; private set; }
    public KycStatus Status { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? RejectReason { get; private set; }

    public void SetPersonal(string legalName, string idCardNumberEncrypted, string frontKey, string backKey)
    {
        LegalName = legalName.Trim();
        IdCardNumberEncrypted = idCardNumberEncrypted;
        IdCardFrontKey = frontKey;
        IdCardBackKey = backKey;
        TaxCode = null;
        BusinessLicenseKey = null;
        Status = KycStatus.Pending;
    }

    public void SetBusiness(string legalName, string taxCode, string licenseKey)
    {
        LegalName = legalName.Trim();
        TaxCode = taxCode.Trim();
        BusinessLicenseKey = licenseKey;
        IdCardNumberEncrypted = null;
        IdCardFrontKey = null;
        IdCardBackKey = null;
        Status = KycStatus.Pending;
    }

    public void Review(bool approved, Guid reviewerId, string? reason, DateTimeOffset now)
    {
        Status = approved ? KycStatus.Approved : KycStatus.Rejected;
        ReviewedBy = reviewerId;
        ReviewedAt = now;
        RejectReason = approved ? null : reason;
    }
}

public class ShopWarehouse : AuditableEntity
{
    private ShopWarehouse() { }

    public ShopWarehouse(Guid shopId)
    {
        ShopId = shopId;
    }

    public Guid ShopId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string ContactName { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string ProvinceCode { get; private set; } = string.Empty;
    public string DistrictCode { get; private set; } = string.Empty;
    public string WardCode { get; private set; } = string.Empty;
    public string Street { get; private set; } = string.Empty;
    public bool IsPickupDefault { get; private set; }
    public bool IsReturnDefault { get; private set; }

    public void Update(string name, string contactName, string phone, string provinceCode, string districtCode, string wardCode,
        string street, bool isPickupDefault, bool isReturnDefault)
    {
        Name = name.Trim();
        ContactName = contactName.Trim();
        Phone = phone;
        ProvinceCode = provinceCode;
        DistrictCode = districtCode;
        WardCode = wardCode;
        Street = street.Trim();
        IsPickupDefault = isPickupDefault;
        IsReturnDefault = isReturnDefault;
    }
}

public enum ShopStaffRole
{
    Owner,
    Manager,
    CustomerService,
    Warehouse,
}

public class ShopStaff : AuditableEntity
{
    private ShopStaff() { }

    public ShopStaff(Guid shopId, Guid userId, ShopStaffRole role, IReadOnlyCollection<string> permissions)
    {
        ShopId = shopId;
        UserId = userId;
        Role = role;
        Permissions = permissions.Distinct().ToList();
    }

    public Guid ShopId { get; private set; }
    public Guid UserId { get; private set; }
    public ShopStaffRole Role { get; private set; }

    // Explicit grants for non-owner staff (owners implicitly hold every shop permission)
    public List<string> Permissions { get; private set; } = [];

    public bool Has(string permission) => Role == ShopStaffRole.Owner || Permissions.Contains(permission);

    public void Change(ShopStaffRole role, IReadOnlyCollection<string> permissions)
    {
        if (Role == ShopStaffRole.Owner || role == ShopStaffRole.Owner)
            throw new BusinessRuleException("Không đổi được vai trò chủ shop.");
        Role = role;
        Permissions = permissions.Distinct().ToList();
    }
}

public class ShopBankAccount : AuditableEntity
{
    private ShopBankAccount() { }

    public ShopBankAccount(Guid shopId, string bankCode, string accountNoEncrypted, string accountNoLast4, string accountName, bool isDefault)
    {
        ShopId = shopId;
        BankCode = bankCode.Trim().ToUpperInvariant();
        AccountNoEncrypted = accountNoEncrypted;
        AccountNoLast4 = accountNoLast4;
        AccountName = accountName.Trim().ToUpperInvariant();
        IsDefault = isDefault;
    }

    public Guid ShopId { get; private set; }
    public string BankCode { get; private set; } = string.Empty;
    public string AccountNoEncrypted { get; private set; } = string.Empty;
    public string AccountNoLast4 { get; private set; } = string.Empty;
    public string AccountName { get; private set; } = string.Empty;
    public DateTimeOffset? VerifiedAt { get; private set; }
    public bool IsDefault { get; private set; }

    public void MarkVerified(DateTimeOffset now) => VerifiedAt = now;
}

/// <summary>Penalty point given to a shop (late fulfilment, seller cancellation…); the shop's total is recomputed from these rows.</summary>
public class ShopPenalty : Entity
{
    private ShopPenalty() { }

    public ShopPenalty(Guid shopId, int points, string reason, Guid? orderId, DateTimeOffset now)
    {
        if (points <= 0) throw new BusinessRuleException("Điểm phạt phải lớn hơn 0.");
        ShopId = shopId;
        Points = points;
        Reason = reason;
        OrderId = orderId;
        CreatedAt = now;
    }

    public Guid ShopId { get; private set; }
    public int Points { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid? OrderId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

