using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Reports;

public enum AdminReportKind
{
    GmvByTime,
    GmvByCategory,
    GmvByProvince,
    TopShops,
    TopProducts,
    Vouchers,
    FlashSales,
    Cancellations,
    Returns,
    Users,
    Funnel,
}

public enum ExportFormat
{
    Xlsx,
    Pdf,
}

/// <summary>
/// Platform reports (spec VI.10). Each one is a table + a chart over the same rows, exported as Excel / PDF from the
/// same table. Grouping by Vietnam day happens in memory on the projected rows of the period (≤ 366 days).
/// </summary>
public sealed class AdminReports(IApplicationDbContext db)
{
    private static readonly ReportColumn[] NoColumns = [];

    public Task<ReportResult> BuildAsync(AdminReportKind kind, ReportRange range, CancellationToken ct) => kind switch
    {
        AdminReportKind.GmvByTime => GmvByTimeAsync(range, ct),
        AdminReportKind.GmvByCategory => GmvByCategoryAsync(range, ct),
        AdminReportKind.GmvByProvince => GmvByProvinceAsync(range, ct),
        AdminReportKind.TopShops => TopShopsAsync(range, ct),
        AdminReportKind.TopProducts => TopProductsAsync(range, ct),
        AdminReportKind.Vouchers => VouchersAsync(range, ct),
        AdminReportKind.FlashSales => FlashSalesAsync(range, ct),
        AdminReportKind.Cancellations => CancellationsAsync(range, ct),
        AdminReportKind.Returns => ReturnsAsync(range, ct),
        AdminReportKind.Users => UsersAsync(range, ct),
        AdminReportKind.Funnel => FunnelAsync(range, ct),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private IQueryable<Order> PlacedIn(ReportRange r) => db.Orders.AsNoTracking().Placed().Where(o => o.CreatedAt >= r.Start && o.CreatedAt < r.End);

    private static ReportColumn Text(string t) => new(t, ReportCellKind.Text);
    private static ReportColumn Int(string t) => new(t, ReportCellKind.Integer);
    private static ReportColumn Money(string t) => new(t, ReportCellKind.Money);
    private static ReportColumn Pct(string t) => new(t, ReportCellKind.Percent);

    // ---------- GMV over time ----------

    private async Task<ReportResult> GmvByTimeAsync(ReportRange r, CancellationToken ct)
    {
        var rows = await PlacedIn(r).Select(o => new { o.CreatedAt, o.Subtotal, o.GrandTotal, o.BuyerId, o.Status }).ToListAsync(ct);
        var byBucket = rows.GroupBy(o => r.BucketOf(o.CreatedAt)).ToDictionary(g => g.Key);
        var table = new List<IReadOnlyList<object>>();
        var points = new List<ChartPoint>();
        foreach (var b in r.Buckets())
        {
            var g = byBucket.GetValueOrDefault(b);
            long orders = g?.Count() ?? 0, gmv = g?.Sum(o => o.Subtotal) ?? 0, paid = g?.Sum(o => o.GrandTotal) ?? 0;
            long buyers = g?.Select(o => o.BuyerId).Distinct().LongCount() ?? 0;
            long cancelled = g?.LongCount(o => o.Status == OrderStatus.Cancelled) ?? 0;
            table.Add([r.Label(b), orders, buyers, gmv, paid, ReportOrders.Bp(cancelled, orders)]);
            points.Add(new ChartPoint(r.Label(b), gmv, orders));
        }
        var totalOrders = rows.Count;
        return new ReportResult(
            new ReportTable("GMV theo thời gian", r.Describe(),
                [Text("Kỳ"), Int("Số đơn"), Int("Người mua"), Money("GMV (tiền hàng)"), Money("Khách trả"), Pct("Tỉ lệ huỷ")], table,
                ["Tổng", (long)totalOrders, rows.Select(o => o.BuyerId).Distinct().LongCount(), rows.Sum(o => o.Subtotal), rows.Sum(o => o.GrandTotal),
                    ReportOrders.Bp(rows.LongCount(o => o.Status == OrderStatus.Cancelled), totalOrders)]),
            new ChartDto("line", "GMV", "Số đơn", points));
    }

    // ---------- GMV by top-level category ----------

    private async Task<ReportResult> GmvByCategoryAsync(ReportRange r, CancellationToken ct)
    {
        var lines = await (from i in db.OrderItems.AsNoTracking()
                           join o in PlacedIn(r) on i.OrderId equals o.Id
                           join p in db.Products.IgnoreQueryFilters().AsNoTracking() on i.ProductId equals p.Id
                           select new { i.OrderId, p.CategoryId, i.Quantity, i.LineTotal }).ToListAsync(ct);
        var categories = await db.Categories.IgnoreQueryFilters().AsNoTracking().Select(c => new { c.Id, c.ParentId, c.Name }).ToDictionaryAsync(c => c.Id, ct);
        Guid Root(Guid id)
        {
            var c = categories[id];
            for (var guard = 0; c.ParentId is { } parent && categories.ContainsKey(parent) && guard < 10; guard++) c = categories[parent];
            return c.Id;
        }
        var total = lines.Sum(l => l.LineTotal);
        var groups = lines.GroupBy(l => Root(l.CategoryId))
            .Select(g => new { Name = categories[g.Key].Name, Orders = (long)g.Select(l => l.OrderId).Distinct().Count(), Qty = g.Sum(l => (long)l.Quantity), Gmv = g.Sum(l => l.LineTotal) })
            .OrderByDescending(g => g.Gmv).ThenBy(g => g.Name).ToList();
        return new ReportResult(
            new ReportTable("GMV theo ngành hàng", r.Describe(), [Text("Ngành"), Int("Số đơn"), Int("Số lượng"), Money("GMV"), Pct("Tỉ trọng")],
                groups.Select(g => (IReadOnlyList<object>)[g.Name, g.Orders, g.Qty, g.Gmv, ReportOrders.Bp(g.Gmv, total)]).ToList(),
                ["Tổng", (long)lines.Select(l => l.OrderId).Distinct().Count(), lines.Sum(l => (long)l.Quantity), total, total == 0 ? 0L : 10_000L]),
            new ChartDto("bar", "GMV", null, groups.Select(g => new ChartPoint(g.Name, g.Gmv)).ToList()));
    }

    // ---------- GMV by the buyer's province ----------

    private async Task<ReportResult> GmvByProvinceAsync(ReportRange r, CancellationToken ct)
    {
        var rows = await (from o in PlacedIn(r)
                          join c in db.CheckoutSessions.AsNoTracking() on o.CheckoutId equals c.Id
                          select new { o.Id, o.Subtotal, c.AddressSnapshot }).ToListAsync(ct);
        static string Province(string snapshot)
        {
            try
            {
                return JsonDocument.Parse(snapshot).RootElement.TryGetProperty("provinceCode", out var p) ? p.GetString() ?? "" : "";
            }
            catch (JsonException)
            {
                return "";
            }
        }
        var names = await db.AdminDivisions.AsNoTracking().Where(d => d.ParentCode == null).ToDictionaryAsync(d => d.Code, d => d.Name, ct);
        var total = rows.Sum(o => o.Subtotal);
        var groups = rows.GroupBy(o => Province(o.AddressSnapshot))
            .Select(g => new { Name = names.GetValueOrDefault(g.Key) ?? "Không rõ", Orders = (long)g.Count(), Gmv = g.Sum(o => o.Subtotal) })
            .OrderByDescending(g => g.Gmv).ThenBy(g => g.Name).ToList();
        return new ReportResult(
            new ReportTable("GMV theo tỉnh / thành của người mua", r.Describe(), [Text("Tỉnh / thành"), Int("Số đơn"), Money("GMV"), Pct("Tỉ trọng")],
                groups.Select(g => (IReadOnlyList<object>)[g.Name, g.Orders, g.Gmv, ReportOrders.Bp(g.Gmv, total)]).ToList(),
                ["Tổng", (long)rows.Count, total, total == 0 ? 0L : 10_000L]),
            new ChartDto("bar", "GMV", null, groups.Take(15).Select(g => new ChartPoint(g.Name, g.Gmv)).ToList()));
    }

    // ---------- top shops / products ----------

    private async Task<ReportResult> TopShopsAsync(ReportRange r, CancellationToken ct)
    {
        var groups = await PlacedIn(r).GroupBy(o => o.ShopId)
            .Select(g => new
            {
                ShopId = g.Key, Orders = (long)g.Count(), Gmv = g.Sum(o => o.Subtotal),
                Cancelled = (long)g.Count(o => o.Status == OrderStatus.Cancelled), Buyers = (long)g.Select(o => o.BuyerId).Distinct().Count(),
            })
            .OrderByDescending(g => g.Gmv).ThenBy(g => g.ShopId).Take(50).ToListAsync(ct);
        var ids = groups.Select(g => g.ShopId).ToList();
        var shops = await db.Shops.IgnoreQueryFilters().AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => new { s.Name, s.RatingAvg }, ct);
        return new ReportResult(
            new ReportTable("Top shop theo GMV", r.Describe(),
                [Text("Shop"), Int("Số đơn"), Int("Người mua"), Money("GMV"), Pct("Tỉ lệ huỷ"), Text("Đánh giá")],
                groups.Select(g => (IReadOnlyList<object>)[shops[g.ShopId].Name, g.Orders, g.Buyers, g.Gmv, ReportOrders.Bp(g.Cancelled, g.Orders),
                    shops[g.ShopId].RatingAvg.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"))]).ToList()),
            new ChartDto("bar", "GMV", null, groups.Take(10).Select(g => new ChartPoint(shops[g.ShopId].Name, g.Gmv)).ToList()));
    }

    private async Task<ReportResult> TopProductsAsync(ReportRange r, CancellationToken ct)
    {
        var groups = await (from i in db.OrderItems.AsNoTracking()
                            join o in PlacedIn(r) on i.OrderId equals o.Id
                            group i by new { i.ProductId, o.ShopId } into g
                            select new { g.Key.ProductId, g.Key.ShopId, Qty = g.Sum(i => (long)i.Quantity), Gmv = g.Sum(i => i.LineTotal), Orders = (long)g.Select(i => i.OrderId).Distinct().Count() })
            .OrderByDescending(g => g.Gmv).ThenBy(g => g.ProductId).Take(50).ToListAsync(ct);
        var productIds = groups.Select(g => g.ProductId).ToList();
        var shopIds = groups.Select(g => g.ShopId).Distinct().ToList();
        var products = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var shops = await db.Shops.IgnoreQueryFilters().AsNoTracking().Where(s => shopIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return new ReportResult(
            new ReportTable("Top sản phẩm theo GMV", r.Describe(), [Text("Sản phẩm"), Text("Shop"), Int("Số đơn"), Int("Đã bán"), Money("GMV")],
                groups.Select(g => (IReadOnlyList<object>)[products[g.ProductId], shops[g.ShopId], g.Orders, g.Qty, g.Gmv]).ToList()),
            new ChartDto("bar", "GMV", null, groups.Take(10).Select(g => new ChartPoint(products[g.ProductId], g.Gmv)).ToList()));
    }

    // ---------- marketing effectiveness ----------

    private async Task<ReportResult> VouchersAsync(ReportRange r, CancellationToken ct)
    {
        var usages = await (from u in db.VoucherUsages.AsNoTracking()
                            where u.RevertedAt == null && u.UsedAt >= r.Start && u.UsedAt < r.End
                            join v in db.Vouchers.IgnoreQueryFilters().AsNoTracking() on u.VoucherId equals v.Id
                            select new { u.VoucherId, v.Code, v.Name, v.Owner, u.UserId, u.Amount, u.OrderId, u.CheckoutId }).ToListAsync(ct);
        // Goods value the vouchers came with: the order (shop voucher) or every order of the checkout (platform voucher)
        var checkoutIds = usages.Select(u => u.CheckoutId).Distinct().ToList();
        var orderGoods = await db.Orders.AsNoTracking().Where(o => checkoutIds.Contains(o.CheckoutId)).Select(o => new { o.Id, o.CheckoutId, o.Subtotal }).ToListAsync(ct);
        long Goods(Guid? orderId, Guid checkoutId) =>
            orderId is { } id ? orderGoods.Where(o => o.Id == id).Sum(o => o.Subtotal) : orderGoods.Where(o => o.CheckoutId == checkoutId).Sum(o => o.Subtotal);
        var groups = usages.GroupBy(u => u.VoucherId).Select(g => new
        {
            g.First().Code, g.First().Name, Owner = g.First().Owner == VoucherOwner.Platform ? "Sàn" : "Shop",
            Uses = (long)g.Count(), Users = (long)g.Select(u => u.UserId).Distinct().Count(), Discount = g.Sum(u => u.Amount),
            Gmv = g.Select(u => (u.OrderId, u.CheckoutId)).Distinct().Sum(x => Goods(x.OrderId, x.CheckoutId)),
        }).OrderByDescending(g => g.Discount).ThenBy(g => g.Code).ToList();
        return new ReportResult(
            new ReportTable("Hiệu quả voucher", r.Describe(),
                [Text("Mã"), Text("Tên"), Text("Bên phát hành"), Int("Lượt dùng"), Int("Người dùng"), Money("Tổng giảm"), Money("GMV đơn có mã"), Pct("Chi phí / GMV")],
                groups.Select(g => (IReadOnlyList<object>)[g.Code, g.Name, g.Owner, g.Uses, g.Users, g.Discount, g.Gmv, ReportOrders.Bp(g.Discount, g.Gmv)]).ToList(),
                ["Tổng", "", "", groups.Sum(g => g.Uses), (long)usages.Select(u => u.UserId).Distinct().Count(), groups.Sum(g => g.Discount),
                    groups.Sum(g => g.Gmv), ReportOrders.Bp(groups.Sum(g => g.Discount), groups.Sum(g => g.Gmv))]),
            new ChartDto("bar", "Tổng giảm", "GMV đơn có mã", groups.Take(10).Select(g => new ChartPoint(g.Code, g.Discount, g.Gmv)).ToList()));
    }

    private async Task<ReportResult> FlashSalesAsync(ReportRange r, CancellationToken ct)
    {
        var items = await (from i in db.FlashSaleItems.AsNoTracking()
                           join s in db.FlashSaleSlots.AsNoTracking() on i.SlotId equals s.Id
                           where s.StartAt >= r.Start && s.StartAt < r.End && i.Status == FlashItemStatus.Approved
                           join p in db.Products.IgnoreQueryFilters().AsNoTracking() on i.ProductId equals p.Id
                           select new { i.SkuId, p.Name, i.FlashPrice, i.Quota, i.Sold, s.StartAt, s.EndAt, s.Owner }).ToListAsync(ct);
        var skuIds = items.Select(i => i.SkuId).Distinct().ToList();
        var sold = await (from oi in db.OrderItems.AsNoTracking()
                          join o in db.Orders.AsNoTracking().Placed() on oi.OrderId equals o.Id
                          where skuIds.Contains(oi.SkuId) && (oi.PriceSource == PriceProgramKind.ShopFlash || oi.PriceSource == PriceProgramKind.PlatformFlash)
                                && o.Status != OrderStatus.Cancelled
                          select new { oi.SkuId, o.CreatedAt, oi.Quantity, oi.LineTotal }).ToListAsync(ct);
        var rows = items.OrderBy(i => i.StartAt).ThenBy(i => i.Name).Select(i =>
        {
            var lines = sold.Where(s => s.SkuId == i.SkuId && s.CreatedAt >= i.StartAt && s.CreatedAt < i.EndAt).ToList();
            return new
            {
                Slot = $"{Common.VietnamTime.Format(i.StartAt)} ({(i.Owner == FlashSaleOwner.Platform ? "sàn" : "shop")})", i.Name, i.FlashPrice,
                Quota = (long)i.Quota, Sold = (long)i.Sold, Revenue = lines.Sum(l => l.LineTotal),
            };
        }).ToList();
        return new ReportResult(
            new ReportTable("Hiệu quả Flash Sale", r.Describe(),
                [Text("Khung giờ"), Text("Sản phẩm"), Money("Giá Flash Sale"), Int("Suất"), Int("Đã bán"), Pct("Tỉ lệ bán hết"), Money("Doanh thu")],
                rows.Select(x => (IReadOnlyList<object>)[x.Slot, x.Name, x.FlashPrice, x.Quota, x.Sold, ReportOrders.Bp(x.Sold, x.Quota), x.Revenue]).ToList(),
                ["Tổng", "", 0L, rows.Sum(x => x.Quota), rows.Sum(x => x.Sold), ReportOrders.Bp(rows.Sum(x => x.Sold), rows.Sum(x => x.Quota)), rows.Sum(x => x.Revenue)]),
            new ChartDto("bar", "Đã bán", "Suất", rows.Take(15).Select(x => new ChartPoint(x.Name, x.Sold, x.Quota)).ToList()));
    }

    // ---------- cancellations / returns ----------

    private static string ActorName(OrderActor? a) => a switch
    {
        OrderActor.Buyer => "Người mua",
        OrderActor.Seller => "Shop",
        OrderActor.Admin => "Sàn",
        OrderActor.Carrier => "Hãng vận chuyển",
        OrderActor.Gateway => "Cổng thanh toán",
        _ => "Hệ thống",
    };

    private async Task<ReportResult> CancellationsAsync(ReportRange r, CancellationToken ct)
    {
        var orders = await PlacedIn(r).Select(o => new { o.ShopId, o.Status, o.CancelReason, o.CancelledBy }).ToListAsync(ct);
        var perShop = orders.GroupBy(o => o.ShopId).ToDictionary(g => g.Key, g => (long)g.Count());
        var groups = orders.Where(o => o.Status == OrderStatus.Cancelled)
            .GroupBy(o => new { o.ShopId, Reason = o.CancelReason ?? "(không ghi lý do)", o.CancelledBy })
            .Select(g => new { g.Key.ShopId, g.Key.Reason, By = ActorName(g.Key.CancelledBy), Count = (long)g.Count() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.Reason).ToList();
        var shopIds = groups.Select(g => g.ShopId).Distinct().ToList();
        var shops = await db.Shops.IgnoreQueryFilters().AsNoTracking().Where(s => shopIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var byReason = groups.GroupBy(g => g.Reason).Select(g => new ChartPoint(g.Key, g.Sum(x => x.Count))).OrderByDescending(p => p.Value).Take(10).ToList();
        return new ReportResult(
            new ReportTable("Tỉ lệ huỷ theo shop & lý do", r.Describe(), [Text("Shop"), Text("Lý do"), Text("Bên huỷ"), Int("Số đơn huỷ"), Pct("Trên đơn của shop")],
                groups.Select(g => (IReadOnlyList<object>)[shops[g.ShopId], g.Reason, g.By, g.Count, ReportOrders.Bp(g.Count, perShop[g.ShopId])]).ToList(),
                ["Tổng", "", "", groups.Sum(g => g.Count), ReportOrders.Bp(groups.Sum(g => g.Count), orders.Count)]),
            new ChartDto("bar", "Số đơn huỷ", null, byReason));
    }

    private static string ReturnReasonName(ReturnReason r) => r switch
    {
        ReturnReason.MissingItem => "Thiếu hàng",
        ReturnReason.WrongItem => "Sai hàng",
        ReturnReason.Damaged => "Hư hỏng",
        ReturnReason.NotAsDescribed => "Không giống mô tả",
        ReturnReason.Counterfeit => "Hàng giả",
        _ => "Khác",
    };

    private async Task<ReportResult> ReturnsAsync(ReportRange r, CancellationToken ct)
    {
        var returns = await db.ReturnRequests.AsNoTracking().Where(x => x.CreatedAt >= r.Start && x.CreatedAt < r.End)
            .Select(x => new { x.ShopId, x.Reason, x.Status, x.OrderId, Refund = x.RefundAmount ?? 0 }).ToListAsync(ct);
        var delivered = await db.Orders.AsNoTracking().Where(o => o.DeliveredAt >= r.Start && o.DeliveredAt < r.End)
            .GroupBy(o => o.ShopId).Select(g => new { g.Key, Count = (long)g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var groups = returns.GroupBy(x => new { x.ShopId, x.Reason })
            .Select(g => new
            {
                g.Key.ShopId, Reason = ReturnReasonName(g.Key.Reason), Count = (long)g.Count(), Refunded = (long)g.Count(x => x.Status == ReturnStatus.Refunded),
                Amount = g.Sum(x => x.Refund), Orders = (long)g.Select(x => x.OrderId).Distinct().Count(),
            })
            .OrderByDescending(g => g.Count).ThenBy(g => g.Reason).ToList();
        var shopIds = groups.Select(g => g.ShopId).Distinct().ToList();
        var shops = await db.Shops.IgnoreQueryFilters().AsNoTracking().Where(s => shopIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return new ReportResult(
            new ReportTable("Trả hàng / hoàn tiền theo shop & lý do", r.Describe(),
                [Text("Shop"), Text("Lý do"), Int("Yêu cầu"), Int("Đã hoàn tiền"), Money("Số tiền hoàn"), Pct("Trên đơn đã giao của shop")],
                groups.Select(g => (IReadOnlyList<object>)[shops[g.ShopId], g.Reason, g.Count, g.Refunded, g.Amount,
                    ReportOrders.Bp(g.Orders, delivered.GetValueOrDefault(g.ShopId))]).ToList(),
                ["Tổng", "", groups.Sum(g => g.Count), groups.Sum(g => g.Refunded), groups.Sum(g => g.Amount),
                    ReportOrders.Bp(returns.Select(x => x.OrderId).Distinct().Count(), delivered.Values.Sum())]),
            new ChartDto("bar", "Yêu cầu", null,
                groups.GroupBy(g => g.Reason).Select(g => new ChartPoint(g.Key, g.Sum(x => x.Count))).OrderByDescending(p => p.Value).ToList()));
    }

    // ---------- people ----------

    private async Task<ReportResult> UsersAsync(ReportRange r, CancellationToken ct)
    {
        var newUsers = await db.Users.AsNoTracking().Where(u => u.CreatedAt >= r.Start && u.CreatedAt < r.End).Select(u => u.CreatedAt).ToListAsync(ct);
        var buyers = await PlacedIn(r).Select(o => new { o.BuyerId, o.CreatedAt }).ToListAsync(ct);
        var ids = buyers.Select(b => b.BuyerId).Distinct().ToList();
        // A buyer's very first placed order decides "mới" vs "quay lại"
        var firsts = await db.Orders.AsNoTracking().Placed().Where(o => ids.Contains(o.BuyerId)).GroupBy(o => o.BuyerId)
            .Select(g => new { g.Key, First = g.Min(o => o.CreatedAt) }).ToDictionaryAsync(g => g.Key, g => g.First, ct);
        var rows = new List<IReadOnlyList<object>>();
        var points = new List<ChartPoint>();
        foreach (var b in r.Buckets())
        {
            var inBucket = buyers.Where(x => r.BucketOf(x.CreatedAt) == b).Select(x => x.BuyerId).Distinct().ToList();
            long first = inBucket.LongCount(id => r.BucketOf(firsts[id]) == b), returning = inBucket.Count - first;
            long registered = newUsers.LongCount(t => r.BucketOf(t) == b);
            rows.Add([r.Label(b), registered, (long)inBucket.Count, first, returning, ReportOrders.Bp(returning, inBucket.Count)]);
            points.Add(new ChartPoint(r.Label(b), first, returning));
        }
        var firstInPeriod = ids.LongCount(id => firsts[id] >= r.Start);
        return new ReportResult(
            new ReportTable("Người dùng mới & quay lại", r.Describe(),
                [Text("Kỳ"), Int("Đăng ký mới"), Int("Người mua"), Int("Mua lần đầu"), Int("Mua lại"), Pct("Tỉ lệ quay lại")], rows,
                ["Cả kỳ", (long)newUsers.Count, (long)ids.Count, firstInPeriod, ids.Count - firstInPeriod, ReportOrders.Bp(ids.Count - firstInPeriod, ids.Count)]),
            new ChartDto("bar", "Mua lần đầu", "Mua lại", points));
    }

    /// <summary>
    /// Conversion funnel of signed-in buyers (guests cannot be followed across devices): viewed a product → added to
    /// cart → placed a checkout → paid (online paid, Ví, or COD delivered).
    /// </summary>
    private async Task<ReportResult> FunnelAsync(ReportRange r, CancellationToken ct)
    {
        var viewed = await db.ProductViews.AsNoTracking().Where(v => v.UserId != null && v.ViewedAt >= r.Start && v.ViewedAt < r.End)
            .Select(v => v.UserId!.Value).Distinct().CountAsync(ct);
        var carted = await db.CartAdds.AsNoTracking().Where(a => a.UserId != null && a.AddedAt >= r.Start && a.AddedAt < r.End)
            .Select(a => a.UserId!.Value).Distinct().CountAsync(ct);
        var ordered = await db.CheckoutSessions.AsNoTracking().Where(c => c.CreatedAt >= r.Start && c.CreatedAt < r.End)
            .Select(c => c.UserId).Distinct().CountAsync(ct);
        var paid = await db.Orders.AsNoTracking().Where(o => o.CreatedAt >= r.Start && o.CreatedAt < r.End
                                                             && (o.PaymentStatus != OrderPaymentStatus.Unpaid
                                                                 || (o.PaymentMethod == PaymentMethod.Cod && o.DeliveredAt != null)))
            .Select(o => o.BuyerId).Distinct().CountAsync(ct);
        (string Step, long Count)[] steps = [("Xem sản phẩm", viewed), ("Thêm vào giỏ", carted), ("Đặt hàng", ordered), ("Trả tiền", paid)];
        var rows = steps.Select((s, i) => (IReadOnlyList<object>)[s.Step, s.Count, i == 0 ? 10_000L : ReportOrders.Bp(s.Count, steps[i - 1].Count),
            ReportOrders.Bp(s.Count, steps[0].Count)]).ToList();
        return new ReportResult(
            new ReportTable("Phễu chuyển đổi (người dùng đã đăng nhập)", r.Describe(), [Text("Bước"), Int("Số người"), Pct("So với bước trước"), Pct("So với bước đầu")], rows),
            new ChartDto("funnel", "Số người", null, steps.Select(s => new ChartPoint(s.Step, s.Count)).ToList()));
    }
}

public record AdminReportQuery(AdminReportKind Kind, DateOnly? From, DateOnly? To, Granularity Granularity) : IRequest<ReportResult>;

public sealed class AdminReportHandler(AdminReports reports, IClock clock) : IRequestHandler<AdminReportQuery, ReportResult>
{
    public Task<ReportResult> Handle(AdminReportQuery q, CancellationToken ct) =>
        reports.BuildAsync(q.Kind, ReportRange.Of(q.From, q.To, q.Granularity, clock.UtcNow), ct);
}

public record ReportFile(byte[] Content, string ContentType, string FileName);

public record AdminReportExportQuery(AdminReportKind Kind, DateOnly? From, DateOnly? To, Granularity Granularity, ExportFormat Format) : IRequest<ReportFile>;

public sealed class AdminReportExportHandler(AdminReports reports, IReportDocuments documents, IClock clock) : IRequestHandler<AdminReportExportQuery, ReportFile>
{
    public async Task<ReportFile> Handle(AdminReportExportQuery q, CancellationToken ct)
    {
        var range = ReportRange.Of(q.From, q.To, q.Granularity, clock.UtcNow);
        var result = await reports.BuildAsync(q.Kind, range, ct);
        return ReportFiles.Of(result.Table, q.Format, $"bao-cao-{q.Kind.ToString().ToLowerInvariant()}-{range.From:yyyyMMdd}-{range.To:yyyyMMdd}", documents);
    }
}

public static class ReportFiles
{
    public static ReportFile Of(ReportTable table, ExportFormat format, string name, IReportDocuments documents) => format == ExportFormat.Pdf
        ? new ReportFile(documents.Pdf(table), "application/pdf", name + ".pdf")
        : new ReportFile(documents.Excel(table), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name + ".xlsx");
}
