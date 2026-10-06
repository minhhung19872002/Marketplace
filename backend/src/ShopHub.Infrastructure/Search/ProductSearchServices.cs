using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Storefront;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Shops;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Search;

// SQL helper: public.immutable_unaccent(text) registered as an EF function
public static class SearchFunctions
{
    public static string Unaccent(string value) => throw new NotSupportedException("Chỉ dùng trong truy vấn LINQ tới PostgreSQL.");
}

/// <summary>Turns raw facet counts into labelled values (category/province/brand names come from the database).</summary>
internal sealed class FacetLabeler(ShopHubDbContext db)
{
    public async Task<ProductSearchFacets> LabelAsync(
        Dictionary<string, int> categories, Dictionary<string, int> provinces, Dictionary<string, int> brands, Dictionary<int, int> ratingFloors,
        int mall, int preferred, Dictionary<string, int> conditions, Dictionary<string, Dictionary<string, int>> attributes, CancellationToken ct)
    {
        var categoryIds = categories.Keys.Select(Guid.Parse).ToList();
        var categoryNames = await db.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id.ToString(), c => c.Name, ct);
        var provinceCodes = provinces.Keys.ToList();
        var provinceNames = await db.AdminDivisions.AsNoTracking().Where(d => provinceCodes.Contains(d.Code)).ToDictionaryAsync(d => d.Code, d => d.Name, ct);
        var brandIds = brands.Keys.Select(Guid.Parse).ToList();
        var brandNames = await db.Brands.AsNoTracking().Where(b => brandIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id.ToString(), b => b.Name, ct);

        static List<FacetValue> Sorted(IEnumerable<FacetValue> v) => v.Where(f => f.Count > 0).OrderByDescending(f => f.Count).ThenBy(f => f.Label).ToList();

        // "Từ N sao" counts every product rated N or more
        var ratings = new[] { 5, 4, 3 }.Select(n => new FacetValue(n.ToString(), n == 5 ? "5 sao" : $"Từ {n} sao",
            ratingFloors.Where(kv => kv.Key >= n).Sum(kv => kv.Value))).Where(f => f.Count > 0).ToList();

        return new ProductSearchFacets(
            Sorted(categories.Where(kv => categoryNames.ContainsKey(kv.Key)).Select(kv => new FacetValue(kv.Key, categoryNames[kv.Key], kv.Value))),
            Sorted(provinces.Select(kv => new FacetValue(kv.Key, ProductCards.ShortProvince(provinceNames.GetValueOrDefault(kv.Key)) ?? kv.Key, kv.Value))),
            Sorted(brands.Where(kv => brandNames.ContainsKey(kv.Key)).Select(kv => new FacetValue(kv.Key, brandNames[kv.Key], kv.Value))),
            ratings,
            new[] { new FacetValue("mall", "ShopHub Mall", mall), new FacetValue("preferred", "Shop Yêu thích", preferred) }.Where(f => f.Count > 0).ToList(),
            Sorted(conditions.Select(kv => new FacetValue(kv.Key, kv.Key == "New" ? "Mới" : "Đã sử dụng", kv.Value))),
            attributes.Where(kv => kv.Value.Count > 1 || kv.Value.Values.Sum() > 0)
                .OrderBy(kv => kv.Key)
                .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<FacetValue>)Sorted(kv.Value.Select(v => new FacetValue($"{kv.Key}={v.Key}", v.Key, v.Value)))));
    }
}

// ---------------------------------------------------------------------------------------------------------------
// Meilisearch
// ---------------------------------------------------------------------------------------------------------------

public sealed class MeiliProductSearch(MeiliClient meili, ShopHubDbContext db)
{
    private static readonly string[] Facets = ["categoryIds", "provinceCode", "brandId", "ratingFloor", "isMall", "isPreferred", "condition", "attributes"];

