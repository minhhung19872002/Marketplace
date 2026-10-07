using ShopHub.Domain.Common;

namespace ShopHub.Domain.Catalog;

public enum ProductStatus
{
    Draft,
    PendingReview,
    Active,
    Hidden,
    Banned,
    Deleted,
}

public enum ProductCondition
{
    New,
    Used,
}

public enum MediaType
{
    Image,
    Video,
}

public record TierSpec(string Name, IReadOnlyList<OptionSpec> Options);

public record OptionSpec(string Value, string? ImageUrl);

/// <summary>Price/stock for one combination; option values identify the SKU (tier 2 value null without a 2nd tier).</summary>
public record SkuSpec(string? Option1, string? Option2, string? SellerSku, long Price, long OriginalPrice, int Stock, int? WeightG, bool IsActive,
    PackageSize? Size = null);

/// <summary>Kích thước đóng gói of one variant (3.2) in mm; a variant without one uses the product's.</summary>
public record PackageSize(int LengthMm, int WidthMm, int HeightMm);

public record MediaSpec(MediaType Type, Guid? AssetId, string Url, string? OptionValue);

/// <summary>
/// SPU with up to two variant tiers. Every product has at least one SKU; cart and order lines point at SKUs.
/// Status only changes through the methods below (no outside assignment).
/// </summary>
public class Product : AuditableEntity
{
    public const int MaxTiers = 2;
    public const int MaxOptionsPerTier = 20;
    public const int MaxImages = 9;
    public const int MaxNameLength = 120;

    private readonly List<VariantTier> _tiers = [];
    private readonly List<Sku> _skus = [];
    private readonly List<ProductMedia> _media = [];
    private readonly List<ProductAttribute> _attributes = [];

    private Product() { }

    public Product(Guid shopId)
    {
        ShopId = shopId;
        Status = ProductStatus.Draft;
    }

    public Guid ShopId { get; private set; }
    public Guid CategoryId { get; private set; }
    public Guid? BrandId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public ProductStatus Status { get; private set; }
    public ProductCondition Condition { get; private set; }
    public int WeightG { get; private set; }
    public int LengthMm { get; private set; }
    public int WidthMm { get; private set; }
    public int HeightMm { get; private set; }
    public bool IsPreorder { get; private set; }
    public int PreorderDays { get; private set; }

    // Giới hạn mua mỗi người (all variants together); null = no limit
    public int? MaxPerBuyer { get; private set; }
    // Kho gửi when the shop runs several warehouses (null → the default pickup warehouse)
    public Guid? WarehouseId { get; private set; }

    public void ShipFrom(Guid? warehouseId) => WarehouseId = warehouseId;

    // Đơn vị vận chuyển cho sản phẩm (III.3): only these carriers may take it; empty = every carrier the shop uses
    public List<string> CarrierCodes { get; private set; } = [];

    public void LimitCarriers(IEnumerable<string> codes) => CarrierCodes = codes.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct().Order().ToList();

    // Denormalised, always recomputed from the source rows (SKUs / orders / reviews), never accumulated
    public long MinPrice { get; private set; }
    public long MaxPrice { get; private set; }
    public int SoldCount { get; private set; }
    public double RatingAvg { get; private set; }
    public int RatingCount { get; private set; }
    public int LikeCount { get; private set; }
    public int ViewCount { get; private set; }

    public string? BanReason { get; private set; }

