using ShopHub.Domain.Common;

namespace ShopHub.Domain.Promo;

public enum PromotionType
{
    Discount,  // GIẢM GIÁ — a lower price per SKU for a period
    Combo,     // COMBO — buy N of the listed products, get a % / amount off them
    AddOn,     // MUA KÈM DEAL SỐC — with a main product, add-on SKUs at a special price
    Gift,      // QUÀ TẶNG — spend X on the listed products, get a gift SKU free
}

public enum PromotionStatus
{
    Active,
    Stopped,
}

/// <summary>
/// A shop marketing programme (spec 3.10, III.5). Prices of "Discount" SKUs also go into
/// <see cref="PriceProgram"/>, where one SKU can only be in one price programme at a time (database exclusion
/// constraint). Combo / add-on / gift rules are read by the checkout (spec 3.6: "ưu đãi combo / mua kèm").
/// </summary>
public class Promotion : Entity
{
    private Promotion() { }

    public Promotion(Guid shopId, PromotionType type, string name, DateTimeOffset startAt, DateTimeOffset endAt, Guid createdBy, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BusinessRuleException("Vui lòng nhập tên chương trình.");
        if (endAt <= startAt) throw new BusinessRuleException("Thời gian kết thúc phải sau thời gian bắt đầu.");
        if (endAt - startAt > TimeSpan.FromDays(180)) throw new BusinessRuleException("Mỗi chương trình kéo dài tối đa 180 ngày.");
        if (endAt <= now) throw new BusinessRuleException("Thời gian kết thúc đã qua.");
        ShopId = shopId;
        Type = type;
        Name = name.Trim();
        StartAt = startAt.ToUniversalTime();
        EndAt = endAt.ToUniversalTime();
        CreatedBy = createdBy;
        CreatedAt = now;
        Status = PromotionStatus.Active;
    }

    public Guid ShopId { get; private set; }
    public PromotionType Type { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }
    public PromotionStatus Status { get; private set; }

    // Combo: buy at least MinQuantity units of the listed products → DiscountBp % off (or DiscountAmount) on them
    public int MinQuantity { get; private set; }
    public int DiscountBp { get; private set; }
    public long DiscountAmount { get; private set; }

    // Add-on: at most MaxAddOnQuantity add-on units per order, only with a main product in the same order
    public int MaxAddOnQuantity { get; private set; }

    // Gift: spend at least MinSpend on the main products → GiftQuantity × the gift SKU free
    public long MinSpend { get; private set; }
    public Guid? GiftSkuId { get; private set; }
    public int GiftQuantity { get; private set; }

    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StoppedAt { get; private set; }
    public uint Version { get; private set; }

    public List<PromotionProduct> Products { get; private set; } = [];
    public List<PromotionSku> Skus { get; private set; } = [];

    public bool IsRunning(DateTimeOffset at) => Status == PromotionStatus.Active && StartAt <= at && at < EndAt;

    public void ConfigureCombo(int minQuantity, int discountBp, long discountAmount)
    {
        if (Type != PromotionType.Combo) throw new InvalidOperationException("Not a combo.");
        if (minQuantity is < 2 or > 100) throw new BusinessRuleException("Combo cần mua từ 2 đến 100 sản phẩm.");
        if ((discountBp > 0) == (discountAmount > 0)) throw new BusinessRuleException("Combo giảm theo % hoặc theo số tiền (chọn một).");
        if (discountBp is < 0 or > 9_000) throw new BusinessRuleException("Combo giảm tối đa 90%.");
        if (discountAmount < 0) throw new BusinessRuleException("Số tiền giảm không hợp lệ.");
        MinQuantity = minQuantity;
        DiscountBp = discountBp;
        DiscountAmount = discountAmount;
    }

    public void ConfigureAddOn(int maxAddOnQuantity)
    {
        if (Type != PromotionType.AddOn) throw new InvalidOperationException("Not an add-on deal.");
        if (maxAddOnQuantity is < 1 or > 10) throw new BusinessRuleException("Mỗi đơn mua kèm từ 1 đến 10 sản phẩm.");
        MaxAddOnQuantity = maxAddOnQuantity;
    }

