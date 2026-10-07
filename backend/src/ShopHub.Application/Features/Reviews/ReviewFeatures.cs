using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Media;
using ShopHub.Application.Features.Seller;
using ShopHub.Application.Features.Storefront;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Media;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Reviews;

public record ReviewMediaDto(ReviewMediaType Type, string Url);

public record ReviewDto(
    Guid Id,
    string ReviewerName,
    int Rating,
    string Content,
    IReadOnlyList<string> Tags,
    string? Variant,
    IReadOnlyList<ReviewMediaDto> Media,
    DateTimeOffset CreatedAt,
    bool Edited,
    string? SellerReply,
    DateTimeOffset? RepliedAt);

// Variants: how many reviews each bought variant has (filter "theo phân loại", spec 3.11)
public record ReviewSummaryDto(double Average, int Total, IReadOnlyDictionary<int, int> ByStar, int WithMedia, int WithComment,
    IReadOnlyList<ReviewVariantCountDto>? Variants = null);

public record ReviewVariantCountDto(string Variant, int Count);

public record ProductReviewsDto(ReviewSummaryDto Summary, PagedResult<ReviewDto> Reviews);

internal static class ReviewViews
{
    public static async Task<List<ReviewDto>> MapAsync(IApplicationDbContext db, IObjectStorage storage, IReadOnlyList<Review> reviews, CancellationToken ct)
    {
        var buyerIds = reviews.Select(r => r.BuyerId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => buyerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return reviews.Select(r => new ReviewDto(r.Id, Review.MaskName(names.GetValueOrDefault(r.BuyerId) ?? "", r.IsAnonymous), r.Rating, r.Content, r.Tags,
            r.VariantSnapshot, r.Media.OrderBy(m => m.SortOrder).Select(m => new ReviewMediaDto(m.Type, m.Url)).ToList(), r.CreatedAt, r.EditedAt is not null,
            r.SellerReply, r.RepliedAt)).ToList();
    }
}

/// <summary>Resolves uploaded review media that belongs to the buyer (purpose "review").</summary>
internal static class ReviewMediaResolver
{
    public static async Task<List<(ReviewMediaType, Guid?, string)>> ResolveAsync(IApplicationDbContext db, IObjectStorage storage, Guid buyerId,
        IReadOnlyList<Guid> assetIds, IEnumerable<ReviewMedia> current, CancellationToken ct)
    {
        var wanted = assetIds.Distinct().ToList();
        var keep = current.Where(m => m.AssetId is not null).Select(m => m.AssetId!.Value).ToHashSet();
        var assets = await db.MediaAssets.AsNoTracking()
            .Where(a => wanted.Contains(a.Id) && a.Purpose == "review" && (a.OwnerUserId == buyerId || keep.Contains(a.Id))).ToDictionaryAsync(a => a.Id, ct);
        if (assets.Count != wanted.Count) throw new NotFoundException("Không tìm thấy ảnh/video đã tải lên (hoặc tệp không thuộc về bạn).");
        return wanted.Select(id => assets[id]).Select(a => a.Kind == MediaKind.Video
            ? (ReviewMediaType.Video, (Guid?)a.Id, storage.PublicUrl(a.Bucket, a.ObjectKey))
            : (ReviewMediaType.Image, (Guid?)a.Id, storage.PublicUrl(a.Bucket, ImageSizes.Key(a.ObjectKey, ImageSizes.Large)))).ToList();
    }
}

/// <summary>Coin reward (once) for a review with enough text and a photo/video (spec 3.11).</summary>
public sealed class ReviewRewards(IApplicationDbContext db, ISystemParameters parameters, IClock clock)
{
    public async Task GrantIfDueAsync(Review review, CancellationToken ct)
    {
        if (review.Rewarded) return;
        // Once granted, never again — a reward taken back for a refund is not handed out on the next edit (L125)
        if (await db.CoinLedger.AnyAsync(c => c.RefType == "review" && c.RefId == review.Id && c.Reason == CoinReason.ReviewReward && c.Delta > 0, ct)) return;
        var min = (int)await parameters.GetIntAsync(ParameterKeys.ReviewRewardMinChars, ct);
        if (!review.QualifiesForReward(min)) return;
        var coins = await parameters.GetIntAsync(ParameterKeys.ReviewRewardCoins, ct);
        if (coins <= 0) return;
        var days = await parameters.GetIntAsync(ParameterKeys.CoinExpiryDays, ct);
        db.CoinLedger.Add(new CoinEntry(review.BuyerId, coins, CoinReason.ReviewReward, "review", review.Id, clock.UtcNow.AddDays(days),
            "Thưởng xu đánh giá sản phẩm", clock.UtcNow));
        review.MarkRewarded();
    }

