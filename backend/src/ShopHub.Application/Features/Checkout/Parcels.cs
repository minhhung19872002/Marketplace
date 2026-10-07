using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Common;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Checkout;

/// <summary>One line of a shop's order as far as parcels go: which product, its kho gửi, its size.</summary>
public record ParcelLine(Guid ProductId, Guid? ProductWarehouseId, ParcelItem Item);

/// <summary>A parcel of one shop's order: the warehouse it leaves from and the products in it.</summary>
public record PlannedParcel(int No, ShopWarehouse Warehouse, IReadOnlyList<Guid> ProductIds, int WeightG);

/// <summary>The fee of a parcel for the carrier chosen for the shop.</summary>
public record PlanParcel(int No, Guid WarehouseId, string WarehouseName, string ProvinceCode, int WeightG, long Fee, IReadOnlyList<Guid> ProductIds);

/// <summary>
/// Đa kho (spec 3.5 "mỗi kho gửi của shop một kiện nếu bật đa kho"): with the shop's switch on, every product leaves from
/// its own warehouse and the order becomes one parcel per warehouse; off (or the product's warehouse gone) → the default
/// pickup warehouse. Parcel 1 is the default warehouse's, the rest follow by warehouse name — the same order every time.
/// </summary>
public static class Parcels
{
    public static Task<List<ShopWarehouse>> WarehousesAsync(IApplicationDbContext db, Guid shopId, CancellationToken ct) =>
        db.ShopWarehouses.AsNoTracking().Where(w => w.ShopId == shopId)
            .OrderByDescending(w => w.IsPickupDefault).ThenBy(w => w.Name).ThenBy(w => w.Id).ToListAsync(ct);

    public static ShopWarehouse ShipFrom(bool multiWarehouse, IReadOnlyList<ShopWarehouse> warehouses, Guid? productWarehouseId) =>
        (multiWarehouse && productWarehouseId is { } id ? warehouses.FirstOrDefault(w => w.Id == id) : null) ?? warehouses[0];

    public static List<PlannedParcel> Plan(bool multiWarehouse, IReadOnlyList<ShopWarehouse> warehouses, IReadOnlyList<ParcelLine> lines)
    {
        if (warehouses.Count == 0) throw new BusinessRuleException("Shop chưa có kho lấy hàng.");
        var order = warehouses.Select((w, i) => (w.Id, i)).ToDictionary(x => x.Id, x => x.i);
        return lines.GroupBy(l => ShipFrom(multiWarehouse, warehouses, l.ProductWarehouseId))
            .OrderBy(g => order[g.Key.Id])
            .Select((g, i) => new PlannedParcel(i + 1, g.Key, g.Select(l => l.ProductId).Distinct().ToList(),
                ShippingCalculator.ChargeableWeightG(g.Select(l => l.Item))))
            .ToList();
    }

    /// <summary>
    /// Options for a shop's parcels together: a carrier is offered only when it serves every parcel; its fee is the sum
    /// of the parcels' fees, its delivery the slowest parcel's.
    /// </summary>
    public static IReadOnlyList<ShippingOption> Combine(IReadOnlyList<IReadOnlyList<ShippingOption>> perParcel)
    {
        if (perParcel.Count == 0) return [];
        if (perParcel.Count == 1) return perParcel[0];
        return perParcel[0]
            .Where(o => perParcel.All(p => p.Any(x => x.Code == o.Code)))
            .Select(o =>
            {
                var all = perParcel.Select(p => p.First(x => x.Code == o.Code)).ToList();
                return o with
                {
                    Fee = all.Sum(x => x.Fee),
                    Days = all.Max(x => x.Days),
                    ExpectedDate = all.Max(x => x.ExpectedDate),
                    SupportsCod = all.All(x => x.SupportsCod),
                };
            }).ToList();
    }

    /// <summary>The order's free-shipping discount shared over its parcels by fee (largest remainder, to the đồng).</summary>
    public static IReadOnlyList<long> ShareDiscount(long shippingDiscount, IReadOnlyList<long> fees)
    {
        if (fees.Count == 0) return [];
        if (shippingDiscount == 0 || fees.Sum() == 0) return fees.Select(_ => 0L).ToList();
        return Money.Vnd(shippingDiscount).Allocate(fees.ToList()).Select(m => m.Value).ToList();
    }

    /// <summary>
    /// The COD each parcel's carrier collects: the order's total over the parcels by what the buyer pays for each
    /// (its lines after discounts + its net shipping), largest remainder — the parts add up to the order's total.
    /// </summary>
    public static IReadOnlyList<long> ShareCod(long grandTotal, IReadOnlyList<long> parcelDue)
    {
        if (parcelDue.Count == 1) return [grandTotal];
        if (grandTotal == 0 || parcelDue.Sum() == 0) return parcelDue.Select((_, i) => i == 0 ? grandTotal : 0L).ToList();
        return Money.Vnd(grandTotal).Allocate(parcelDue.ToList()).Select(m => m.Value).ToList();
    }
}

/// <summary>A parcel of a placed order with its lines, as the seller ships it.</summary>
public record OrderParcel(int No, ShopWarehouse Warehouse, IReadOnlyList<Domain.Sales.OrderItem> Items, long ShippingFee, long ShippingDiscount);

public static class OrderParcels
{
    /// <summary>The order's parcels; an order placed before đa kho is one parcel from the default pickup warehouse.</summary>
    public static List<OrderParcel> Of(Domain.Sales.Order order, IReadOnlyList<ShopWarehouse> warehouses)
    {
        if (order.Packages.Count == 0) return [new OrderParcel(1, warehouses[0], order.Items, order.ShippingFee, order.ShippingDiscount)];
        return order.Packages.OrderBy(p => p.No).Select(p => new OrderParcel(p.No, warehouses.FirstOrDefault(w => w.Id == p.WarehouseId) ?? warehouses[0],
            order.Items.Where(i => i.PackageNo == p.No).ToList(), p.ShippingFee, p.ShippingDiscount)).ToList();
    }
}