    public async Task<ProductSearchResult> SearchAsync(ProductSearchRequest r, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["q"] = string.IsNullOrWhiteSpace(r.Query) ? "" : Slug.Fold(r.Query),
            // Every word must match (typos allowed), same as the PostgreSQL fallback
            ["matchingStrategy"] = "all",
            ["filter"] = BuildFilter(r),
            ["facets"] = Facets,
            ["page"] = r.Page,
            ["hitsPerPage"] = r.PageSize,
            ["sort"] = r.Sort switch
            {
                ProductSort.Newest => new[] { "publishedAt:desc", "id:asc" },
                ProductSort.BestSelling => new[] { "restricted:asc", "soldCount:desc", "id:asc" },
                ProductSort.PriceAsc => new[] { "minPrice:asc", "id:asc" },
                ProductSort.PriceDesc => new[] { "minPrice:desc", "id:asc" },
                _ => string.IsNullOrWhiteSpace(r.Query) ? new[] { "restricted:asc", "soldCount:desc", "publishedAt:desc", "id:asc" } : null,
            },
        };
        var res = await meili.SearchAsync(MeiliClient.ProductsIndex, body, ct);

        var items = res["hits"]!.AsArray().Select(h => h!.Deserialize<ProductSearchDocument>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!)
            .Select(d => new ProductCardDto(Guid.Parse(d.Id), d.Name, d.Slug, d.ImageUrl, d.MinPrice, d.MaxPrice, d.OriginalPrice, d.DiscountPercent,
                d.RatingAvg, d.RatingCount, d.SoldCount, d.InStock, Guid.Parse(d.ShopId), d.ShopName, d.IsMall, d.IsPreferred, d.ProvinceName))
            .ToList();

        var dist = res["facetDistribution"]?.AsObject() ?? new JsonObject();
        Dictionary<string, int> Facet(string name) =>
            dist[name]?.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<int>()) ?? [];

        var attributes = new Dictionary<string, Dictionary<string, int>>();
        foreach (var (key, count) in Facet("attributes"))
        {
            var sep = key.IndexOf('=');
            if (sep <= 0) continue;
            var name = key[..sep];
            if (!attributes.TryGetValue(name, out var values)) attributes[name] = values = [];
            values[key[(sep + 1)..]] = count;
        }

        var facets = await new FacetLabeler(db).LabelAsync(Facet("categoryIds"), Facet("provinceCode"), Facet("brandId"),
            Facet("ratingFloor").ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value),
            Facet("isMall").GetValueOrDefault("true"), Facet("isPreferred").GetValueOrDefault("true"), Facet("condition"), attributes, ct);

        return new ProductSearchResult(items, res["totalHits"]?.GetValue<int>() ?? items.Count, r.Page, r.PageSize, facets, "meilisearch");
    }

    private static List<object> BuildFilter(ProductSearchRequest r)
    {
        static string Q(string v) => "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        var filter = new List<object>();
        if (r.CategoryId is { } c) filter.Add($"categoryIds = {Q(c.ToString())}");
        if (r.ShopId is { } s) filter.Add($"shopId = {Q(s.ToString())}");
        if (r.ProvinceCodes.Count > 0) filter.Add($"provinceCode IN [{string.Join(", ", r.ProvinceCodes.Select(Q))}]");
        if (r.BrandIds.Count > 0) filter.Add($"brandId IN [{string.Join(", ", r.BrandIds.Select(b => Q(b.ToString())))}]");
        // A product matches a price range when any of its SKUs can be in it
        if (r.MinPrice is { } min) filter.Add($"maxPrice >= {min}");
        if (r.MaxPrice is { } max) filter.Add($"minPrice <= {max}");
        if (r.MinRating is { } rating) filter.Add($"ratingFloor >= {rating}");
        if (r.MallOnly) filter.Add("isMall = true");
        if (r.PreferredOnly) filter.Add("isPreferred = true");
        if (r.InStockOnly) filter.Add("inStock = true");
        if (!string.IsNullOrWhiteSpace(r.Condition)) filter.Add($"condition = {Q(r.Condition)}");
        // Same attribute: any of the values (OR); different attributes: all (AND)
        foreach (var group in r.Attributes.Where(a => a.Contains('=')).GroupBy(a => a[..a.IndexOf('=')]))
            filter.Add(group.Select(v => $"attributes = {Q(v)}").ToList());
        return filter;
    }

    public static object Settings(Dictionary<string, List<string>> synonyms) => new
    {
        searchableAttributes = new[] { "nameFolded", "brandName", "categoryNamesFolded", "shopNameFolded" },
        filterableAttributes = new[]
        {
            "categoryIds", "shopId", "provinceCode", "brandId", "minPrice", "maxPrice", "ratingFloor", "isMall", "isPreferred", "inStock",
            "condition", "attributes",
        },
        sortableAttributes = new[] { "publishedAt", "soldCount", "minPrice", "ratingAvg", "id", "restricted" },
        // An explicit sort (price, newest…) wins over relevance — the buyer asked for that order; without one the
        // "sort" rule is a no-op and relevance decides
        // Relevance first, then shops restricted by penalty points go last
        rankingRules = new[] { "sort", "words", "typo", "proximity", "attribute", "exactness", "restricted:asc", "soldCount:desc" },
        // Vietnamese words are short: allow one typo from 4 letters, two from 8
        typoTolerance = new { enabled = true, minWordSizeForTypos = new { oneTypo = 4, twoTypos = 8 } },
        faceting = new { maxValuesPerFacet = 200 },
        pagination = new { maxTotalHits = 6000 },
        synonyms,
    };
}