    /// <summary>The reviewed line was refunded: take the reward back (never below a zero balance).</summary>
    public async Task RevokeAsync(Review review, CancellationToken ct)
    {
        if (!review.Rewarded) return;
        var given = await db.CoinLedger.Where(c => c.RefType == "review" && c.RefId == review.Id && c.Reason == CoinReason.ReviewReward)
            .SumAsync(c => (long?)c.Delta, ct) ?? 0;
        await db.LockAsync($"coins:{review.BuyerId}", ct);
        var balance = await db.CoinLedger.Where(c => c.UserId == review.BuyerId).SumAsync(c => (long?)c.Delta, ct) ?? 0;
        var take = Math.Min(given, Math.Max(0, balance));
        if (take > 0)
            db.CoinLedger.Add(new CoinEntry(review.BuyerId, -take, CoinReason.ReviewReward, "review", review.Id, null, "Thu hồi xu do sản phẩm đã được hoàn tiền",
                clock.UtcNow));
        review.RevokeReward();
    }
}

// ---------- buyer ----------

public record ReviewableItemDto(Guid OrderItemId, string Name, string? Variant, string? ImageUrl, int Quantity, ReviewDto? Review, bool CanReview,
    bool CanEdit, DateTimeOffset? Deadline);

public record ReviewableItemsQuery(string Code) : IRequest<IReadOnlyList<ReviewableItemDto>>;

public sealed class ReviewableItemsHandler(IApplicationDbContext db, IObjectStorage storage, ISystemParameters parameters, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<ReviewableItemsQuery, IReadOnlyList<ReviewableItemDto>>
{
    public async Task<IReadOnlyList<ReviewableItemDto>> Handle(ReviewableItemsQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var code = request.Code.Trim().ToUpperInvariant();
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Code == code && o.BuyerId == userId, ct)
                    ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
        var window = await parameters.GetIntAsync(ParameterKeys.ReviewWindowDays, ct);
        var editDays = await parameters.GetIntAsync(ParameterKeys.ReviewEditDays, ct);
        var deadline = order.CompletedAt?.AddDays(window);
        var itemIds = order.Items.Select(i => i.Id).ToList();
        var reviews = await db.Reviews.AsNoTracking().Include(r => r.Media).Where(r => itemIds.Contains(r.OrderItemId)).ToListAsync(ct);
        var dtos = (await ReviewViews.MapAsync(db, storage, reviews, ct)).ToDictionary(d => d.Id);
        var now = clock.UtcNow;
        return order.Items.OrderBy(i => i.Id).Select(i =>
        {
            var r = reviews.FirstOrDefault(x => x.OrderItemId == i.Id);
            return new ReviewableItemDto(i.Id, i.NameSnapshot, i.VariantSnapshot, i.ImageSnapshot, i.Quantity, r is null ? null : dtos[r.Id],
                r is null && order.Status == OrderStatus.Completed && deadline > now,
                r is not null && r.EditedAt is null && now <= r.CreatedAt.AddDays(editDays), deadline);
        }).ToList();
    }
}

public record WriteReviewCommand(string Code, Guid OrderItemId, int Rating, string? Content, IReadOnlyList<string>? Tags, bool Anonymous,
    IReadOnlyList<Guid>? MediaAssetIds) : IRequest<Guid>;

public sealed class WriteReviewValidator : AbstractValidator<WriteReviewCommand>
{
    public WriteReviewValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5).WithMessage("Vui lòng chọn từ 1 đến 5 sao.");
        RuleFor(x => x.Content).MaximumLength(Review.MaxContentLength).WithMessage($"Nội dung đánh giá tối đa {Review.MaxContentLength} ký tự.");
        RuleFor(x => x.MediaAssetIds).Must(m => m is null || m.Count <= Review.MaxImages + 1).WithMessage("Tối đa 6 ảnh và 1 video.");
    }
}