    public void ConfigureGift(long minSpend, Guid giftSkuId, int giftQuantity)
    {
        if (Type != PromotionType.Gift) throw new InvalidOperationException("Not a gift programme.");
        if (minSpend < 1_000) throw new BusinessRuleException("Giá trị đơn tối thiểu để nhận quà từ ₫1.000.");
        if (giftQuantity is < 1 or > 5) throw new BusinessRuleException("Mỗi đơn tặng từ 1 đến 5 quà.");
        MinSpend = minSpend;
        GiftSkuId = giftSkuId;
        GiftQuantity = giftQuantity;
    }

    public void Stop(DateTimeOffset now)
    {
        if (Status == PromotionStatus.Stopped) return;
        Status = PromotionStatus.Stopped;
        StoppedAt = now;
    }

    public static string Label(PromotionType type) => type switch
    {
        PromotionType.Discount => "Chương trình giảm giá",
        PromotionType.Combo => "Combo khuyến mãi",
        PromotionType.AddOn => "Mua kèm deal sốc",
        PromotionType.Gift => "Quà tặng kèm",
        _ => type.ToString(),
    };
}

/// <summary>Products a combo / add-on (main products) / gift programme applies to.</summary>
public class PromotionProduct : Entity
{
    private PromotionProduct() { }

    public PromotionProduct(Guid promotionId, Guid productId)
    {
        PromotionId = promotionId;
        ProductId = productId;
    }

    public Guid PromotionId { get; private set; }
    public Guid ProductId { get; private set; }
}

/// <summary>A SKU with its programme price: the discounted price ("Discount") or the add-on price ("AddOn").</summary>
public class PromotionSku : Entity
{
    private PromotionSku() { }

    public PromotionSku(Guid promotionId, Guid skuId, long price, int? perUserLimit)
    {
        if (price < 1_000) throw new BusinessRuleException("Giá khuyến mãi tối thiểu ₫1.000.");
        if (perUserLimit is < 1) throw new BusinessRuleException("Giới hạn mua mỗi người phải từ 1.");
        PromotionId = promotionId;
        SkuId = skuId;
        Price = price;
        PerUserLimit = perUserLimit;
    }

    public Guid PromotionId { get; private set; }
    public Guid SkuId { get; private set; }
    public long Price { get; private set; }
    public int? PerUserLimit { get; private set; }
}

public enum PriceProgramKind
{
    Discount,       // shop discount programme
    ShopFlash,      // shop flash sale
    PlatformFlash,  // platform flash sale (registered by the shop, approved by the platform)
}

/// <summary>
/// The price a SKU sells at during [StartAt, EndAt). EXCLUDE USING gist (sku_id =, tstzrange(start_at, end_at) &&)
/// WHERE is_active: a SKU can never be in two price programmes at the same moment (spec 3.10, 6.2).
/// </summary>
public class PriceProgram : Entity
{
    private PriceProgram() { }

    public PriceProgram(Guid skuId, Guid shopId, PriceProgramKind kind, Guid refId, long price, DateTimeOffset startAt, DateTimeOffset endAt)
    {
        if (endAt <= startAt) throw new BusinessRuleException("Thời gian kết thúc phải sau thời gian bắt đầu.");
        SkuId = skuId;
        ShopId = shopId;
        Kind = kind;
        RefId = refId;
        Price = price;
        StartAt = startAt.ToUniversalTime();
        EndAt = endAt.ToUniversalTime();
        IsActive = true;
    }

    public Guid SkuId { get; private set; }
    public Guid ShopId { get; private set; }
    public PriceProgramKind Kind { get; private set; }
    // PromotionSku.PromotionId for a discount, FlashSaleItem.Id for a flash sale
    public Guid RefId { get; private set; }
    public long Price { get; private set; }
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }
    public bool IsActive { get; private set; }

    public void Deactivate() => IsActive = false;
}

public enum FlashSaleOwner
{
    Platform,
    Shop,
}

public enum FlashSlotStatus
{
    Open,      // shops may register (platform) / items listed (shop)
    Cancelled,
}

/// <summary>
/// A Flash Sale time slot (spec 3.10). Platform slots carry criteria shops must meet to register; a shop's own slot
/// belongs to that shop and its items are live at once.
/// </summary>
public class FlashSaleSlot : Entity
{
    private FlashSaleSlot() { }