// ---------------------------------------------------------------------------------------------------------------
// PostgreSQL fallback
// ---------------------------------------------------------------------------------------------------------------

/// <summary>
/// Same contract on PostgreSQL (unaccent + per-word LIKE, trigram-indexed). No typo tolerance or synonyms — it only
/// has to keep the site usable while Meilisearch is down.
/// </summary>
public sealed class PostgresProductSearch(ShopHubDbContext db, IClock clock)
{
    public async Task<ProductSearchResult> SearchAsync(ProductSearchRequest r, CancellationToken ct)
    {
        var (minPrice, maxPrice) = await PriceExpressionsAsync(ct);
        var filtered = await FilterAsync(r, minPrice, maxPrice, ct);

        IOrderedQueryable<Product> ordered = r.Sort switch
        {
            ProductSort.Newest => filtered.OrderByDescending(p => p.PublishedAt),
            ProductSort.BestSelling => filtered.OrderByDescending(p => p.SoldCount),
            ProductSort.PriceAsc => filtered.OrderBy(minPrice),
            ProductSort.PriceDesc => filtered.OrderByDescending(minPrice),
            _ => filtered.OrderByDescending(p => p.SoldCount).ThenByDescending(p => p.PublishedAt),
        };
        var page = await ordered.ThenBy(p => p.Id).ToPagedResultAsync(ProductCards.Row(db), new Paging(r.Page, r.PageSize), ct);

        // Facets on the same filtered set
        var categoryRows = await filtered.GroupBy(p => p.CategoryId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var parents = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToDictionaryAsync(c => c.Id, c => c.ParentId, ct);
        var categories = new Dictionary<string, int>();
        foreach (var row in categoryRows)
            for (Guid? cur = row.Key; cur is { } id; cur = parents.GetValueOrDefault(id))
                categories[id.ToString()] = categories.GetValueOrDefault(id.ToString()) + row.Count;

        var provinces = await (from p in filtered
                               join w in db.ShopWarehouses on p.ShopId equals w.ShopId
                               where w.IsPickupDefault
                               group p by w.ProvinceCode into g
                               select new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var brands = await filtered.Where(p => p.BrandId != null).GroupBy(p => p.BrandId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key.ToString(), x => x.Count, ct);
        var ratings = await filtered.GroupBy(p => (int)Math.Floor(p.RatingAvg)).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var mall = await filtered.CountAsync(p => db.Shops.Any(s => s.Id == p.ShopId && s.Type == ShopType.Mall), ct);
        var preferred = await filtered.CountAsync(p => db.Shops.Any(s => s.Id == p.ShopId && s.IsPreferred), ct);
        var conditions = await filtered.GroupBy(p => p.Condition).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key.ToString(), x => x.Count, ct);
        // text[] values are expanded in memory (EF cannot group over unnest); one row per product attribute
        var attributeRows = await (from p in filtered
                                   from a in p.Attributes
                                   join d in db.CategoryAttributes on a.AttributeId equals d.Id
                                   where d.IsFilterable
                                   select new { p.Id, d.Name, a.Values }).ToListAsync(ct);
        var attributes = attributeRows
            .SelectMany(x => x.Values.Distinct().Select(v => new { x.Id, x.Name, Value = v }))
            .GroupBy(x => x.Name)
            .ToDictionary(g => g.Key, g => g.GroupBy(x => x.Value).ToDictionary(v => v.Key, v => v.Select(x => x.Id).Distinct().Count()));

        var facets = await new FacetLabeler(db).LabelAsync(categories, provinces, brands, ratings, mall, preferred, conditions, attributes, ct);
        return new ProductSearchResult(page.Items.Select(ProductCards.ToDto).ToList(), page.TotalCount, r.Page, r.PageSize, facets, "postgres");
    }

    /// <summary>
    /// The product's lowest / highest price in force, like the index (PriceBook rules): a running programme below the list
    /// price — a flash one only while approved, its slot open and quota left. Plain columns when no programme runs at all.
    /// </summary>
    private async Task<(Expression<Func<Product, long>> Min, Expression<Func<Product, long>> Max)> PriceExpressionsAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (!await db.PricePrograms.AnyAsync(pp => pp.IsActive && pp.StartAt <= now && pp.EndAt > now, ct))
            return (p => p.MinPrice, p => p.MaxPrice);
        return (p => p.Skus.Where(s => s.IsActive).Select(s => (long?)(db.PricePrograms
                    .Where(pp => pp.SkuId == s.Id && pp.IsActive && pp.StartAt <= now && pp.EndAt > now && pp.Price < s.Price
                        && (pp.Kind == PriceProgramKind.Discount || db.FlashSaleItems.Any(i => i.Id == pp.RefId && i.Status == FlashItemStatus.Approved
                            && i.Sold < i.Quota && db.FlashSaleSlots.Any(sl => sl.Id == i.SlotId && sl.Status == FlashSlotStatus.Open))))
                    .Select(pp => (long?)pp.Price).Min() ?? s.Price)).Min() ?? p.MinPrice,
                p => p.Skus.Where(s => s.IsActive).Select(s => (long?)(db.PricePrograms
                    .Where(pp => pp.SkuId == s.Id && pp.IsActive && pp.StartAt <= now && pp.EndAt > now && pp.Price < s.Price
                        && (pp.Kind == PriceProgramKind.Discount || db.FlashSaleItems.Any(i => i.Id == pp.RefId && i.Status == FlashItemStatus.Approved
                            && i.Sold < i.Quota && db.FlashSaleSlots.Any(sl => sl.Id == i.SlotId && sl.Status == FlashSlotStatus.Open))))
                    .Select(pp => (long?)pp.Price).Min() ?? s.Price)).Max() ?? p.MaxPrice);
    }

