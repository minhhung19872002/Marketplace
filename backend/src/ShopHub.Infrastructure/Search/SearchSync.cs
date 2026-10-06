using ShopHub.Domain.Sales;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopHub.Application.Abstractions;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Shops;
using ShopHub.Domain.SystemConfig;
using ShopHub.Infrastructure.Outbox;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Search;

/// <summary>
/// Any SaveChanges touching a product, its SKUs/media/attributes or a shop records a search-sync outbox message in the
/// same transaction — no business handler can forget to reindex. (Set-based ExecuteUpdate bypasses this; those call
/// sites enqueue explicitly.)
/// </summary>
public sealed class SearchSyncInterceptor(IClock clock) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Collect(DbContext context)
    {
        var changed = context.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        var products = new HashSet<Guid>();
        var shops = new HashSet<Guid>();
        foreach (var e in changed)
        {
            switch (e.Entity)
            {
                case Product p: products.Add(p.Id); break;
                case Sku s: products.Add(s.ProductId); break;
                case ProductMedia m: products.Add(m.ProductId); break;
                case ProductAttribute a: products.Add(a.ProductId); break;
                case Shop s when e.State != EntityState.Added: shops.Add(s.Id); break;
            }
        }
        if (products.Count == 0 && shops.Count == 0) return;

        var outbox = context.Set<OutboxMessage>();
        if (products.Count > 0)
            outbox.Add(new OutboxMessage(OutboxTypes.SearchSyncProducts, JsonSerializer.Serialize(new SearchSyncProductsPayload(products.ToList()), Json), clock.UtcNow));
        foreach (var shopId in shops)
            outbox.Add(new OutboxMessage(OutboxTypes.SearchSyncShop, JsonSerializer.Serialize(new SearchSyncShopPayload(shopId), Json), clock.UtcNow));
    }
}

public sealed class SearchSyncProductsHandler(ISearchIndexer indexer) : IOutboxHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Type => OutboxTypes.SearchSyncProducts;

    public Task HandleAsync(string payload, CancellationToken ct) =>
        indexer.SyncProductsAsync((JsonSerializer.Deserialize<SearchSyncProductsPayload>(payload, Json) ?? throw new InvalidOperationException("Tin rỗng.")).ProductIds, ct);
}

public sealed class SearchSyncShopHandler(ISearchIndexer indexer, ICounterRecomputer counters) : IOutboxHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Type => OutboxTypes.SearchSyncShop;

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var shopId = (JsonSerializer.Deserialize<SearchSyncShopPayload>(payload, Json) ?? throw new InvalidOperationException("Tin rỗng.")).ShopId;
        await counters.RecomputeShopProductCountAsync(shopId, ct);
        await indexer.SyncShopAsync(shopId, ct);
    }
}

/// <summary>Denormalised counters are always recomputed from their source rows (spec trap 2), never += 1.</summary>
public sealed class SqlCounterRecomputer(ShopHubDbContext db) : ICounterRecomputer
{
    public Task RecomputeProductLikesAsync(Guid productId, CancellationToken ct) =>
        db.Products.IgnoreQueryFilters().Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LikeCount, p => db.Wishlists.Count(w => w.ProductId == p.Id)), ct);

    public Task RecomputeShopFollowersAsync(Guid shopId, CancellationToken ct) =>
        db.Shops.IgnoreQueryFilters().Where(s => s.Id == shopId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.FollowerCount, s => db.ShopFollowers.Count(f => f.ShopId == s.Id)), ct);

    public Task RecomputeShopProductCountAsync(Guid shopId, CancellationToken ct) =>
        db.Shops.IgnoreQueryFilters().Where(s => s.Id == shopId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.ProductCount,
                s => db.Products.Count(p => p.ShopId == s.Id && p.Status == ProductStatus.Active)), ct);

    private static readonly OrderStatus[] Sold = [OrderStatus.Shipping, OrderStatus.Delivered, OrderStatus.Completed];

    public Task RecomputeProductSalesAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct) =>
        db.Products.IgnoreQueryFilters().Where(p => productIds.Contains(p.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SoldCount, p => db.OrderItems
                .Where(i => i.ProductId == p.Id && db.Orders.Any(o => o.Id == i.OrderId && Sold.Contains(o.Status)))
                .Sum(i => (int?)i.Quantity) ?? 0), ct);

    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task RunJobAsync() => await RecomputeAllAsync(CancellationToken.None);

    public async Task<int> RecomputeAllAsync(CancellationToken ct)
    {
        var n = await db.Products.IgnoreQueryFilters()
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.LikeCount, p => db.Wishlists.Count(w => w.ProductId == p.Id))
                .SetProperty(p => p.ViewCount, p => db.ProductViews.Count(v => v.ProductId == p.Id))
                .SetProperty(p => p.SoldCount, p => db.OrderItems
                    .Where(i => i.ProductId == p.Id && db.Orders.Any(o => o.Id == i.OrderId && Sold.Contains(o.Status)))
                    .Sum(i => (int?)i.Quantity) ?? 0), ct);
        n += await db.Shops.IgnoreQueryFilters()
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.FollowerCount, s => db.ShopFollowers.Count(f => f.ShopId == s.Id))
                .SetProperty(s => s.ProductCount, s => db.Products.Count(p => p.ShopId == s.Id && p.Status == ProductStatus.Active)), ct);
        return n;
    }
}
