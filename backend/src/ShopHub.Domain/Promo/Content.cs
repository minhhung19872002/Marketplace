using System.Text.RegularExpressions;
using ShopHub.Domain.Common;

namespace ShopHub.Domain.Promo;

public enum BannerPosition
{
    HomeMain,   // carousel on the home page
    HomeSide,   // the two small banners beside it
    Shortcut,   // "lối tắt" icons under the banners (title + icon)
    Category,   // top of a category page
    Popup,      // home page popup (shown at most once per POPUP.FREQUENCY_HOURS)
}

/// <summary>Scheduled banner / shortcut / popup set by the platform (spec II.1, VI.6).</summary>
public class Banner : Entity
{
    private Banner() { }

    public Banner(BannerPosition position, string title, string imageUrl, string link, DateTimeOffset startAt, DateTimeOffset endAt, int sortOrder,
        Guid? categoryId, DateTimeOffset now)
    {
        Position = position;
        CreatedAt = now;
        Update(title, imageUrl, link, startAt, endAt, sortOrder, categoryId);
    }

    public BannerPosition Position { get; private set; }
    public string Title { get; private set; } = string.Empty;
    // Image URL — or, for a shortcut, an emoji icon (same convention as category icons)
    public string ImageUrl { get; private set; } = string.Empty;
    // Internal path ("/su-kien/10-10") or https link
    public string Link { get; private set; } = string.Empty;
    public Guid? CategoryId { get; private set; }
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }

    public void Update(string title, string imageUrl, string link, DateTimeOffset startAt, DateTimeOffset endAt, int sortOrder, Guid? categoryId)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new BusinessRuleException("Vui lòng nhập tiêu đề.");
        if (string.IsNullOrWhiteSpace(imageUrl)) throw new BusinessRuleException("Vui lòng chọn ảnh.");
        if (!IsSafeLink(link)) throw new BusinessRuleException("Liên kết phải là đường dẫn trong sàn (/...) hoặc https://.");
        if (endAt <= startAt) throw new BusinessRuleException("Thời gian kết thúc phải sau thời gian bắt đầu.");
        Title = title.Trim();
        ImageUrl = imageUrl.Trim();
        Link = link.Trim();
        StartAt = startAt.ToUniversalTime();
        EndAt = endAt.ToUniversalTime();
        SortOrder = sortOrder;
        CategoryId = Position == BannerPosition.Category ? categoryId : null;
    }

    public void SetActive(bool active) => IsActive = active;

    // No javascript: / data: links on the storefront
    public static bool IsSafeLink(string? link) =>
        !string.IsNullOrWhiteSpace(link) && (Regex.IsMatch(link, "^/[^/\\\\]") || link == "/" || link.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
}

public enum CampaignBlockType
{
    Banner,     // a banner image with a link
    Vouchers,   // platform vouchers to claim (codes)
    FlashSale,  // the running flash sale slot
    Products,   // a product grid from search criteria (keyword / category / price)
}

/// <summary>One block of a campaign landing page (stored as jsonb on the campaign).</summary>
public record CampaignBlock(CampaignBlockType Type, string? Title, string? ImageUrl, string? Link, IReadOnlyList<string>? VoucherCodes, string? Keyword,
    Guid? CategoryId, long? MaxPrice, int? Limit);

/// <summary>Ngày hội mua sắm (9.9, 10.10…): a landing page /su-kien/{slug} built from blocks (spec II.13).</summary>
public class Campaign : Entity
{
    private Campaign() { }

    public Campaign(string name, string slug, DateTimeOffset startAt, DateTimeOffset endAt, IReadOnlyList<CampaignBlock> blocks, DateTimeOffset now)
    {
        CreatedAt = now;
        Update(name, slug, startAt, endAt, blocks);
    }

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }
    public List<CampaignBlock> Blocks { get; private set; } = [];
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }

    public void Update(string name, string slug, DateTimeOffset startAt, DateTimeOffset endAt, IReadOnlyList<CampaignBlock> blocks)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BusinessRuleException("Vui lòng nhập tên chiến dịch.");
        if (!Regex.IsMatch(slug ?? "", "^[a-z0-9]+(-[a-z0-9]+)*$")) throw new BusinessRuleException("Đường dẫn chỉ gồm chữ thường không dấu, số và dấu gạch ngang.");
        if (endAt <= startAt) throw new BusinessRuleException("Thời gian kết thúc phải sau thời gian bắt đầu.");
        if (blocks.Count is 0 or > 20) throw new BusinessRuleException("Trang chiến dịch có từ 1 đến 20 khối.");
        if (blocks.Any(b => b.Link is not null && !Banner.IsSafeLink(b.Link)))
            throw new BusinessRuleException("Liên kết phải là đường dẫn trong sàn (/...) hoặc https://.");
        Name = name.Trim();
        Slug = slug!;
        StartAt = startAt.ToUniversalTime();
        EndAt = endAt.ToUniversalTime();
        Blocks = blocks.ToList();
    }

    public void SetActive(bool active) => IsActive = active;
}
