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

    public ProductView(Guid? userId, string? sessionKey, Guid productId, DateTimeOffset viewedAt)
    {
        UserId = userId;
        SessionKey = sessionKey;
        ProductId = productId;
        ViewedAt = viewedAt;
    }

    public Guid? UserId { get; private set; }
    public string? SessionKey { get; private set; }
    public Guid ProductId { get; private set; }
    public DateTimeOffset ViewedAt { get; private set; }
}

// Search queries for "từ khoá hot" and suggestions (keyword stored folded: lower-case, no tones)
public class SearchLog : Entity
{
    private SearchLog() { }

    public SearchLog(string keyword, Guid? userId, int resultCount, DateTimeOffset occurredAt)
    {
        Keyword = keyword;
        UserId = userId;
        ResultCount = resultCount;
        OccurredAt = occurredAt;
    }

    public string Keyword { get; private set; } = string.Empty;
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
