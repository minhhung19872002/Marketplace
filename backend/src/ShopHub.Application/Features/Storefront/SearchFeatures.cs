using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Engage;

namespace ShopHub.Application.Features.Storefront;

public record SearchProductsQuery(
    string? Q = null,
    Guid? CategoryId = null,
    Guid? ShopId = null,
    string[]? Provinces = null,
    Guid[]? Brands = null,
    long? MinPrice = null,
    long? MaxPrice = null,
    int? MinRating = null,
    bool Mall = false,
    bool Preferred = false,
    bool InStock = false,
    string? Condition = null,
    string[]? Attrs = null,
    ProductSort Sort = ProductSort.Relevance,
    int Page = 1,
    int PageSize = 60) : IRequest<ProductSearchResult>, IPagedRequest;

public sealed class SearchProductsValidator : AbstractValidator<SearchProductsQuery>
{
    public SearchProductsValidator()
    {
        this.ApplyPagingRules();
        RuleFor(x => x.Q).MaximumLength(100).WithMessage("Từ khoá tối đa 100 ký tự.");
        RuleFor(x => x).Must(x => x.MinPrice is null || x.MaxPrice is null || x.MinPrice <= x.MaxPrice)
            .WithName("minPrice").WithMessage("Khoảng giá không hợp lệ: giá từ phải nhỏ hơn hoặc bằng giá đến.");
        RuleFor(x => x.MinRating).InclusiveBetween(1, 5).When(x => x.MinRating is not null).WithMessage("Số sao từ 1 đến 5.");
        RuleFor(x => x.Page).LessThanOrEqualTo(100).WithMessage("Chỉ xem được tối đa 100 trang kết quả.");
    }
}

public sealed class SearchProductsHandler(IProductSearch search, IApplicationDbContext db, ICurrentUser currentUser, IClock clock, CardPricing pricing)
    : IRequestHandler<SearchProductsQuery, ProductSearchResult>
{
    public async Task<ProductSearchResult> Handle(SearchProductsQuery r, CancellationToken ct)
    {
        var result = await search.SearchAsync(new ProductSearchRequest(
            string.IsNullOrWhiteSpace(r.Q) ? null : r.Q.Trim(), r.CategoryId, r.ShopId, r.Provinces ?? [], r.Brands ?? [], r.MinPrice, r.MaxPrice,
            r.MinRating, r.Mall, r.Preferred, r.InStock, r.Condition, r.Attrs ?? [], r.Sort, r.Page, r.PageSize), ct);

        // First page of a keyword search feeds "từ khoá hot" and suggestions
        if (!string.IsNullOrWhiteSpace(r.Q) && r.Page == 1 && r.ShopId is null)
        {
            var keyword = Slug.Fold(r.Q);
            if (keyword.Length is >= 2 and <= 100)
            {
                db.SearchLogs.Add(new SearchLog(keyword, currentUser.UserId, result.TotalCount, clock.UtcNow));
                await db.SaveChangesAsync(ct);
            }
        }
        return result with { Items = await pricing.ApplyAsync(result.Items, ct) };
    }
}

// ---------- Suggestions & hot keywords ----------

public record SuggestionDto(IReadOnlyList<string> Keywords, IReadOnlyList<SuggestedProductDto> Products, IReadOnlyList<SuggestedShopDto> Shops);

public record SuggestedProductDto(Guid Id, string Name, string Slug, string? ImageUrl);

public record SuggestedShopDto(Guid Id, string Name, string Slug, string? LogoUrl, bool IsMall);

public record SuggestQuery(string Q) : IRequest<SuggestionDto>;

public sealed class SuggestHandler(IProductSearch search, IApplicationDbContext db, IClock clock) : IRequestHandler<SuggestQuery, SuggestionDto>
{
    public async Task<SuggestionDto> Handle(SuggestQuery request, CancellationToken ct)
    {
        var q = request.Q.Trim();
        if (q.Length == 0 || q.Length > 100) return new SuggestionDto([], [], []);
        var folded = Slug.Fold(q);
        var since = clock.UtcNow.AddDays(-30);

        // Popular queries that start with what was typed and actually returned results
        var keywords = await db.SearchLogs.AsNoTracking()
            .Where(l => l.OccurredAt > since && l.ResultCount > 0 && l.Keyword.StartsWith(folded))
            .GroupBy(l => l.Keyword)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => g.Key).Take(5).ToListAsync(ct);

