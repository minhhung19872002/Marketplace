using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Chat;
using ShopHub.Application.Features.Seller;
using ShopHub.Application.Security;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Finance;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Reports;

// ---------- VI.1 admin overview ----------

public record KpiDto(long Gmv, long Orders, long NewBuyers, long NewShops, long CancelRateBp, long ReturnRateBp, long FeeRevenue);

public record PendingTasksDto(int ShopsToReview, int ProductsToReview, int OpenDisputes, int PendingWithdrawals, int OpenProductReports, int OpenReviewReports);

public record AdminOverviewDto(string Period, KpiDto Current, KpiDto Previous, IReadOnlyList<ChartPoint> GmvSeries, IReadOnlyList<ChartPoint> OrderSeries,
    PendingTasksDto Pending, IReadOnlyList<string>? Warnings = null);

public record AdminOverviewQuery(DateOnly? From, DateOnly? To, Granularity Granularity) : IRequest<AdminOverviewDto>;

/// <summary>Tổng quan (spec VI.1): the period's KPIs next to the previous period's, series per day / week / month, and the work queue.</summary>
public sealed class AdminOverviewHandler(IApplicationDbContext db, IClock clock, IWorkingCalendar calendar) : IRequestHandler<AdminOverviewQuery, AdminOverviewDto>
{
    private static readonly LedgerAccountType[] FeeAccounts = [LedgerAccountType.FeeFixed, LedgerAccountType.FeePayment, LedgerAccountType.FeeService];

    public async Task<AdminOverviewDto> Handle(AdminOverviewQuery request, CancellationToken ct)
    {
        var range = ReportRange.Of(request.From, request.To, request.Granularity, clock.UtcNow);
        var current = await KpisAsync(range, ct);
        var previous = await KpisAsync(range.Previous(), ct);

        var rows = await db.Orders.AsNoTracking().Placed().Where(o => o.CreatedAt >= range.Start && o.CreatedAt < range.End)
            .Select(o => new { o.CreatedAt, o.Subtotal }).ToListAsync(ct);
        var byBucket = rows.GroupBy(o => range.BucketOf(o.CreatedAt)).ToDictionary(g => g.Key);
        var buckets = range.Buckets();
        var gmv = buckets.Select(b => new ChartPoint(range.Label(b), byBucket.GetValueOrDefault(b)?.Sum(o => o.Subtotal) ?? 0)).ToList();
        var orders = buckets.Select(b => new ChartPoint(range.Label(b), byBucket.GetValueOrDefault(b)?.Count() ?? 0)).ToList();

        var pending = new PendingTasksDto(
            await db.Shops.CountAsync(s => s.Status == ShopStatus.PendingReview, ct),
            await db.Products.CountAsync(p => p.Status == ProductStatus.PendingReview, ct),
            await db.Disputes.CountAsync(d => d.ClosedAt == null, ct),
            await db.Withdrawals.CountAsync(w => w.Status == WithdrawalStatus.Pending, ct),
            await db.ProductReports.CountAsync(r => r.Status == ProductReportStatus.Open, ct),
            await db.ReviewReports.CountAsync(r => r.ResolvedAt == null, ct));
        // Configuration that will go wrong soon: deadlines next year would ignore Tết if the list stops this year
        var warnings = new List<string>();
        var nextYear = VietnamTime.Today(clock.UtcNow).Year + 1;
        if (!(await calendar.HolidaysAsync(ct)).Any(d => d.Year == nextYear))
            warnings.Add($"Danh sách ngày nghỉ lễ (LOGISTICS.HOLIDAYS) chưa có ngày nào của năm {nextYear}: hạn chuẩn bị hàng và ngày giao dự kiến năm sau sẽ tính cả ngày lễ. Bổ sung ở Tham số hệ thống → Vận chuyển.");
        return new AdminOverviewDto(range.Describe(), current, previous, gmv, orders, pending, warnings);
    }