    // Reviewer note when sent back for changes; auto-flag reasons (banned keywords) for the review queue
    public string? ReviewNote { get; private set; }
    public string? Flags { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public uint Version { get; private set; }

    public IReadOnlyList<VariantTier> Tiers => _tiers;
    public IReadOnlyList<Sku> Skus => _skus;
    public IReadOnlyList<ProductMedia> Media => _media;
    public IReadOnlyList<ProductAttribute> Attributes => _attributes;

    public bool IsBuyable => Status == ProductStatus.Active;

    // ---------- Content ----------

    /// <summary>Set the general info. Returns true when a field that requires re-review changed (name, category).</summary>
    public const int MaxPurchaseLimit = 999;

    public void SetPurchaseLimit(int? maxPerBuyer)
    {
        if (maxPerBuyer is < 1 or > MaxPurchaseLimit)
            throw new BusinessRuleException($"Giới hạn mua mỗi người từ 1 đến {MaxPurchaseLimit} (để trống nếu không giới hạn).");
        MaxPerBuyer = maxPerBuyer;
    }

    public bool SetInfo(Guid categoryId, Guid? brandId, string name, string slug, string sanitizedDescription, ProductCondition condition,
        int weightG, int lengthMm, int widthMm, int heightMm, bool isPreorder, int preorderDays)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
            throw new BusinessRuleException($"Tên sản phẩm phải có từ 1 đến {MaxNameLength} ký tự.");
        if (weightG <= 0) throw new BusinessRuleException("Cân nặng phải lớn hơn 0.");
        if (isPreorder && preorderDays is < 7 or > 30) throw new BusinessRuleException("Hàng đặt trước cần từ 7 đến 30 ngày chuẩn bị.");

        var sensitive = Name != name.Trim() || CategoryId != categoryId;
        CategoryId = categoryId;
        BrandId = brandId;
        Name = name.Trim();
        Slug = slug;
        Description = sanitizedDescription;
        Condition = condition;
        WeightG = weightG;
        LengthMm = Math.Max(0, lengthMm);
        WidthMm = Math.Max(0, widthMm);
        HeightMm = Math.Max(0, heightMm);
        IsPreorder = isPreorder;
        PreorderDays = isPreorder ? preorderDays : 0;
        return sensitive;
    }

    public void SetAttributes(IEnumerable<(Guid AttributeId, IReadOnlyList<string> Values)> values)
    {
        _attributes.Clear();
        foreach (var (attributeId, list) in values)
        {
            var filled = list.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList();
            if (filled.Count > 0) _attributes.Add(new ProductAttribute(Id, attributeId, filled));
        }
    }

    /// <summary>Replace media. Returns true when the set of image URLs changed (re-review trigger).</summary>
    public bool SetMedia(IReadOnlyList<MediaSpec> media)
    {
        var images = media.Where(m => m.Type == MediaType.Image).ToList();
        var videos = media.Where(m => m.Type == MediaType.Video).ToList();
        if (images.Count == 0) throw new BusinessRuleException("Sản phẩm cần ít nhất 1 ảnh.");
        if (images.Count > MaxImages) throw new BusinessRuleException($"Sản phẩm có tối đa {MaxImages} ảnh.");
        if (videos.Count > 1) throw new BusinessRuleException("Sản phẩm có tối đa 1 video.");

        var before = _media.Where(m => m.Type == MediaType.Image).Select(m => m.Url).ToList();
        _media.Clear();
        var sort = 0;
        foreach (var m in images.Concat(videos))
        {
            var optionId = m.OptionValue is null ? null : _tiers.FirstOrDefault(t => t.TierIndex == 0)?.Options
                .FirstOrDefault(o => o.Value == m.OptionValue && o.IsActive)?.Id;
            _media.Add(new ProductMedia(Id, m.Type, m.AssetId, m.Url, sort++, optionId));
        }
        return !before.SequenceEqual(images.Select(i => i.Url));
    }