public sealed class WriteReviewHandler(
    IApplicationDbContext db,
    IObjectStorage storage,
    ReviewRewards rewards,
    ICounterRecomputer counters,
    ISystemParameters parameters,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<WriteReviewCommand, Guid>
{
    public async Task<Guid> Handle(WriteReviewCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var code = request.Code.Trim().ToUpperInvariant();
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Code == code && o.BuyerId == userId, ct)
                    ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
        var item = order.Items.FirstOrDefault(i => i.Id == request.OrderItemId) ?? throw new NotFoundException("Không tìm thấy sản phẩm trong đơn.");
        if (order.Status != OrderStatus.Completed) throw new ConflictException("Chỉ đánh giá được sau khi đơn hàng hoàn thành.", "NOT_COMPLETED");
        var window = await parameters.GetIntAsync(ParameterKeys.ReviewWindowDays, ct);
        if (clock.UtcNow > order.CompletedAt!.Value.AddDays(window))
            throw new ConflictException($"Đã quá hạn đánh giá ({window} ngày sau khi đơn hoàn thành).", "REVIEW_EXPIRED");
        if (await db.Reviews.AnyAsync(r => r.OrderItemId == item.Id, ct)) throw new ConflictException("Sản phẩm này của đơn đã được đánh giá.", "ALREADY_REVIEWED");

        var review = new Review(item.Id, order.Id, item.ProductId, item.SkuId, order.ShopId, userId, item.VariantSnapshot, clock.UtcNow);
        review.Write(request.Rating, request.Content, request.Tags ?? [], request.Anonymous);
        review.SetMedia(await ReviewMediaResolver.ResolveAsync(db, storage, userId, request.MediaAssetIds ?? [], [], ct));
        await using var tx = await db.BeginTransactionAsync(ct);
        db.Reviews.Add(review);
        await rewards.GrantIfDueAsync(review, ct);
        await db.SaveChangesAsync(ct);
        await counters.RecomputeRatingsAsync([item.ProductId], [order.ShopId], ct);
        await tx.CommitAsync(ct);
        return review.Id;
    }
}

public record EditReviewCommand(Guid ReviewId, int Rating, string? Content, IReadOnlyList<string>? Tags, bool Anonymous, IReadOnlyList<Guid>? MediaAssetIds)
    : IRequest<Unit>;

public sealed class EditReviewValidator : AbstractValidator<EditReviewCommand>
{
    public EditReviewValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5).WithMessage("Vui lòng chọn từ 1 đến 5 sao.");
        RuleFor(x => x.Content).MaximumLength(Review.MaxContentLength).WithMessage($"Nội dung đánh giá tối đa {Review.MaxContentLength} ký tự.");
    }
}