    private async Task<KpiDto> KpisAsync(ReportRange r, CancellationToken ct)
    {
        var placed = db.Orders.AsNoTracking().Placed().Where(o => o.CreatedAt >= r.Start && o.CreatedAt < r.End);
        var orders = await placed.LongCountAsync(ct);
        var gmv = await placed.SumAsync(o => (long?)o.Subtotal, ct) ?? 0;
        var cancelled = await placed.LongCountAsync(o => o.Status == OrderStatus.Cancelled, ct);
        var returned = await placed.LongCountAsync(o => db.ReturnRequests.Any(x => x.OrderId == o.Id), ct);
        var newBuyers = await db.Users.LongCountAsync(u => u.CreatedAt >= r.Start && u.CreatedAt < r.End, ct);
        var newShops = await db.Shops.LongCountAsync(s => s.ApprovedAt >= r.Start && s.ApprovedAt < r.End, ct);
        // Fee income = credits − debits (reversals of refunded orders) on the platform's fee accounts
        var fees = await (from e in db.LedgerEntries.AsNoTracking()
                          join a in db.LedgerAccounts.AsNoTracking() on e.AccountId equals a.Id
                          where FeeAccounts.Contains(a.Type) && e.PostedAt >= r.Start && e.PostedAt < r.End
                          select e.Direction == LedgerDirection.Credit ? e.Amount : -e.Amount).SumAsync(x => (long?)x, ct) ?? 0;
        return new KpiDto(gmv, orders, newBuyers, newShops, ReportOrders.Bp(cancelled, orders), ReportOrders.Bp(returned, orders), fees);
    }
}

// ---------- III.8 seller analytics ----------

public record SellerPeriodDto(long Sales, long Orders, long Buyers, long Views, long ConversionBp);

public record SellerSeriesPoint(string Label, long Sales, long Orders, long Buyers, long Views);

public record SellerTopProductDto(Guid ProductId, string Name, long Sold, long Sales, long Views, long ConversionBp);

public record TrafficSourceDto(ViewSource Source, string Name, long Views, long ShareBp);

public record SellerPerformanceDto(long FailedRateBp, long LateDeliveryRateBp, int ChatResponseRatePercent, string ChatResponseTime, PenaltyStatus Penalty);

public record SellerAnalyticsDto(string Period, SellerPeriodDto Current, SellerPeriodDto Previous, IReadOnlyList<SellerSeriesPoint> Series,
    IReadOnlyList<SellerTopProductDto> TopProducts, IReadOnlyList<TrafficSourceDto> Traffic, SellerPerformanceDto Performance);

public record SellerAnalyticsQuery(Guid ShopId, DateOnly? From, DateOnly? To, Granularity Granularity) : IRequest<SellerAnalyticsDto>;

/// <summary>
/// Dữ liệu &amp; phân tích of one shop (spec III.8). Sales = goods value after the shop's own discount of placed orders;
/// conversion = buyers / distinct viewers of the shop's products; every figure has its previous-period twin.
/// </summary>
public sealed class SellerAnalytics(IApplicationDbContext db)
{
    public static string SourceName(ViewSource s) => s switch
    {
        ViewSource.Home => "Trang chủ / Gợi ý",
        ViewSource.Search => "Tìm kiếm",
        ViewSource.Category => "Danh mục",
        ViewSource.Shop => "Trang shop",
        ViewSource.Campaign => "Sự kiện / Flash Sale",
        ViewSource.External => "Bên ngoài (mạng xã hội, quảng cáo)",
        ViewSource.Direct => "Truy cập trực tiếp",
        _ => "Khác",
    };

    private IQueryable<Order> Orders(Guid shopId, ReportRange r) =>
        db.Orders.AsNoTracking().Placed().Where(o => o.ShopId == shopId && o.CreatedAt >= r.Start && o.CreatedAt < r.End);

    private IQueryable<ProductView> Views(Guid shopId, ReportRange r) =>
        db.ProductViews.AsNoTracking().Where(v => v.ViewedAt >= r.Start && v.ViewedAt < r.End
                                                  && db.Products.IgnoreQueryFilters().Any(p => p.Id == v.ProductId && p.ShopId == shopId));