    /// <summary>
    /// Replace variant tiers and SKUs. Existing SKUs are matched by their option values and keep their identity
    /// (and reserved stock); combinations no longer offered are deactivated, never deleted.
    /// </summary>
    public IReadOnlyList<(Sku Sku, int StockDelta)> SetVariants(IReadOnlyList<TierSpec> tiers, IReadOnlyList<SkuSpec> skus)
    {
        if (tiers.Count > MaxTiers) throw new BusinessRuleException("Sản phẩm có tối đa 2 tầng phân loại.");
        foreach (var tier in tiers)
        {
            if (string.IsNullOrWhiteSpace(tier.Name)) throw new BusinessRuleException("Tên phân loại không được để trống.");
            if (tier.Options.Count is 0 or > MaxOptionsPerTier)
                throw new BusinessRuleException($"Mỗi tầng phân loại có từ 1 đến {MaxOptionsPerTier} lựa chọn.");
            if (tier.Options.Select(o => o.Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != tier.Options.Count)
                throw new BusinessRuleException($"Phân loại \"{tier.Name}\" có lựa chọn bị trùng.");
        }

        // Tiers & options: keep option ids for values that remain
        for (var i = 0; i < MaxTiers; i++)
        {
            var existing = _tiers.FirstOrDefault(t => t.TierIndex == i);
            if (i < tiers.Count)
            {
                if (existing is null)
                {
                    existing = new VariantTier(Id, i, tiers[i].Name);
                    _tiers.Add(existing);
                }
                existing.Sync(tiers[i]);
            }
            else
            {
                existing?.DeactivateAll();
            }
        }

        // Expected combinations
        var tier1 = tiers.Count > 0 ? tiers[0].Options.Select(o => (string?)o.Value.Trim()).ToList() : [null];
        var tier2 = tiers.Count > 1 ? tiers[1].Options.Select(o => (string?)o.Value.Trim()).ToList() : [null];
        var expected = (from a in tier1 from b in tier2 select (a, b)).ToList();

        var byCombo = skus.ToDictionary(s => (s.Option1?.Trim(), s.Option2?.Trim()));
        if (byCombo.Count != skus.Count) throw new BusinessRuleException("Bảng SKU có tổ hợp phân loại bị trùng.");
        foreach (var combo in expected)
            if (!byCombo.ContainsKey(combo))
                throw new BusinessRuleException($"Thiếu giá/tồn kho cho phân loại {Describe(combo.a, combo.b)}.");
        if (byCombo.Keys.Any(k => !expected.Contains(k)))
            throw new BusinessRuleException("Bảng SKU có tổ hợp không thuộc các phân loại đã khai báo.");

        var deltas = new List<(Sku, int)>();
        foreach (var (a, b) in expected)
        {
            var spec = byCombo[(a, b)];
            var option1 = a is null ? null : _tiers.Single(t => t.TierIndex == 0).Options.Single(o => o.Value == a && o.IsActive);
            var option2 = b is null ? null : _tiers.Single(t => t.TierIndex == 1).Options.Single(o => o.Value == b && o.IsActive);
            var sku = _skus.FirstOrDefault(s => s.Option1Id == option1?.Id && s.Option2Id == option2?.Id);
            if (sku is null)
            {
                sku = new Sku(Id, option1?.Id, option2?.Id);
                _skus.Add(sku);
            }
            sku.Update(spec.SellerSku, spec.Price, spec.OriginalPrice, spec.WeightG, spec.IsActive, spec.Size);
            var delta = sku.SetStockForEditor(spec.Stock);
            if (delta != 0) deltas.Add((sku, delta));
        }

        // Anything not offered any more stays (orders reference it) but cannot be bought
        foreach (var sku in _skus.Where(s => !expected.Any(e =>
                     MatchesOption(0, s.Option1Id, e.a) && MatchesOption(1, s.Option2Id, e.b))))
            sku.Deactivate();

        if (!_skus.Any(s => s.IsActive)) throw new BusinessRuleException("Sản phẩm cần ít nhất một phân loại đang bán.");
        RecomputePriceRange();
        return deltas;
    }

    private bool MatchesOption(int tierIndex, Guid? optionId, string? value)
    {
        if (optionId is null) return value is null;
        var option = _tiers.FirstOrDefault(t => t.TierIndex == tierIndex)?.Options.FirstOrDefault(o => o.Id == optionId);
        return option is not null && option.IsActive && option.Value == value;
    }

    public void RecomputePriceRange()
    {
        var active = _skus.Where(s => s.IsActive).ToList();
        MinPrice = active.Count == 0 ? 0 : active.Min(s => s.Price);
        MaxPrice = active.Count == 0 ? 0 : active.Max(s => s.Price);
    }

    private static string Describe(string? a, string? b) => string.Join(" / ", new[] { a, b }.Where(x => x is not null));

    // ---------- Lifecycle (the only place Status changes) ----------

    public void SubmitForReview(DateTimeOffset now, string? flags)
    {
        if (Status is not (ProductStatus.Draft or ProductStatus.Hidden or ProductStatus.Active or ProductStatus.PendingReview))
            throw new BusinessRuleException("Sản phẩm ở trạng thái hiện tại không thể gửi duyệt.");
        Status = ProductStatus.PendingReview;
        SubmittedAt = now;
        ReviewNote = null;
        Flags = flags;
    }

    public void Approve(DateTimeOffset now)
    {
        if (Status != ProductStatus.PendingReview) throw new BusinessRuleException("Chỉ duyệt được sản phẩm đang chờ duyệt.");
        Status = ProductStatus.Active;
        PublishedAt ??= now;
        Flags = null;
    }

    /// <summary>Send back to the seller with the changes to make.</summary>
    public void Reject(string note)
    {
        if (Status != ProductStatus.PendingReview) throw new BusinessRuleException("Chỉ từ chối được sản phẩm đang chờ duyệt.");
        if (string.IsNullOrWhiteSpace(note)) throw new BusinessRuleException("Cần ghi lý do yêu cầu sửa.");
        Status = ProductStatus.Draft;
        ReviewNote = note.Trim();
    }

    /// <summary>Approved content changed in a sensitive way: goes back to the review queue.</summary>
    public void RequireReReview(DateTimeOffset now)
    {
        if (Status is ProductStatus.Active or ProductStatus.Hidden) SubmitForReview(now, Flags);
    }

    public void Hide()
    {
        if (Status != ProductStatus.Active) throw new BusinessRuleException("Chỉ ẩn được sản phẩm đang bán.");
        Status = ProductStatus.Hidden;
    }

    public void Show()
    {
        if (Status != ProductStatus.Hidden) throw new BusinessRuleException("Chỉ hiện lại được sản phẩm đang ẩn.");
        Status = ProductStatus.Active;
    }

    public void Ban(string reason)
    {
        if (Status == ProductStatus.Deleted) throw new BusinessRuleException("Sản phẩm đã bị xoá.");
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Cần ghi lý do khoá.");
        Status = ProductStatus.Banned;
        BanReason = reason.Trim();
    }

    public void Unban()
    {
        if (Status != ProductStatus.Banned) throw new BusinessRuleException("Sản phẩm không bị khoá.");
        Status = ProductStatus.Hidden;
        BanReason = null;
    }

    public void MarkDeleted(DateTimeOffset now)
    {
        if (Status == ProductStatus.Banned) throw new BusinessRuleException("Không xoá được sản phẩm đang bị khoá vì vi phạm.");
        Status = ProductStatus.Deleted;
        DeletedAt = now;
    }

    public bool IsEditableBySeller => Status is not (ProductStatus.Banned or ProductStatus.Deleted);
}

public class VariantTier : Entity
{
    private readonly List<VariantOption> _options = [];

