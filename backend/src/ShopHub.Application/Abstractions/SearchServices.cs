namespace ShopHub.Application.Abstractions;

public enum ProductSort
{
    Relevance,
    Newest,
    BestSelling,
    PriceAsc,
    PriceDesc,
}

/// <summary>Everything a buyer can filter on. Category filters include every descendant category.</summary>
public record ProductSearchRequest(
    string? Query,
    Guid? CategoryId,
    Guid? ShopId,
    IReadOnlyList<string> ProvinceCodes,
    IReadOnlyList<Guid> BrandIds,
    long? MinPrice,
    long? MaxPrice,
    int? MinRating,
    bool MallOnly,
    bool PreferredOnly,
    bool InStockOnly,
    string? Condition,
    IReadOnlyList<string> Attributes,
    ProductSort Sort,
    int Page,
    int PageSize,
    // Đơn vị vận chuyển (any of) and dịch vụ: Freeship Xtra shop, shop voucher running, COD possible
    IReadOnlyList<string>? CarrierCodes = null,
    bool FreeshipOnly = false,
    bool WithVoucherOnly = false,
    bool CodOnly = false);

public record ProductCardDto(
    Guid Id,
    string Name,
    string Slug,
    string? ImageUrl,
    long MinPrice,
    long MaxPrice,
    long OriginalPrice,
    int DiscountPercent,
    double RatingAvg,
    int RatingCount,
    int SoldCount,
    bool InStock,
    Guid ShopId,
    string ShopName,
    bool IsMall,
    bool IsPreferred,
    string? ProvinceName,
    // True when the shown price is a Flash Sale price (set by CardPricing)
    bool IsFlashSale = false,
    // Short tags under the name, each backed by data: a running combo / add-on / gift programme, the shop's coin
    // cashback voucher, Freeship Xtra (set by CardPricing)
    IReadOnlyList<string>? Labels = null,
    // Campaign frame over the photo when the product takes part in a running campaign that has one — drawn only on the
    // campaign's own page; every other grid shows a small chip with CampaignName instead (G3 B3)
    string? FrameUrl = null,
    string? CampaignName = null);

public record FacetValue(string Value, string Label, int Count);

public record ProductSearchFacets(
    IReadOnlyList<FacetValue> Categories,
    IReadOnlyList<FacetValue> Provinces,
    IReadOnlyList<FacetValue> Brands,
    IReadOnlyList<FacetValue> Ratings,
    IReadOnlyList<FacetValue> ShopTypes,
    IReadOnlyList<FacetValue> Conditions,
    IReadOnlyDictionary<string, IReadOnlyList<FacetValue>> Attributes,
    IReadOnlyList<FacetValue>? Carriers = null,
    // "freeship", "voucher", "cod"
    IReadOnlyList<FacetValue>? Services = null);

/// <param name="Engine">"meilisearch" or "postgres" (fallback while the search engine is down).</param>
public record ProductSearchResult(
    IReadOnlyList<ProductCardDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    ProductSearchFacets Facets,
    string Engine);

/// <summary>
/// Product search with facet counts. The implementation tries Meilisearch and falls back to PostgreSQL
/// (unaccent + trigram) when the engine is unavailable, so the buyer site keeps working.
/// </summary>
public interface IProductSearch
{
    Task<ProductSearchResult> SearchAsync(ProductSearchRequest request, CancellationToken ct);
}

/// <summary>Write side of the search index (Meilisearch). Fed by the outbox, never inside a business transaction.</summary>
public interface ISearchIndexer
{
    Task SyncProductsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct);
    Task SyncShopAsync(Guid shopId, CancellationToken ct);
    Task<int> ReindexAllAsync(CancellationToken ct);
}

/// <summary>Recomputes denormalised counters from their source rows (likes, followers, product counts, views).</summary>
public interface ICounterRecomputer
{
    Task RecomputeProductLikesAsync(Guid productId, CancellationToken ct);
    Task RecomputeShopFollowersAsync(Guid shopId, CancellationToken ct);
    Task RecomputeShopProductCountAsync(Guid shopId, CancellationToken ct);

    /// <summary>"Đã bán" = units in orders handed to the carrier and not returned.</summary>
    Task RecomputeProductSalesAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct);

    /// <summary>Average rating and review count of the products and their shops, from visible reviews.</summary>
    Task RecomputeRatingsAsync(IReadOnlyCollection<Guid> productIds, IReadOnlyCollection<Guid> shopIds, CancellationToken ct);

    Task<int> RecomputeAllAsync(CancellationToken ct);
}