    public async Task<SellerPeriodDto> PeriodAsync(Guid shopId, ReportRange r, CancellationToken ct)
    {
        var orders = Orders(shopId, r).Where(o => o.Status != OrderStatus.Cancelled);
        var sales = await orders.SumAsync(o => (long?)(o.Subtotal - o.ShopDiscount), ct) ?? 0;
        var count = await orders.LongCountAsync(ct);
        var buyers = await orders.Select(o => o.BuyerId).Distinct().LongCountAsync(ct);
        var views = await Views(shopId, r).LongCountAsync(ct);
        var viewers = await Views(shopId, r).Select(v => v.UserId.HasValue ? v.UserId.Value.ToString() : v.SessionKey).Distinct().LongCountAsync(ct);
        return new SellerPeriodDto(sales, count, buyers, views, ReportOrders.Bp(buyers, viewers));
    }

    public async Task<IReadOnlyList<SellerSeriesPoint>> SeriesAsync(Guid shopId, ReportRange r, CancellationToken ct)
    {
        var orders = await Orders(shopId, r).Where(o => o.Status != OrderStatus.Cancelled)
            .Select(o => new { o.CreatedAt, Sales = o.Subtotal - o.ShopDiscount, o.BuyerId }).ToListAsync(ct);
        var views = await Views(shopId, r).Select(v => v.ViewedAt).ToListAsync(ct);
        var o = orders.GroupBy(x => r.BucketOf(x.CreatedAt)).ToDictionary(g => g.Key);
        var v = views.GroupBy(r.BucketOf).ToDictionary(g => g.Key, g => (long)g.Count());
        return r.Buckets().Select(b => new SellerSeriesPoint(r.Label(b), o.GetValueOrDefault(b)?.Sum(x => x.Sales) ?? 0, o.GetValueOrDefault(b)?.Count() ?? 0,
            o.GetValueOrDefault(b)?.Select(x => x.BuyerId).Distinct().LongCount() ?? 0, v.GetValueOrDefault(b))).ToList();
    }

