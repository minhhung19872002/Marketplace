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

    private static bool VoucherOfferChanged(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry e, Domain.Promo.Voucher v) =>
        e.State != EntityState.Modified
        || new[] { nameof(v.IsActive), nameof(v.IsPublic), nameof(v.StartAt), nameof(v.EndAt), nameof(v.TotalQuota) }.Any(n => e.Property(n).IsModified)
        || (e.Property(nameof(v.UsedCount)).IsModified && v.TotalQuota is { } quota
            && (v.UsedCount >= quota || (int)e.Property(nameof(v.UsedCount)).OriginalValue! >= quota));

    private void Collect(DbContext context)
    {
        var changed = context.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        var products = new HashSet<Guid>();
        var shops = new HashSet<Guid>();
        var skus = new HashSet<Guid>();
        var reindexAll = false;
        foreach (var e in changed)
        {
            switch (e.Entity)
            {
                case Product p: products.Add(p.Id); break;
                case Sku s: products.Add(s.ProductId); break;
                case ProductMedia m: products.Add(m.ProductId); break;
                case ProductAttribute a: products.Add(a.ProductId); break;
                case Shop s when e.State != EntityState.Added: shops.Add(s.Id); break;
                // The index carries the price in force: a programme added or stopped changes it
                case Domain.Promo.PriceProgram pp: skus.Add(pp.SkuId); break;
                // Facets: the shop's carriers / COD, its warehouses (nơi bán), its running vouchers
                case ShopShippingChannel ch: shops.Add(ch.ShopId); break;
                case ShopWarehouse w when e.State != EntityState.Added: shops.Add(w.ShopId); break;
                // (not on every use: only when it starts / stops being offered — on, public, period, quota used up)
                case Domain.Promo.Voucher { ShopId: { } voucherShop } v when VoucherOfferChanged(e, v): shops.Add(voucherShop); break;
                case Domain.Logistics.Carrier when e.State == EntityState.Modified: reindexAll = true; break;
            }
        }
        if (products.Count == 0 && shops.Count == 0 && skus.Count == 0 && !reindexAll) return;

        var outbox = context.Set<OutboxMessage>();
        if (products.Count > 0)
            outbox.Add(new OutboxMessage(OutboxTypes.SearchSyncProducts, JsonSerializer.Serialize(new SearchSyncProductsPayload(products.ToList()), Json), clock.UtcNow));
        foreach (var shopId in shops)
            outbox.Add(new OutboxMessage(OutboxTypes.SearchSyncShop, JsonSerializer.Serialize(new SearchSyncShopPayload(shopId), Json), clock.UtcNow));
        if (skus.Count > 0)
            outbox.Add(new OutboxMessage(OutboxTypes.SearchSyncSkus, JsonSerializer.Serialize(new SearchSyncSkusPayload(skus.ToList()), Json), clock.UtcNow));
        if (reindexAll) outbox.Add(new OutboxMessage(OutboxTypes.SearchReindexAll, "{}", clock.UtcNow));
    }
}

public sealed class SearchReindexAllHandler(ISearchIndexer indexer) : IOutboxHandler
{
    public string Type => OutboxTypes.SearchReindexAll;

    public Task HandleAsync(string payload, CancellationToken ct) => indexer.ReindexAllAsync(ct);
}

public sealed class SearchSyncSkusHandler(ShopHubDbContext db, ISearchIndexer indexer) : IOutboxHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Type => OutboxTypes.SearchSyncSkus;

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var skus = (JsonSerializer.Deserialize<SearchSyncSkusPayload>(payload, Json) ?? throw new InvalidOperationException("Tin rỗng.")).SkuIds;
        var products = await db.Skus.AsNoTracking().IgnoreQueryFilters().Where(s => skus.Contains(s.Id)).Select(s => s.ProductId).Distinct().ToListAsync(ct);
        if (products.Count > 0) await indexer.SyncProductsAsync(products, ct);
    }
}

