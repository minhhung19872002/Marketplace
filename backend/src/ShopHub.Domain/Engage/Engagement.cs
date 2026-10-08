using ShopHub.Domain.Common;

namespace ShopHub.Domain.Engage;

// A product saved by a user (unique per user + product)
public class Wishlist : Entity
{
    private Wishlist() { }

    public Wishlist(Guid userId, Guid productId, DateTimeOffset createdAt)
    {
        UserId = userId;
        ProductId = productId;
        CreatedAt = createdAt;
    }

    public Guid UserId { get; private set; }
    public Guid ProductId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    // When the reminders job last saw the product sold out — "có hàng lại" is told once it is back
    public DateTimeOffset? SoldOutSeenAt { get; private set; }

    public void SeenSoldOut(DateTimeOffset at) => SoldOutSeenAt ??= at;

    public void BackInStock() => SoldOutSeenAt = null;
}

// A user following a shop (unique per shop + user)
public class ShopFollower : Entity
{
    private ShopFollower() { }

    public ShopFollower(Guid shopId, Guid userId, DateTimeOffset createdAt)
    {
        ShopId = shopId;
        UserId = userId;
        CreatedAt = createdAt;
    }

    public Guid ShopId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>
/// One product view. Signed-in users are keyed by user id, guests by an anonymous session key; repeat views by the
/// same viewer within 30 minutes are not recorded (no inflated view counts).
/// </summary>
public class ProductView : Entity
{
    private ProductView() { }

    public ProductView(Guid? userId, string? sessionKey, Guid productId, DateTimeOffset viewedAt, ViewSource source = ViewSource.Direct)
    {
        UserId = userId;
        SessionKey = sessionKey;
        ProductId = productId;
        ViewedAt = viewedAt;
        Source = source;
    }

    // Where the viewer came from (seller analytics: nguồn truy cập)
    public ViewSource Source { get; private set; }
    /// <summary>
    /// Set when the viewer clears "Đã xem gần đây" (G2-B1): the row leaves the buyer's history but still counts for the
    /// shop's views and conversion funnel.
    /// </summary>
    public DateTimeOffset? HiddenAt { get; private set; }

    public Guid? UserId { get; private set; }
    public string? SessionKey { get; private set; }
    public Guid ProductId { get; private set; }
    public DateTimeOffset ViewedAt { get; private set; }
}

public enum ViewSource
{
    Direct,    // typed / bookmarked / no referrer
    Home,      // trang chủ, gợi ý hôm nay
    Search,    // trang tìm kiếm
    Category,  // trang danh mục
    Shop,      // trang shop
    Campaign,  // trang sự kiện, Flash Sale
    External,  // another site (social, ads)
    Other,
}

// Search queries for "từ khoá hot" and suggestions (keyword stored folded: lower-case, no tones)
public class SearchLog : Entity
{
    private SearchLog() { }

    public SearchLog(string keyword, Guid? userId, int resultCount, DateTimeOffset occurredAt, string? displayKeyword = null)
    {
        Keyword = keyword;
        DisplayKeyword = displayKeyword;
        UserId = userId;
        ResultCount = resultCount;
        OccurredAt = occurredAt;
    }

    /// <summary>Folded form (no accents, lower case) — what searches are grouped by.</summary>
    public string Keyword { get; private set; } = string.Empty;
    /// <summary>As typed (trimmed, single spaces): the accented form shown in "từ khoá hot" / suggestions.</summary>
    public string? DisplayKeyword { get; private set; }
    public Guid? UserId { get; private set; }
    public int ResultCount { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>Daily check-in (điểm danh) of a buyer: one row per Vietnam calendar day; the streak gives the xu reward (spec VIII).</summary>
public class CheckIn : Entity
{
    private CheckIn() { }

    public CheckIn(Guid userId, DateOnly day, int streakDay, long coins, DateTimeOffset now)
    {
        UserId = userId;
        Day = day;
        StreakDay = streakDay;
        Coins = coins;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }
    public DateOnly Day { get; private set; }
    // 1..7: position in the 7-day cycle (missing a day starts again at 1)
    public int StreakDay { get; private set; }
    public long Coins { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>"Thêm vào giỏ" as an event (the cart itself only keeps the current state): the conversion funnel's second step.</summary>
public class CartAdd : Entity
{
    private CartAdd() { }

    public CartAdd(Guid? userId, string? sessionKey, Guid productId, Guid skuId, int quantity, DateTimeOffset addedAt)
    {
        UserId = userId;
        SessionKey = sessionKey;
        ProductId = productId;
        SkuId = skuId;
        Quantity = quantity;
        AddedAt = addedAt;
    }

    public Guid? UserId { get; private set; }
    public string? SessionKey { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid SkuId { get; private set; }
    public int Quantity { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }
}