    public FlashSaleSlot(FlashSaleOwner owner, Guid? shopId, DateTimeOffset startAt, DateTimeOffset endAt, int minDiscountBp, double minRating,
        IReadOnlyList<Guid> categoryIds, DateTimeOffset now)
    {
        if (endAt <= startAt) throw new BusinessRuleException("Khung giờ kết thúc phải sau khi bắt đầu.");
        if (endAt - startAt > TimeSpan.FromHours(24)) throw new BusinessRuleException("Một khung Flash Sale dài tối đa 24 giờ.");
        if ((owner == FlashSaleOwner.Shop) != shopId.HasValue) throw new InvalidOperationException("Shop slots need a shop, platform slots none.");
        if (minDiscountBp is < 0 or > 9_000) throw new BusinessRuleException("Mức giảm tối thiểu không hợp lệ.");
        if (minRating is < 0 or > 5) throw new BusinessRuleException("Điểm đánh giá tối thiểu từ 0 đến 5.");
        Owner = owner;
        ShopId = shopId;
        StartAt = startAt.ToUniversalTime();
        EndAt = endAt.ToUniversalTime();
        MinDiscountBp = minDiscountBp;
        MinRating = minRating;
        CategoryIds = categoryIds.Distinct().ToList();
        Status = FlashSlotStatus.Open;
        CreatedAt = now;
    }

    public FlashSaleOwner Owner { get; private set; }
    public Guid? ShopId { get; private set; }
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }
    // Criteria (platform slots): flash price at least this % below the normal price, product rating, categories (empty = all)
    public int MinDiscountBp { get; private set; }
    public double MinRating { get; private set; }
    public List<Guid> CategoryIds { get; private set; } = [];
    public FlashSlotStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsRunning(DateTimeOffset at) => Status == FlashSlotStatus.Open && StartAt <= at && at < EndAt;

    public void Cancel() => Status = FlashSlotStatus.Cancelled;
}

public enum FlashItemStatus
{
    Pending,   // CHỜ duyệt
    Approved,  // DUYỆT
    Rejected,  // TỪ CHỐI
}

/// <summary>One SKU in a Flash Sale slot: flash price, quota (suất), sold, per-buyer limit.</summary>
public class FlashSaleItem : Entity
{
    private FlashSaleItem() { }

    public FlashSaleItem(Guid slotId, Guid skuId, Guid productId, Guid shopId, long flashPrice, int quota, int perUserLimit, DateTimeOffset now)
    {
        if (flashPrice < 1_000) throw new BusinessRuleException("Giá Flash Sale tối thiểu ₫1.000.");
        if (quota is < 1 or > 100_000) throw new BusinessRuleException("Số suất từ 1 đến 100.000.");
        if (perUserLimit is < 1 or > 100) throw new BusinessRuleException("Giới hạn mỗi người từ 1 đến 100.");
        SlotId = slotId;
        SkuId = skuId;
        ProductId = productId;
        ShopId = shopId;
        FlashPrice = flashPrice;
        Quota = quota;
        PerUserLimit = perUserLimit;
        Status = FlashItemStatus.Pending;
        CreatedAt = now;
    }

    public Guid SlotId { get; private set; }
    public Guid SkuId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid ShopId { get; private set; }
    public long FlashPrice { get; private set; }
    public int Quota { get; private set; }
    // Units taken by placed orders (conditional UPDATE sold + n <= quota — the database backstop of the Redis counter)
    public int Sold { get; private set; }
    public int PerUserLimit { get; private set; }
    public FlashItemStatus Status { get; private set; }
    public string? RejectReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    public void Approve(DateTimeOffset now)
    {
        if (Status != FlashItemStatus.Pending) throw new BusinessRuleException("Sản phẩm đăng ký này đã được xử lý.");
        Status = FlashItemStatus.Approved;
        DecidedAt = now;
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        if (Status != FlashItemStatus.Pending) throw new BusinessRuleException("Sản phẩm đăng ký này đã được xử lý.");
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Vui lòng nhập lý do từ chối.");
        Status = FlashItemStatus.Rejected;
        RejectReason = reason.Trim();
        DecidedAt = now;
    }
}

/// <summary>How many units of a flash item one buyer took (per-buyer limit, enforced by a conditional upsert).</summary>
public class FlashSaleBuyer : Entity
{
    private FlashSaleBuyer() { }

    public Guid ItemId { get; private set; }
    public Guid UserId { get; private set; }
    public int Quantity { get; private set; }
}
