using ShopHub.Domain.Common;

namespace ShopHub.Domain.Shops;

/// <summary>Danh mục của shop (III.9): the shop's own grouping of its products, shown as tabs on the shop page.</summary>
public class ShopCategory : AuditableEntity
{
    public const int MaxPerShop = 30;
    public const int MaxNameLength = 40;

    private ShopCategory() { }

    public ShopCategory(Guid shopId, string name, int sortOrder)
    {
        ShopId = shopId;
        Rename(name);
        SortOrder = sortOrder;
    }

    public Guid ShopId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public bool IsVisible { get; private set; } = true;

    public void Rename(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Length > MaxNameLength)
            throw new BusinessRuleException($"Tên danh mục 1–{MaxNameLength} ký tự.");
        Name = name;
    }

    public void Update(string name, int sortOrder, bool isVisible)
    {
        Rename(name);
        SortOrder = sortOrder;
        IsVisible = isVisible;
    }
}

/// <summary>A product placed in a shop category (unique per category + product).</summary>
public class ShopCategoryProduct : Entity
{
    private ShopCategoryProduct() { }

    public ShopCategoryProduct(Guid shopCategoryId, Guid productId, int sortOrder)
    {
        ShopCategoryId = shopCategoryId;
        ProductId = productId;
        SortOrder = sortOrder;
    }

    public Guid ShopCategoryId { get; private set; }
    public Guid ProductId { get; private set; }
    public int SortOrder { get; private set; }
}

public enum DecorationBlockType
{
    Banner,     // 1–5 images, each with an optional link inside the site (a slideshow)
    Products,   // up to 12 hand-picked products of the shop
    Category,   // the first products of one of the shop's categories
    Video,      // one uploaded MP4
    Text,       // a plain-text paragraph (no HTML)
}

public record DecorationImage(string Url, string? Link);

/// <summary>One block of the shop's "Dạo" tab (stored as jsonb on the decoration, in display order).</summary>
public record DecorationBlock(DecorationBlockType Type, string? Title, IReadOnlyList<DecorationImage>? Images,
    IReadOnlyList<Guid>? ProductIds, Guid? ShopCategoryId, string? VideoUrl, string? Text);

/// <summary>Trang trí shop: the ordered blocks of the shop home ("Dạo"); one row per shop.</summary>
public class ShopDecoration : AuditableEntity
{
    public const int MaxBlocks = 20;
    public const int MaxBannerImages = 5;
    public const int MaxProducts = 12;
    public const int MaxTextLength = 1000;

    private ShopDecoration() { }

    public ShopDecoration(Guid shopId) => ShopId = shopId;

    public Guid ShopId { get; private set; }
    public List<DecorationBlock> Blocks { get; private set; } = [];
    public DateTimeOffset? PublishedAt { get; private set; }

    public void Publish(IReadOnlyList<DecorationBlock> blocks, DateTimeOffset now)
    {
        if (blocks.Count > MaxBlocks) throw new BusinessRuleException($"Tối đa {MaxBlocks} khối trang trí.");
        for (var i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            var at = $"Khối {i + 1}: ";
            switch (b.Type)
            {
                case DecorationBlockType.Banner when b.Images is not { Count: > 0 } || b.Images.Count > MaxBannerImages:
                    throw new BusinessRuleException($"{at}banner cần 1–{MaxBannerImages} ảnh.");
                case DecorationBlockType.Banner when b.Images.Any(img => img.Link is { } link && !IsSiteLink(link)):
                    throw new BusinessRuleException($"{at}liên kết của banner phải là một trang trong ShopHub (bắt đầu bằng /).");
                case DecorationBlockType.Products when b.ProductIds is not { Count: > 0 } || b.ProductIds.Count > MaxProducts:
                    throw new BusinessRuleException($"{at}chọn 1–{MaxProducts} sản phẩm.");
                case DecorationBlockType.Category when b.ShopCategoryId is null:
                    throw new BusinessRuleException($"{at}chọn một danh mục của shop.");
                case DecorationBlockType.Video when string.IsNullOrWhiteSpace(b.VideoUrl):
                    throw new BusinessRuleException($"{at}chưa có video.");
                case DecorationBlockType.Text when string.IsNullOrWhiteSpace(b.Text) || b.Text.Length > MaxTextLength:
                    throw new BusinessRuleException($"{at}nội dung chữ 1–{MaxTextLength} ký tự.");
            }
            if (b.Title is { Length: > 60 }) throw new BusinessRuleException($"{at}tiêu đề tối đa 60 ký tự.");
        }
        Blocks = blocks.ToList();
        PublishedAt = now;
    }

    // Only paths inside the site: no scheme, no protocol-relative "//host"
    private static bool IsSiteLink(string link) => link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal);
}