    private static Expression<Func<Product, bool>> Compare(Expression<Func<Product, long>> price, Func<Expression, Expression, BinaryExpression> op, long bound) =>
        Expression.Lambda<Func<Product, bool>>(op(price.Body, Expression.Constant(bound)), price.Parameters);

    private async Task<IQueryable<Product>> FilterAsync(ProductSearchRequest r, Expression<Func<Product, long>> minPrice,
        Expression<Func<Product, long>> maxPrice, CancellationToken ct)
    {
        var q = ProductCards.Visible(db).AsNoTracking();
        var categories = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId, c.Name }).ToListAsync(ct);
        HashSet<Guid> Subtree(IEnumerable<Guid> roots)
        {
            var subtree = roots.ToHashSet();
            for (var added = true; added;)
            {
                added = false;
                foreach (var c in categories.Where(c => c.ParentId is { } pid && subtree.Contains(pid) && !subtree.Contains(c.Id)))
                    added |= subtree.Add(c.Id);
            }
            return subtree;
        }

        // Same fields as the Meilisearch index: every word must appear in the product name, its category path,
        // its brand or its shop name
        if (!string.IsNullOrWhiteSpace(r.Query))
            foreach (var word in Slug.Fold(r.Query).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var inCategory = Subtree(categories.Where(c => Slug.Fold(c.Name).Contains(word)).Select(c => c.Id)).ToList();
                q = q.Where(p => SearchFunctions.Unaccent(p.Name.ToLower()).Contains(word)
                    || inCategory.Contains(p.CategoryId)
                    || db.Brands.Any(b => b.Id == p.BrandId && SearchFunctions.Unaccent(b.Name.ToLower()).Contains(word))
                    || db.Shops.Any(s => s.Id == p.ShopId && SearchFunctions.Unaccent(s.Name.ToLower()).Contains(word)));
            }

        if (r.CategoryId is { } categoryId)
        {
            var subtree = Subtree([categoryId]);
            q = q.Where(p => subtree.Contains(p.CategoryId));
        }
        if (r.ShopId is { } shopId) q = q.Where(p => p.ShopId == shopId);
        if (r.ProvinceCodes.Count > 0)
            q = q.Where(p => db.ShopWarehouses.Any(w => w.ShopId == p.ShopId && w.IsPickupDefault && r.ProvinceCodes.Contains(w.ProvinceCode)));
        if (r.BrandIds.Count > 0) q = q.Where(p => p.BrandId != null && r.BrandIds.Contains(p.BrandId.Value));
        if (r.MinPrice is { } min) q = q.Where(Compare(maxPrice, Expression.GreaterThanOrEqual, min));
        if (r.MaxPrice is { } max) q = q.Where(Compare(minPrice, Expression.LessThanOrEqual, max));
        if (r.MinRating is { } rating) q = q.Where(p => p.RatingAvg >= rating);
        if (r.MallOnly) q = q.Where(p => db.Shops.Any(s => s.Id == p.ShopId && s.Type == ShopType.Mall));
        if (r.PreferredOnly) q = q.Where(p => db.Shops.Any(s => s.Id == p.ShopId && s.IsPreferred));
        if (r.InStockOnly) q = q.Where(p => p.Skus.Any(s => s.IsActive && s.Stock - s.Reserved > 0));
        if (Enum.TryParse<ProductCondition>(r.Condition, out var condition)) q = q.Where(p => p.Condition == condition);
        foreach (var group in r.Attributes.Where(a => a.Contains('=')).GroupBy(a => a[..a.IndexOf('=')]))
        {
            var name = group.Key;
            var values = group.Select(a => a[(a.IndexOf('=') + 1)..]).ToList();
            var ids = await db.CategoryAttributes.AsNoTracking().Where(d => d.Name == name).Select(d => d.Id).ToListAsync(ct);
            q = q.Where(p => p.Attributes.Any(a => ids.Contains(a.AttributeId) && a.Values.Any(v => values.Contains(v))));
        }
        return q;
    }

    private sealed record Paging(int Page, int PageSize) : IPagedRequest;
}