/// <summary>
/// Programmes start and end on the clock, with no write to notice: every minute, the SKUs whose programme crossed its
/// start or end in the last few minutes are re-indexed (overlapping windows; a repeat is harmless).
/// </summary>
public sealed class PriceIndexJob(ShopHubDbContext db, IClock clock)
{
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 300)]
    [Hangfire.AutomaticRetry(Attempts = 0)]
    public Task RunJobAsync() => RunAsync(CancellationToken.None);

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var since = now.AddMinutes(-5);
        var skus = await db.PricePrograms.AsNoTracking()
            .Where(p => (p.StartAt > since && p.StartAt <= now) || (p.EndAt > since && p.EndAt <= now))
            .Select(p => p.SkuId).Distinct().ToListAsync(ct);
        var shops = await db.Vouchers.AsNoTracking()
            .Where(v => v.ShopId != null && ((v.StartAt > since && v.StartAt <= now) || (v.EndAt > since && v.EndAt <= now)))
            .Select(v => v.ShopId!.Value).Distinct().ToListAsync(ct);
        if (skus.Count == 0 && shops.Count == 0) return 0;
        if (skus.Count > 0)
            db.OutboxMessages.Add(new OutboxMessage(OutboxTypes.SearchSyncSkus, JsonSerializer.Serialize(new SearchSyncSkusPayload(skus), Json), now));
        foreach (var shopId in shops)
            db.OutboxMessages.Add(new OutboxMessage(OutboxTypes.SearchSyncShop, JsonSerializer.Serialize(new SearchSyncShopPayload(shopId), Json), now));
        await db.SaveChangesAsync(ct);
        return skus.Count + shops.Count;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
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

    // Units handed to the carrier, minus units given back through refunded returns
    public Task RecomputeProductSalesAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct) =>
        db.Products.IgnoreQueryFilters().Where(p => productIds.Contains(p.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SoldCount, p => (db.OrderItems
                .Where(i => i.ProductId == p.Id && db.Orders.Any(o => o.Id == i.OrderId && Sold.Contains(o.Status)))
                .Sum(i => (int?)i.Quantity) ?? 0) - (db.ReturnItems
                .Where(r => db.OrderItems.Any(i => i.Id == r.OrderItemId && i.ProductId == p.Id)
                            && db.ReturnRequests.Any(x => x.Id == r.ReturnId && x.Status == ReturnStatus.Refunded))
                .Sum(r => (int?)r.Quantity) ?? 0)), ct);

    public async Task RecomputeRatingsAsync(IReadOnlyCollection<Guid> productIds, IReadOnlyCollection<Guid> shopIds, CancellationToken ct)
    {
        await db.Products.IgnoreQueryFilters().Where(p => productIds.Contains(p.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.RatingCount, p => db.Reviews.Count(r => r.ProductId == p.Id && !r.IsHidden))
                .SetProperty(p => p.RatingAvg, p => db.Reviews.Where(r => r.ProductId == p.Id && !r.IsHidden).Average(r => (double?)r.Rating) ?? 0), ct);
        await db.Shops.IgnoreQueryFilters().Where(x => shopIds.Contains(x.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RatingCount, x => db.Reviews.Count(r => r.ShopId == x.Id && !r.IsHidden))
                .SetProperty(x => x.RatingAvg, x => db.Reviews.Where(r => r.ShopId == x.Id && !r.IsHidden).Average(r => (double?)r.Rating) ?? 0), ct);
    }

    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task RunJobAsync() => await RecomputeAllAsync(CancellationToken.None);

    public async Task<int> RecomputeAllAsync(CancellationToken ct)
    {
        // A whole-catalogue pass (1M products) outlives the default 30 s command timeout
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(15));
        // A product with no source row and all-zero counters would be recomputed to the same zeros: skip it,
        // so the pass only rewrites rows that can change (the perf catalogue is mostly untouched products)
        var n = await db.Products.IgnoreQueryFilters()
            .Where(p => p.SoldCount != 0 || p.RatingCount != 0 || p.RatingAvg != 0 || p.LikeCount != 0 || p.ViewCount != 0
                        || db.OrderItems.Any(i => i.ProductId == p.Id) || db.Reviews.Any(r => r.ProductId == p.Id)
                        || db.Wishlists.Any(w => w.ProductId == p.Id) || db.ProductViews.Any(v => v.ProductId == p.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.LikeCount, p => db.Wishlists.Count(w => w.ProductId == p.Id))
                .SetProperty(p => p.ViewCount, p => db.ProductViews.Count(v => v.ProductId == p.Id))
                .SetProperty(p => p.SoldCount, p => (db.OrderItems
                    .Where(i => i.ProductId == p.Id && db.Orders.Any(o => o.Id == i.OrderId && Sold.Contains(o.Status)))
                    .Sum(i => (int?)i.Quantity) ?? 0) - (db.ReturnItems
                    .Where(r => db.OrderItems.Any(i => i.Id == r.OrderItemId && i.ProductId == p.Id)
                                && db.ReturnRequests.Any(x => x.Id == r.ReturnId && x.Status == ReturnStatus.Refunded))
                    .Sum(r => (int?)r.Quantity) ?? 0))
                .SetProperty(p => p.RatingCount, p => db.Reviews.Count(r => r.ProductId == p.Id && !r.IsHidden))
                .SetProperty(p => p.RatingAvg, p => db.Reviews.Where(r => r.ProductId == p.Id && !r.IsHidden).Average(r => (double?)r.Rating) ?? 0), ct);
        n += await db.Shops.IgnoreQueryFilters()
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.FollowerCount, s => db.ShopFollowers.Count(f => f.ShopId == s.Id))
                .SetProperty(s => s.ProductCount, s => db.Products.Count(p => p.ShopId == s.Id && p.Status == ProductStatus.Active))
                .SetProperty(s => s.RatingCount, s => db.Reviews.Count(r => r.ShopId == s.Id && !r.IsHidden))
                .SetProperty(s => s.RatingAvg, s => db.Reviews.Where(r => r.ShopId == s.Id && !r.IsHidden).Average(r => (double?)r.Rating) ?? 0), ct);
        return n;
    }
}
