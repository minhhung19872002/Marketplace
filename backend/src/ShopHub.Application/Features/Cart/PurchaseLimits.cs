using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Cart;

/// <summary>
/// "Giới hạn mua mỗi người" (spec 3.4, II.4): a product may cap how many of it (all variants together) one buyer gets.
/// What counts: units in the buyer's cart plus units in their orders that are not cancelled or returned.
/// Guests only have the cart to go by; checkout re-counts inside the order transaction under a per-buyer lock.
/// </summary>
public sealed class PurchaseLimits(IApplicationDbContext db)
{
    private static readonly OrderStatus[] NotCounted = [OrderStatus.Cancelled, OrderStatus.Returned];

    public record Limit(int Max, int Bought);

    /// <summary>The limited products among the given ones, with what the buyer already bought.</summary>
    public async Task<Dictionary<Guid, Limit>> ForAsync(Guid? userId, IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        if (productIds.Count == 0) return [];
        var ids = productIds.Distinct().ToList();
        var caps = await db.Products.AsNoTracking().IgnoreQueryFilters().Where(p => ids.Contains(p.Id) && p.MaxPerBuyer != null)
            .Select(p => new { p.Id, Max = p.MaxPerBuyer!.Value }).ToListAsync(ct);
        if (caps.Count == 0) return [];
        var limited = caps.Select(c => c.Id).ToList();
        var bought = userId is { } uid
            ? await (from i in db.OrderItems.AsNoTracking()
                     join o in db.Orders.AsNoTracking() on i.OrderId equals o.Id
                     where o.BuyerId == uid && limited.Contains(i.ProductId) && !NotCounted.Contains(o.Status)
                     group i by i.ProductId into g
                     select new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.ProductId, x => x.Quantity, ct)
            : [];
        return caps.ToDictionary(c => c.Id, c => new Limit(c.Max, bought.GetValueOrDefault(c.Id)));
    }

    public static string Message(string productName, Limit limit) => limit.Bought > 0
        ? $"Mỗi người mua tối đa {limit.Max} sản phẩm \"{productName}\" (bạn đã mua {limit.Bought})."
        : $"Mỗi người mua tối đa {limit.Max} sản phẩm \"{productName}\".";

    /// <summary>Refuses (409) a cart change that would take the buyer past the product's limit.</summary>
    public async Task EnsureAsync(Guid? userId, Guid productId, string productName, int quantityAfterChange, CancellationToken ct)
    {
        var limits = await ForAsync(userId, [productId], ct);
        if (limits.TryGetValue(productId, out var limit) && quantityAfterChange + limit.Bought > limit.Max)
            throw new ConflictException(Message(productName, limit), "PURCHASE_LIMIT");
    }
}