public sealed class EditReviewHandler(
    IApplicationDbContext db,
    IObjectStorage storage,
    ReviewRewards rewards,
    ICounterRecomputer counters,
    ISystemParameters parameters,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<EditReviewCommand, Unit>
{
    public async Task<Unit> Handle(EditReviewCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var review = await db.Reviews.Include(r => r.Media).FirstOrDefaultAsync(r => r.Id == request.ReviewId && r.BuyerId == userId, ct)
                     ?? throw new NotFoundException("Không tìm thấy đánh giá.");
        review.EnsureEditable((int)await parameters.GetIntAsync(ParameterKeys.ReviewEditDays, ct), clock.UtcNow);
        review.Write(request.Rating, request.Content, request.Tags ?? [], request.Anonymous);
        review.SetMedia(await ReviewMediaResolver.ResolveAsync(db, storage, userId, request.MediaAssetIds ?? [], review.Media, ct));
        review.MarkEdited(clock.UtcNow);
        await using var tx = await db.BeginTransactionAsync(ct);
        await rewards.GrantIfDueAsync(review, ct);
        await db.SaveChangesAsync(ct);
        await counters.RecomputeRatingsAsync([review.ProductId], [review.ShopId], ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}

// ---------- product page ----------

public record ProductReviewsQuery(Guid ProductId, int? Rating = null, bool WithMedia = false, bool WithComment = false, int Page = 1, int PageSize = 10,
    string? Variant = null) : IRequest<ProductReviewsDto>, IPagedRequest;

public sealed class ProductReviewsValidator : AbstractValidator<ProductReviewsQuery>
{
    public ProductReviewsValidator()
    {
        this.ApplyPagingRules();
        RuleFor(x => x.Rating).InclusiveBetween(1, 5).When(x => x.Rating is not null).WithMessage("Số sao từ 1 đến 5.");
        RuleFor(x => x.Variant).MaximumLength(100).WithMessage("Phân loại tối đa 100 ký tự.");
    }
}

public sealed class ProductReviewsHandler(IApplicationDbContext db, IObjectStorage storage) : IRequestHandler<ProductReviewsQuery, ProductReviewsDto>
{
    public async Task<ProductReviewsDto> Handle(ProductReviewsQuery request, CancellationToken ct)
    {
        var visible = db.Reviews.AsNoTracking().Where(r => r.ProductId == request.ProductId && !r.IsHidden);
        var byStar = await visible.GroupBy(r => r.Rating).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var summary = new ReviewSummaryDto(
            byStar.Count == 0 ? 0 : Math.Round(byStar.Sum(kv => kv.Key * kv.Value) / (double)byStar.Values.Sum(), 1),
            byStar.Values.Sum(), Enumerable.Range(1, 5).ToDictionary(s => s, s => byStar.GetValueOrDefault(s)),
            await visible.CountAsync(r => r.Media.Any(), ct), await visible.CountAsync(r => r.Content != "", ct),
            (await visible.Where(r => r.VariantSnapshot != null).GroupBy(r => r.VariantSnapshot!)
                .Select(g => new { Variant = g.Key, Count = g.Count() }).OrderByDescending(v => v.Count).ThenBy(v => v.Variant).Take(30).ToListAsync(ct))
                .Select(v => new ReviewVariantCountDto(v.Variant, v.Count)).ToList());

        var q = visible;
        if (request.Rating is { } rating) q = q.Where(r => r.Rating == rating);
        if (request.WithMedia) q = q.Where(r => r.Media.Any());
        if (request.WithComment) q = q.Where(r => r.Content != "");
        if (!string.IsNullOrWhiteSpace(request.Variant)) q = q.Where(r => r.VariantSnapshot == request.Variant.Trim());
        var page = await q.Include(r => r.Media).OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).ToPagedResultAsync(request, ct);
        var items = await ReviewViews.MapAsync(db, storage, page.Items, ct);
        return new ProductReviewsDto(summary, new PagedResult<ReviewDto>(items, page.TotalCount, page.Page, page.PageSize));
    }
}

// ---------- seller ----------

public record ShopReviewDto(ReviewDto Review, Guid ProductId, string ProductName, string OrderCode);

public record ShopReviewsQuery(Guid ShopId, int? Rating = null, bool? Replied = null, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<ShopReviewDto>>, IPagedRequest;

public sealed class ShopReviewsValidator : AbstractValidator<ShopReviewsQuery>
{
    public ShopReviewsValidator() => this.ApplyPagingRules();
}

public sealed class ShopReviewsHandler(IApplicationDbContext db, IObjectStorage storage, SellerAccess access)
    : IRequestHandler<ShopReviewsQuery, PagedResult<ShopReviewDto>>
{
    public async Task<PagedResult<ShopReviewDto>> Handle(ShopReviewsQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.ReviewManage, ct);
        var q = db.Reviews.AsNoTracking().Where(r => r.ShopId == request.ShopId);
        if (request.Rating is { } rating) q = q.Where(r => r.Rating == rating);
        if (request.Replied is { } replied) q = q.Where(r => (r.SellerReply != null) == replied);
        var page = await q.Include(r => r.Media).OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).ToPagedResultAsync(request, ct);
        var dtos = await ReviewViews.MapAsync(db, storage, page.Items, ct);
        var productIds = page.Items.Select(r => r.ProductId).Distinct().ToList();
        var names = await db.Products.IgnoreQueryFilters().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var orderIds = page.Items.Select(r => r.OrderId).Distinct().ToList();
        var codes = await db.Orders.Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Code, ct);
        return new PagedResult<ShopReviewDto>(page.Items.Select((r, i) => new ShopReviewDto(dtos[i], r.ProductId, names.GetValueOrDefault(r.ProductId) ?? "",
            codes.GetValueOrDefault(r.OrderId) ?? "")).ToList(), page.TotalCount, page.Page, page.PageSize);
    }
}

public record ReplyReviewCommand(Guid ShopId, Guid ReviewId, string Text) : IRequest<Unit>;