    public async Task<SellerAnalyticsDto> BuildAsync(Guid shopId, ReportRange r, ChatPerformanceService chat, ShopPenaltyService penalties, CancellationToken ct)
    {
        var top = await (from i in db.OrderItems.AsNoTracking()
                         join o in Orders(shopId, r).Where(x => x.Status != OrderStatus.Cancelled) on i.OrderId equals o.Id
                         group i by i.ProductId into g
                         select new { ProductId = g.Key, Sold = g.Sum(i => (long)i.Quantity), Sales = g.Sum(i => i.LineTotal), Buyers = (long)g.Select(i => i.OrderId).Distinct().Count() })
            .OrderByDescending(x => x.Sales).ThenBy(x => x.ProductId).Take(10).ToListAsync(ct);
        var topIds = top.Select(t => t.ProductId).ToList();
        var names = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(p => topIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var topViews = await Views(shopId, r).Where(v => topIds.Contains(v.ProductId)).GroupBy(v => v.ProductId)
            .Select(g => new { g.Key, Count = (long)g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        var traffic = await Views(shopId, r).GroupBy(v => v.Source).Select(g => new { g.Key, Count = (long)g.Count() }).ToListAsync(ct);
        var totalViews = traffic.Sum(t => t.Count);

        // Performance: failed = cancelled by the shop / system or not delivered; late = delivered after the promised day
        var all = await Orders(shopId, r).Select(o => new { o.Status, o.CancelledBy }).ToListAsync(ct);
        var failed = all.LongCount(o => (o.Status == OrderStatus.Cancelled && o.CancelledBy is OrderActor.Seller or OrderActor.System)
                                        || o.Status is OrderStatus.DeliveryFailed or OrderStatus.Returning or OrderStatus.Returned);
        var delivered = await (from s in db.Shipments.AsNoTracking()
                               join o in db.Orders.AsNoTracking() on s.OrderId equals o.Id
                               where o.ShopId == shopId && s.Direction == ShipmentDirection.Outbound && o.DeliveredAt >= r.Start && o.DeliveredAt < r.End
                               select new { o.DeliveredAt, s.ExpectedDeliveryAt }).ToListAsync(ct);
        var late = delivered.LongCount(d => d.DeliveredAt > d.ExpectedDeliveryAt);
        var chatPerf = await chat.OfShopAsync(shopId, ct);

        return new SellerAnalyticsDto(r.Describe(), await PeriodAsync(shopId, r, ct), await PeriodAsync(shopId, r.Previous(), ct), await SeriesAsync(shopId, r, ct),
            top.Select(t => new SellerTopProductDto(t.ProductId, names[t.ProductId], t.Sold, t.Sales, topViews.GetValueOrDefault(t.ProductId),
                ReportOrders.Bp(t.Buyers, topViews.GetValueOrDefault(t.ProductId)))).ToList(),
            traffic.OrderByDescending(t => t.Count).Select(t => new TrafficSourceDto(t.Key, SourceName(t.Key), t.Count, ReportOrders.Bp(t.Count, totalViews))).ToList(),
            new SellerPerformanceDto(ReportOrders.Bp(failed, all.Count), ReportOrders.Bp(late, delivered.Count), chatPerf.ResponseRatePercent, chatPerf.ResponseTime,
                await penalties.OfShopAsync(shopId, ct)));
    }
}

public sealed class SellerAnalyticsHandler(SellerAccess access, SellerAnalytics analytics, ChatPerformanceService chat, ShopPenaltyService penalties, IClock clock)
    : IRequestHandler<SellerAnalyticsQuery, SellerAnalyticsDto>
{
    public async Task<SellerAnalyticsDto> Handle(SellerAnalyticsQuery q, CancellationToken ct)
    {
        await access.RequireAsync(q.ShopId, ShopPermissions.OrderView, ct);
        return await analytics.BuildAsync(q.ShopId, ReportRange.Of(q.From, q.To, q.Granularity, clock.UtcNow), chat, penalties, ct);
    }
}

public record SellerAnalyticsExportQuery(Guid ShopId, DateOnly? From, DateOnly? To, Granularity Granularity) : IRequest<ReportFile>;

/// <summary>The period series as Excel (spec III.8 "xuất Excel"), totals next to the previous period.</summary>
public sealed class SellerAnalyticsExportHandler(IApplicationDbContext db, SellerAccess access, SellerAnalytics analytics, IReportDocuments documents, IClock clock)
    : IRequestHandler<SellerAnalyticsExportQuery, ReportFile>
{
    public async Task<ReportFile> Handle(SellerAnalyticsExportQuery q, CancellationToken ct)
    {
        await access.RequireAsync(q.ShopId, ShopPermissions.OrderView, ct);
        var range = ReportRange.Of(q.From, q.To, q.Granularity, clock.UtcNow);
        var shop = await db.Shops.AsNoTracking().Where(s => s.Id == q.ShopId).Select(s => s.Name).SingleAsync(ct);
        var series = await analytics.SeriesAsync(q.ShopId, range, ct);
        var cur = await analytics.PeriodAsync(q.ShopId, range, ct);
        var table = new ReportTable($"Dữ liệu bán hàng — {shop}", range.Describe(),
            [new("Kỳ", ReportCellKind.Text), new("Doanh số", ReportCellKind.Money), new("Số đơn", ReportCellKind.Integer),
                new("Người mua", ReportCellKind.Integer), new("Lượt xem", ReportCellKind.Integer)],
            series.Select(p => (IReadOnlyList<object>)[p.Label, p.Sales, p.Orders, p.Buyers, p.Views]).ToList(),
            ["Tổng", cur.Sales, cur.Orders, cur.Buyers, cur.Views]);
        return ReportFiles.Of(table, ExportFormat.Xlsx, $"du-lieu-ban-hang-{range.From:yyyyMMdd}-{range.To:yyyyMMdd}", documents);
    }
}