/// <summary>Meilisearch first; on any engine failure the PostgreSQL path answers instead.</summary>
public sealed class ResilientProductSearch(MeiliProductSearch meili, PostgresProductSearch postgres, ILogger<ResilientProductSearch> logger) : IProductSearch
{
    public async Task<ProductSearchResult> SearchAsync(ProductSearchRequest request, CancellationToken ct)
    {
        try
        {
            return await meili.SearchAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Meilisearch unavailable, falling back to PostgreSQL search");
            return await postgres.SearchAsync(request, ct);
        }
    }
}

// ---------------------------------------------------------------------------------------------------------------
// Indexer
// ---------------------------------------------------------------------------------------------------------------

public sealed class MeiliSearchIndexer(MeiliClient meili, ShopHubDbContext db, ISystemParameters parameters, Application.Features.Marketing.PriceBook prices,
    IClock clock, ILogger<MeiliSearchIndexer> logger)
    : ISearchIndexer
{
    private const int Batch = 500;

    public async Task ConfigureAsync(CancellationToken ct)
    {
        await meili.EnsureIndexAsync(MeiliClient.ProductsIndex, ct);
        var synonyms = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(await parameters.GetStringAsync(ParameterKeys.SearchSynonyms, ct)) ?? [];
        // Synonyms work on the folded (no-tone) text that is indexed and queried
        var folded = synonyms.ToDictionary(kv => Slug.Fold(kv.Key), kv => kv.Value.Select(Slug.Fold).ToList());
        await meili.UpdateSettingsAsync(MeiliClient.ProductsIndex, MeiliProductSearch.Settings(folded), ct);
    }

    public async Task SyncProductsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        foreach (var chunk in productIds.Distinct().Chunk(Batch))
        {
            var (docs, removed) = await ProductSearchProjection.BuildAsync(db, chunk, prices, clock.UtcNow, ct);
            await meili.AddOrReplaceAsync(MeiliClient.ProductsIndex, docs, ct);
            await meili.DeleteAsync(MeiliClient.ProductsIndex, removed.Select(id => id.ToString()).ToList(), ct);
        }
    }

    public async Task SyncShopAsync(Guid shopId, CancellationToken ct)
    {
        var ids = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(p => p.ShopId == shopId).Select(p => p.Id).ToListAsync(ct);
        await SyncProductsAsync(ids, ct);
    }

    public async Task<int> ReindexAllAsync(CancellationToken ct)
    {
        await ConfigureAsync(ct);
        await meili.DeleteAllAsync(MeiliClient.ProductsIndex, ct);
        var ids = await db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Active).OrderBy(p => p.Id).Select(p => p.Id).ToListAsync(ct);
        await SyncProductsAsync(ids, ct);
        logger.LogInformation("Reindexed {Count} products into Meilisearch", ids.Count);
        return ids.Count;
    }

    /// <summary>At startup: configure, and rebuild when the index and the database disagree on the number of products.</summary>
    public async Task EnsureFreshAsync(CancellationToken ct)
    {
        await ConfigureAsync(ct);
        // Documents still being indexed would look like a mismatch and trigger a needless full rebuild
        await meili.WaitIdleAsync(MeiliClient.ProductsIndex, TimeSpan.FromMinutes(30), ct);
        var indexed = await meili.CountAsync(MeiliClient.ProductsIndex, ct);
        var expected = await ProductCards.Visible(db).CountAsync(ct);
        if (indexed != expected)
        {
            await ReindexAllAsync(ct);
            return;
        }
        // Prices in force move on the clock: programmes running now or ended while the API may have been down get re-priced
        var now = clock.UtcNow;
        var since = now.AddDays(-7);
        var repriced = await (from pp in db.PricePrograms.AsNoTracking()
                              join s in db.Skus.AsNoTracking().IgnoreQueryFilters() on pp.SkuId equals s.Id
                              where pp.StartAt <= now && pp.EndAt > since
                              select s.ProductId).Distinct().ToListAsync(ct);
        if (repriced.Count > 0) await SyncProductsAsync(repriced, ct);
    }
}
