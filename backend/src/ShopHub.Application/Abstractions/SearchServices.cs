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
    int PageSize);

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
    string? ProvinceName);

public record FacetValue(string Value, string Label, int Count);

public record ProductSearchFacets(
    IReadOnlyList<FacetValue> Categories,
    IReadOnlyList<FacetValue> Provinces,
    IReadOnlyList<FacetValue> Brands,
    IReadOnlyList<FacetValue> Ratings,
    IReadOnlyList<FacetValue> ShopTypes,
    IReadOnlyList<FacetValue> Conditions,
    IReadOnlyDictionary<string, IReadOnlyList<FacetValue>> Attributes);

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
    Task<int> RecomputeAllAsync(CancellationToken ct);
}