    private VariantTier() { }

    public VariantTier(Guid productId, int tierIndex, string name)
    {
        ProductId = productId;
        TierIndex = tierIndex;
        Name = name.Trim();
    }

    public Guid ProductId { get; private set; }
    public int TierIndex { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public IReadOnlyList<VariantOption> Options => _options;

    internal void Sync(TierSpec spec)
    {
        Name = spec.Name.Trim();
        var sort = 0;
        foreach (var o in spec.Options)
        {
            var value = o.Value.Trim();
            var existing = _options.FirstOrDefault(x => x.Value == value);
            if (existing is null)
            {
                existing = new VariantOption(Id, value);
                _options.Add(existing);
            }
            existing.Update(o.ImageUrl, sort++, active: true);
        }
        foreach (var gone in _options.Where(x => spec.Options.All(o => o.Value.Trim() != x.Value)))
            gone.Update(gone.ImageUrl, gone.SortOrder, active: false);
    }

    internal void DeactivateAll()
    {
        foreach (var o in _options) o.Update(o.ImageUrl, o.SortOrder, active: false);
    }
}

public class VariantOption : Entity
{
    private VariantOption() { }

    public VariantOption(Guid tierId, string value)
    {
        TierId = tierId;
        Value = value;
    }

    public Guid TierId { get; private set; }
    public string Value { get; private set; } = string.Empty;
    public string? ImageUrl { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    internal void Update(string? imageUrl, int sortOrder, bool active)
    {
        ImageUrl = imageUrl;
        SortOrder = sortOrder;
        IsActive = active;
    }
}

/// <summary>
/// Stock keeping unit. available = Stock − Reserved; the database enforces Stock ≥ Reserved ≥ 0. Reservations are
/// taken with a conditional UPDATE (Phase 5), never read-then-write.
/// </summary>
public class Sku : Entity
{
    private Sku() { }

