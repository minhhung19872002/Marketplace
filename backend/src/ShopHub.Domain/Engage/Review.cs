using ShopHub.Domain.Common;

namespace ShopHub.Domain.Engage;

/// <summary>
/// One review per order line (spec 3.11), written by the buyer of a completed order within the review window.
/// The text may be edited once within 30 days; the shop replies once; admins hide reviews that break the rules.
/// </summary>
public class Review : Entity, ISoftDeletable
{
    public const int MaxImages = 6;
    public const int MaxContentLength = 1000;

    public static readonly IReadOnlyList<string> AllowedTags =
        ["Đúng mô tả", "Chất lượng tốt", "Giao hàng nhanh", "Đóng gói cẩn thận", "Shop phục vụ tốt", "Đáng đồng tiền"];

    private Review() { }

    public Review(Guid orderItemId, Guid orderId, Guid productId, Guid skuId, Guid shopId, Guid buyerId, string? variantSnapshot, DateTimeOffset now)
    {
        OrderItemId = orderItemId;
        OrderId = orderId;
        ProductId = productId;
        SkuId = skuId;
        ShopId = shopId;
        BuyerId = buyerId;
        VariantSnapshot = variantSnapshot;
        CreatedAt = now;
    }

    public Guid OrderItemId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid SkuId { get; private set; }
    public Guid ShopId { get; private set; }
    public Guid BuyerId { get; private set; }
    public string? VariantSnapshot { get; private set; }
    public int Rating { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public List<string> Tags { get; private set; } = [];
    public bool IsAnonymous { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? EditedAt { get; private set; }
    public string? SellerReply { get; private set; }
    public DateTimeOffset? RepliedAt { get; private set; }
    public Guid? RepliedBy { get; private set; }
    public bool IsHidden { get; private set; }
    public string? HiddenReason { get; private set; }
    public bool Rewarded { get; private set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public List<ReviewMedia> Media { get; private set; } = [];

    public void Write(int rating, string? content, IReadOnlyList<string> tags, bool anonymous)
    {
        if (rating is < 1 or > 5) throw new BusinessRuleException("Vui lòng chọn từ 1 đến 5 sao.");
        var text = (content ?? string.Empty).Trim();
        if (text.Length > MaxContentLength) throw new BusinessRuleException($"Nội dung đánh giá tối đa {MaxContentLength} ký tự.");
        var unknown = tags.Except(AllowedTags).ToList();
        if (unknown.Count > 0) throw new BusinessRuleException($"Thẻ đánh giá không hợp lệ: {string.Join(", ", unknown)}.");
        Rating = rating;
        Content = text;
        Tags = tags.Distinct().ToList();
        IsAnonymous = anonymous;
    }

    public void SetMedia(IReadOnlyList<(ReviewMediaType Type, Guid? AssetId, string Url)> media)
    {
        if (media.Count(m => m.Type == ReviewMediaType.Image) > MaxImages) throw new BusinessRuleException($"Tối đa {MaxImages} ảnh cho một đánh giá.");
        if (media.Count(m => m.Type == ReviewMediaType.Video) > 1) throw new BusinessRuleException("Tối đa 1 video cho một đánh giá.");
        Media.Clear();
        var order = 0;
        foreach (var m in media) Media.Add(new ReviewMedia(Id, m.Type, m.AssetId, m.Url, order++));
    }

    /// <summary>Editing is allowed once, within <paramref name="editDays"/> of writing.</summary>
    public void EnsureEditable(int editDays, DateTimeOffset now)
    {
        if (EditedAt is not null) throw new BusinessRuleException("Mỗi đánh giá chỉ được sửa một lần.");
        if (now > CreatedAt.AddDays(editDays)) throw new BusinessRuleException($"Chỉ sửa được đánh giá trong {editDays} ngày sau khi viết.");
    }

    public void MarkEdited(DateTimeOffset now) => EditedAt = now;

    public void Reply(string text, Guid by, DateTimeOffset now)
    {
        if (SellerReply is not null) throw new BusinessRuleException("Shop chỉ trả lời mỗi đánh giá một lần.");
        var reply = text?.Trim() ?? string.Empty;
        if (reply.Length is 0 or > 500) throw new BusinessRuleException("Nội dung trả lời từ 1 đến 500 ký tự.");
        SellerReply = reply;
        RepliedAt = now;
        RepliedBy = by;
    }

    public void Hide(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Ẩn đánh giá cần có lý do.");
        IsHidden = true;
        HiddenReason = reason.Trim();
    }

    public void Unhide()
    {
        IsHidden = false;
        HiddenReason = null;
    }

    /// <summary>Enough text and at least one photo or video earn the review reward (once).</summary>
    public bool QualifiesForReward(int minChars) => Content.Length >= minChars && Media.Count > 0;

    public void MarkRewarded() => Rewarded = true;

    public void RevokeReward() => Rewarded = false;

    /// <summary>"ng****an" style display name (spec 3.11), or the anonymous label.</summary>
    public static string MaskName(string fullName, bool anonymous)
    {
        if (anonymous) return "Người mua ẩn danh";
        var compact = new string((fullName ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
        if (compact.Length <= 2) return compact.Length == 0 ? "người mua" : $"{compact[0]}****";
        return $"{compact[..2]}****{compact[^2..]}";
    }
}

public enum ReviewMediaType
{
    Image,
    Video,
}

public class ReviewMedia : Entity
{
    private ReviewMedia() { }

    internal ReviewMedia(Guid reviewId, ReviewMediaType type, Guid? assetId, string url, int sortOrder)
    {
        ReviewId = reviewId;
        Type = type;
        AssetId = assetId;
        Url = url;
        SortOrder = sortOrder;
    }

    public Guid ReviewId { get; private set; }
    public ReviewMediaType Type { get; private set; }
    public Guid? AssetId { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
}

public enum ReviewReportStatus
{
    Pending,
    Upheld,     // the review was hidden
    Dismissed,
}

/// <summary>Someone reports a review that breaks the rules; one report per person per review.</summary>
/// <summary>"Hữu ích" on a review (G2-B2): one vote per buyer and review, guaranteed by the database.</summary>
public class ReviewHelpfulVote : Entity
{
    private ReviewHelpfulVote() { }

    public ReviewHelpfulVote(Guid reviewId, Guid userId, DateTimeOffset now)
    {
        ReviewId = reviewId;
        UserId = userId;
        CreatedAt = now;
    }

    public Guid ReviewId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public class ReviewReport : Entity
{
    private ReviewReport() { }

    public ReviewReport(Guid reviewId, Guid reporterId, string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Vui lòng chọn lý do báo cáo.");
        ReviewId = reviewId;
        ReporterId = reporterId;
        Reason = reason.Trim();
        CreatedAt = now;
        Status = ReviewReportStatus.Pending;
    }

    public Guid ReviewId { get; private set; }
    public Guid ReporterId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public ReviewReportStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? ResolvedBy { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public void Resolve(bool upheld, Guid by, DateTimeOffset now)
    {
        if (Status != ReviewReportStatus.Pending) return;
        Status = upheld ? ReviewReportStatus.Upheld : ReviewReportStatus.Dismissed;
        ResolvedBy = by;
        ResolvedAt = now;
    }
}