        var products = await search.SearchAsync(new ProductSearchRequest(q, null, null, [], [], null, null, null, false, false, false, null, [],
            ProductSort.Relevance, 1, 5), ct);

        var shops = await db.Shops.AsNoTracking()
            .Where(s => (s.Status == Domain.Shops.ShopStatus.Active || s.Status == Domain.Shops.ShopStatus.Vacation) && s.Slug.Contains(Slug.From(q)))
            .OrderByDescending(s => s.Type == Domain.Shops.ShopType.Mall).ThenByDescending(s => s.FollowerCount).ThenBy(s => s.Id)
            .Take(3)
            .Select(s => new SuggestedShopDto(s.Id, s.Name, s.Slug, s.LogoUrl, s.Type == Domain.Shops.ShopType.Mall))
            .ToListAsync(ct);

        return new SuggestionDto(keywords, products.Items.Select(p => new SuggestedProductDto(p.Id, p.Name, p.Slug, p.ImageUrl)).ToList(), shops);
    }
}

/// <summary>"Shop liên quan đến từ khoá" (II.3): open shops whose name matches the keyword, Mall first, then the most followed.</summary>
public record RelatedShopDto(Guid Id, string Name, string Slug, string? LogoUrl, bool IsMall, bool IsPreferred, double RatingAvg, int FollowerCount,
    int ProductCount, string? ProvinceName);

public record RelatedShopsQuery(string Q, int Take = 1) : IRequest<IReadOnlyList<RelatedShopDto>>;

public sealed class RelatedShopsHandler(IApplicationDbContext db) : IRequestHandler<RelatedShopsQuery, IReadOnlyList<RelatedShopDto>>
{
    public async Task<IReadOnlyList<RelatedShopDto>> Handle(RelatedShopsQuery request, CancellationToken ct)
    {
        var slug = Slug.From(request.Q ?? string.Empty);
        if (slug.Length < 2) return [];
        var rows = await db.Shops.AsNoTracking()
            .Where(s => (s.Status == Domain.Shops.ShopStatus.Active || s.Status == Domain.Shops.ShopStatus.Vacation) && s.Slug.Contains(slug))
            .OrderByDescending(s => s.Type == Domain.Shops.ShopType.Mall).ThenByDescending(s => s.FollowerCount).ThenBy(s => s.Id)
            .Take(Math.Clamp(request.Take, 1, 5))
            .Select(s => new
            {
                Shop = s,
                Province = db.ShopWarehouses.Where(w => w.ShopId == s.Id && w.IsPickupDefault)
                    .Select(w => db.AdminDivisions.Where(d => d.Code == w.ProvinceCode).Select(d => d.Name).FirstOrDefault()).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return rows.Select(r => new RelatedShopDto(r.Shop.Id, r.Shop.Name, r.Shop.Slug, r.Shop.LogoUrl, r.Shop.Type == Domain.Shops.ShopType.Mall,
            r.Shop.IsPreferred, r.Shop.RatingAvg, r.Shop.FollowerCount, r.Shop.ProductCount, ProductCards.ShortProvince(r.Province))).ToList();
    }
}

public record HotKeywordsQuery(int Take = 8) : IRequest<IReadOnlyList<string>>;

public sealed class HotKeywordsHandler(IApplicationDbContext db, ISystemParameters parameters, IClock clock)
    : IRequestHandler<HotKeywordsQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(HotKeywordsQuery request, CancellationToken ct)
    {
        var take = Math.Clamp(request.Take, 1, 20);
        var days = await parameters.GetIntAsync(ParameterKeys.SearchHotKeywordDays, ct);
        var since = clock.UtcNow.AddDays(-days);
        var fromLogs = await db.SearchLogs.AsNoTracking()
            .Where(l => l.OccurredAt > since && l.ResultCount > 0)
            .GroupBy(l => l.Keyword)
            .Where(g => g.Select(x => x.UserId).Distinct().Count() >= 2 || g.Count() >= 3)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => g.Key).Take(take).ToListAsync(ct);

        // Top up with the curated list while real traffic is thin
        var curated = JsonSerializer.Deserialize<List<string>>(await parameters.GetStringAsync(ParameterKeys.SearchHotKeywords, ct)) ?? [];
        var result = fromLogs.ToList();
        foreach (var k in curated)
        {
            if (result.Count >= take) break;
            if (!result.Contains(Slug.Fold(k))) result.Add(k);
        }
        return result;
    }
}