    public Sku(Guid productId, Guid? option1Id, Guid? option2Id)
    {
        ProductId = productId;
        Option1Id = option1Id;
        Option2Id = option2Id;
    }

    public Guid ProductId { get; private set; }
    public Guid? Option1Id { get; private set; }
    public Guid? Option2Id { get; private set; }
    public string? SellerSku { get; private set; }
    public long Price { get; private set; }
    public long OriginalPrice { get; private set; }
    public int Stock { get; private set; }
    public int Reserved { get; private set; }
    public int? WeightG { get; private set; }
    // Package size of this variant (mm); null = the product's
    public int? LengthMm { get; private set; }
    public int? WidthMm { get; private set; }
    public int? HeightMm { get; private set; }
    public bool IsActive { get; private set; }
    public uint Version { get; private set; }

    public int Available => Stock - Reserved;

    public PackageSize? Size => LengthMm is { } l && WidthMm is { } w && HeightMm is { } h ? new PackageSize(l, w, h) : null;

    internal void Update(string? sellerSku, long price, long originalPrice, int? weightG, bool isActive, PackageSize? size = null)
    {
        if (size is { } s && (s.LengthMm is < 0 or > 5_000 || s.WidthMm is < 0 or > 5_000 || s.HeightMm is < 0 or > 5_000))
            throw new BusinessRuleException("Kích thước đóng gói mỗi chiều từ 0 đến 5.000 mm.");
        LengthMm = size?.LengthMm;
        WidthMm = size?.WidthMm;
        HeightMm = size?.HeightMm;
        if (price <= 0) throw new BusinessRuleException("Giá bán phải lớn hơn 0.");
        if (price > 1_000_000_000) throw new BusinessRuleException("Giá bán tối đa ₫1.000.000.000.");
        if (originalPrice < price) throw new BusinessRuleException("Giá gốc không được thấp hơn giá bán.");
        SellerSku = string.IsNullOrWhiteSpace(sellerSku) ? null : sellerSku.Trim();
        Price = price;
        OriginalPrice = originalPrice;
        WeightG = weightG;
        IsActive = isActive;
    }

    /// <summary>The editor sends absolute stock; returns the delta to journal in inventory_movements.</summary>
    internal int SetStockForEditor(int stock)
    {
        if (stock < 0) throw new BusinessRuleException("Tồn kho không được âm.");
        if (stock < Reserved) throw new BusinessRuleException($"Tồn kho không được nhỏ hơn số đang giữ cho đơn ({Reserved}).");
        var delta = stock - Stock;
        Stock = stock;
        return delta;
    }