public sealed class ReplyReviewHandler(IApplicationDbContext db, SellerAccess access, IClock clock) : IRequestHandler<ReplyReviewCommand, Unit>
{
    public async Task<Unit> Handle(ReplyReviewCommand request, CancellationToken ct)
    {
        var staff = await access.RequireAsync(request.ShopId, ShopPermissions.ReviewManage, ct);
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == request.ReviewId && r.ShopId == request.ShopId, ct)
                     ?? throw new NotFoundException("Không tìm thấy đánh giá.");
        review.Reply(request.Text, staff.UserId, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ---------- reports & moderation ----------

public record ReportReviewCommand(Guid ReviewId, string Reason) : IRequest<Unit>;

public sealed class ReportReviewHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock) : IRequestHandler<ReportReviewCommand, Unit>
{
    public async Task<Unit> Handle(ReportReviewCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        if (!await db.Reviews.AnyAsync(r => r.Id == request.ReviewId && !r.IsHidden, ct)) throw new NotFoundException("Không tìm thấy đánh giá.");
        if (await db.ReviewReports.AnyAsync(r => r.ReviewId == request.ReviewId && r.ReporterId == userId, ct)) return Unit.Value;
        db.ReviewReports.Add(new ReviewReport(request.ReviewId, userId, request.Reason, clock.UtcNow));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (ConflictException ce) when (ce.Constraint == "ux_review_reports")
        {
            // Reported twice at once: one report is enough
        }
        return Unit.Value;
    }
}

public record ReviewReportDto(Guid Id, Guid ReviewId, string Reason, ReviewReportStatus Status, DateTimeOffset CreatedAt, ReviewDto Review, string ProductName,
    bool ReviewHidden);

public record ReviewReportsQuery(ReviewReportStatus Status = ReviewReportStatus.Pending, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<ReviewReportDto>>, IPagedRequest;

public sealed class ReviewReportsValidator : AbstractValidator<ReviewReportsQuery>
{
    public ReviewReportsValidator() => this.ApplyPagingRules();
}

public sealed class ReviewReportsHandler(IApplicationDbContext db, IObjectStorage storage) : IRequestHandler<ReviewReportsQuery, PagedResult<ReviewReportDto>>
{
    public async Task<PagedResult<ReviewReportDto>> Handle(ReviewReportsQuery request, CancellationToken ct)
    {
        var page = await db.ReviewReports.AsNoTracking().Where(r => r.Status == request.Status)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToPagedResultAsync(request, ct);
        var reviewIds = page.Items.Select(r => r.ReviewId).Distinct().ToList();
        var reviews = await db.Reviews.IgnoreQueryFilters().AsNoTracking().Include(r => r.Media).Where(r => reviewIds.Contains(r.Id)).ToListAsync(ct);
        var dtos = (await ReviewViews.MapAsync(db, storage, reviews, ct)).ToDictionary(d => d.Id);
        var productIds = reviews.Select(r => r.ProductId).Distinct().ToList();
        var names = await db.Products.IgnoreQueryFilters().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        return new PagedResult<ReviewReportDto>(page.Items.Select(r =>
        {
            var review = reviews.Single(x => x.Id == r.ReviewId);
            return new ReviewReportDto(r.Id, r.ReviewId, r.Reason, r.Status, r.CreatedAt, dtos[r.ReviewId], names.GetValueOrDefault(review.ProductId) ?? "",
                review.IsHidden);
        }).ToList(), page.TotalCount, page.Page, page.PageSize);
    }
}

/// <summary>Uphold (hide the review, every open report on it is closed) or dismiss a report.</summary>
public record ResolveReviewReportCommand(Guid ReportId, bool Hide, string? Reason) : IRequest<Unit>;

public sealed class ResolveReviewReportHandler(IApplicationDbContext db, ICounterRecomputer counters, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<ResolveReviewReportCommand, Unit>
{
    public async Task<Unit> Handle(ResolveReviewReportCommand request, CancellationToken ct)
    {
        var adminId = UserGuard.Require(currentUser);
        var report = await db.ReviewReports.FirstOrDefaultAsync(r => r.Id == request.ReportId, ct) ?? throw new NotFoundException("Không tìm thấy báo cáo.");
        var review = await db.Reviews.SingleAsync(r => r.Id == report.ReviewId, ct);
        var now = clock.UtcNow;
        if (request.Hide)
        {
            review.Hide(request.Reason ?? string.Empty);
            foreach (var open in await db.ReviewReports.Where(r => r.ReviewId == review.Id && r.Status == ReviewReportStatus.Pending).ToListAsync(ct))
                open.Resolve(true, adminId, now);
        }
        else
        {
            report.Resolve(false, adminId, now);
        }
        await db.SaveChangesAsync(ct);
        await counters.RecomputeRatingsAsync([review.ProductId], [review.ShopId], ct);
        return Unit.Value;
    }
}