    internal void Deactivate() => IsActive = false;
}

public class ProductMedia : Entity
{
    private ProductMedia() { }

    public ProductMedia(Guid productId, MediaType type, Guid? assetId, string url, int sortOrder, Guid? variantOptionId)
    {
        ProductId = productId;
        Type = type;
        AssetId = assetId;
        Url = url;
        SortOrder = sortOrder;
        VariantOptionId = variantOptionId;
    }

    public Guid ProductId { get; private set; }
    public MediaType Type { get; private set; }
    public Guid? AssetId { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public Guid? VariantOptionId { get; private set; }
}

public class ProductAttribute
{
    private ProductAttribute() { }

    public ProductAttribute(Guid productId, Guid attributeId, List<string> values)
    {
        ProductId = productId;
        AttributeId = attributeId;
        Values = values;
    }

    public Guid ProductId { get; private set; }
    public Guid AttributeId { get; private set; }
    public List<string> Values { get; private set; } = [];
}

public enum InventoryReason
{
    SellerEdit,
    SellerAdjust,
    Import,
    OrderReserve,
    OrderRelease,
    OrderShip,
    ReturnRestock,
    Seed,
}

// Journal of every stock/reservation change on a SKU
public class InventoryMovement : Entity
{
    private InventoryMovement() { }

    public InventoryMovement(Guid skuId, int deltaStock, int deltaReserved, InventoryReason reason, string? refType, Guid? refId,
        Guid? actorId, string? note, DateTimeOffset occurredAt)
    {
        SkuId = skuId;
        DeltaStock = deltaStock;
        DeltaReserved = deltaReserved;
        Reason = reason;
        RefType = refType;
        RefId = refId;
        ActorId = actorId;
        Note = note;
        OccurredAt = occurredAt;
    }

    public Guid SkuId { get; private set; }
    public int DeltaStock { get; private set; }
    public int DeltaReserved { get; private set; }
    public InventoryReason Reason { get; private set; }
    public string? RefType { get; private set; }
    public Guid? RefId { get; private set; }
    public Guid? ActorId { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}

public enum ProductReportReason
{
    Counterfeit,      // hàng giả, hàng nhái
    Prohibited,       // hàng cấm
    WrongInfo,        // thông tin sai lệch
    Offensive,        // nội dung phản cảm
    IntellectualProperty, // vi phạm sở hữu trí tuệ
    Other,
}

public enum ProductReportStatus
{
    Open,
    Banned,     // product locked because of the report
    Dismissed,  // nothing wrong found
}

/// <summary>A buyer reporting a product (spec II.4, VI.4); one open report per reporter + product.</summary>
public class ProductReport : Entity
{
    private ProductReport() { }

    public ProductReport(Guid productId, Guid reporterId, ProductReportReason reason, string? details, DateTimeOffset now)
    {
        ProductId = productId;
        ReporterId = reporterId;
        Reason = reason;
        Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        Status = ProductReportStatus.Open;
        CreatedAt = now;
    }

    public Guid ProductId { get; private set; }
    public Guid ReporterId { get; private set; }
    public ProductReportReason Reason { get; private set; }
    public string? Details { get; private set; }
    public ProductReportStatus Status { get; private set; }
    public Guid? HandledBy { get; private set; }
    public string? Resolution { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? HandledAt { get; private set; }

    public void Resolve(ProductReportStatus outcome, Guid adminId, string? resolution, DateTimeOffset now)
    {
        if (Status != ProductReportStatus.Open) throw new BusinessRuleException("Báo cáo này đã được xử lý.");
        if (outcome == ProductReportStatus.Open) throw new BusinessRuleException("Kết quả xử lý không hợp lệ.");
        Status = outcome;
        HandledBy = adminId;
        Resolution = string.IsNullOrWhiteSpace(resolution) ? null : resolution.Trim();
        HandledAt = now;
    }
}
