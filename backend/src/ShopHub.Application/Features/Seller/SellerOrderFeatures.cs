using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Checkout;
using ShopHub.Application.Features.Orders;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Seller;

public enum ShopOrderTab
{
    All,
    Unpaid,          // chờ thanh toán
    ToConfirm,       // chờ xác nhận
    ToShip,          // chờ lấy hàng
    Shipping,        // đang giao
    Delivered,       // đã giao / hoàn thành
    Cancelled,
    CancelRequests,  // yêu cầu huỷ đang chờ
    Failed,          // giao thất bại / hoàn về
}

public record ShopOrderRowDto(
    Guid Id,
    string Code,
    DateTimeOffset CreatedAt,
    OrderStatus Status,
    string StatusLabel,
    PaymentMethod PaymentMethod,
    OrderPaymentStatus PaymentStatus,
    string BuyerName,
    int ItemCount,
    string? FirstItemName,
    string? FirstItemImage,
    long GrandTotal,
    string CarrierCode,
    string? TrackingNo,
    ShipmentStatus? ShipmentStatus,
    bool HasCancelRequest,
    bool LabelPrinted,
    int ParcelCount = 1);

public record ShopOrderDetailDto(Orders.OrderDetailDto Order, string BuyerName, string? SellerNote, DateOnly? ShipDeadline);

internal static class ShopOrderQueries
{
    public static IQueryable<Order> Tab(IApplicationDbContext db, IQueryable<Order> q, ShopOrderTab tab) => tab switch
    {
        ShopOrderTab.Unpaid => q.Where(o => o.Status == OrderStatus.PendingPayment),
        ShopOrderTab.ToConfirm => q.Where(o => o.Status == OrderStatus.PendingConfirmation),
        ShopOrderTab.ToShip => q.Where(o => o.Status == OrderStatus.ReadyToShip),
        ShopOrderTab.Shipping => q.Where(o => o.Status == OrderStatus.Shipping),
        ShopOrderTab.Delivered => q.Where(o => o.Status == OrderStatus.Delivered || o.Status == OrderStatus.Completed),
        ShopOrderTab.Cancelled => q.Where(o => o.Status == OrderStatus.Cancelled),
        ShopOrderTab.CancelRequests => q.Where(o => db.OrderCancelRequests.Any(r => r.OrderId == o.Id && r.Status == CancelRequestStatus.Pending)),
        ShopOrderTab.Failed => q.Where(o => o.Status == OrderStatus.DeliveryFailed || o.Status == OrderStatus.Returning || o.Status == OrderStatus.Returned),
        _ => q,
    };

    public static async Task<Order> OwnOrderAsync(IApplicationDbContext db, Guid shopId, Guid orderId, CancellationToken ct) =>
        await db.Orders.Include(o => o.Items).ThenInclude(i => i.Discounts).Include(o => o.History)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.ShopId == shopId, ct) ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
}

public record ListShopOrdersQuery(
    Guid ShopId,
    ShopOrderTab Tab = ShopOrderTab.All,
    string? Q = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Carrier = null,
    PaymentMethod? PaymentMethod = null,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<ShopOrderRowDto>>, IPagedRequest;

public sealed class ListShopOrdersValidator : AbstractValidator<ListShopOrdersQuery>
{
    public ListShopOrdersValidator()
    {
        this.ApplyPagingRules();
        RuleFor(x => x).Must(x => x.From is null || x.To is null || x.From <= x.To).WithName("from")
            .WithMessage("Khoảng ngày không hợp lệ: ngày bắt đầu phải trước ngày kết thúc.");
    }
}

public sealed class ListShopOrdersHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<ListShopOrdersQuery, PagedResult<ShopOrderRowDto>>
{
    public async Task<PagedResult<ShopOrderRowDto>> Handle(ListShopOrdersQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.OrderView, ct);
        var q = ShopOrderQueries.Tab(db, db.Orders.AsNoTracking().Where(o => o.ShopId == request.ShopId), request.Tab);
        if (request.From is { } from) q = q.Where(o => o.CreatedAt >= from);
        if (request.To is { } to) q = q.Where(o => o.CreatedAt <= to);
        if (!string.IsNullOrWhiteSpace(request.Carrier)) q = q.Where(o => o.CarrierCode == request.Carrier);
        if (request.PaymentMethod is { } method) q = q.Where(o => o.PaymentMethod == method);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var term = request.Q.Trim();
            var upper = term.ToUpper();
            var lower = term.ToLower();
            q = q.Where(o => o.Code == upper
                             || db.Shipments.Any(s => s.OrderId == o.Id && s.TrackingNo == upper)
                             || db.Users.Any(u => u.Id == o.BuyerId && u.FullName.ToLower().Contains(lower))
                             || o.Items.Any(i => i.NameSnapshot.ToLower().Contains(lower)));
        }

        var page = await q.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).ToPagedResultAsync(o => new
        {
            Order = o,
            Buyer = db.Users.Where(u => u.Id == o.BuyerId).Select(u => u.FullName).FirstOrDefault(),
            Items = o.Items.Sum(i => i.Quantity),
            First = o.Items.OrderBy(i => i.Id).Select(i => new { i.NameSnapshot, i.ImageSnapshot }).FirstOrDefault(),
            Shipment = db.Shipments.Where(s => s.OrderId == o.Id && s.Direction == ShipmentDirection.Outbound)
                .OrderByDescending(s => s.CreatedAt).Select(s => new { s.TrackingNo, s.Status, s.LabelPrintedAt }).FirstOrDefault(),
            CancelRequest = db.OrderCancelRequests.Any(r => r.OrderId == o.Id && r.Status == CancelRequestStatus.Pending),
            Parcels = o.Packages.Count(),
        }, request, ct);

        return new PagedResult<ShopOrderRowDto>(page.Items.Select(r => new ShopOrderRowDto(r.Order.Id, r.Order.Code, r.Order.CreatedAt, r.Order.Status,
            OrderStateMachine.Label(r.Order.Status), r.Order.PaymentMethod, r.Order.PaymentStatus, r.Buyer ?? "", r.Items, r.First?.NameSnapshot,
            r.First?.ImageSnapshot, r.Order.GrandTotal, r.Order.CarrierCode, r.Shipment?.TrackingNo, r.Shipment?.Status, r.CancelRequest,
            r.Shipment?.LabelPrintedAt is not null, Math.Max(1, r.Parcels))).ToList(), page.TotalCount, page.Page, page.PageSize);
    }
}

public record GetShopOrderQuery(Guid ShopId, Guid OrderId) : IRequest<ShopOrderDetailDto>;

public sealed class GetShopOrderHandler(IApplicationDbContext db, SellerAccess access, ISystemParameters parameters, IClock clock)
    : IRequestHandler<GetShopOrderQuery, ShopOrderDetailDto>
{
    public async Task<ShopOrderDetailDto> Handle(GetShopOrderQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.OrderView, ct);
        var order = await ShopOrderQueries.OwnOrderAsync(db, request.ShopId, request.OrderId, ct);
        var detail = await OrderDetails.BuildAsync(db, parameters, clock, order, ct);
        var buyer = await db.Users.Where(u => u.Id == order.BuyerId).Select(u => u.FullName).FirstAsync(ct);
        DateOnly? deadline = order.Status is OrderStatus.PendingConfirmation or OrderStatus.ReadyToShip
            ? VietnamTime.AddWorkingDays(VietnamTime.Today(order.PaidAt ?? order.CreatedAt), (int)await parameters.GetIntAsync(ParameterKeys.OrderShipDeadlineDays, ct),
                new HashSet<DateOnly>())
            : null;
        return new ShopOrderDetailDto(detail, buyer, order.SellerNote, deadline);
    }
}

// ---------- prepare (confirm + book the carrier) ----------

public record PrepareResultDto(Guid OrderId, string Code, bool Ok, string? TrackingNo, string? Error);

/// <summary>"Chuẩn bị hàng" for one or many orders: confirm, then book pickup (time slot) or drop-off → tracking number.</summary>
public record PrepareOrdersCommand(Guid ShopId, IReadOnlyList<Guid> OrderIds, PickupMethod PickupMethod, string? PickupSlot) : IRequest<IReadOnlyList<PrepareResultDto>>;

public sealed class PrepareOrdersValidator : AbstractValidator<PrepareOrdersCommand>
{
    public PrepareOrdersValidator()
    {
        RuleFor(x => x.OrderIds).NotEmpty().WithMessage("Chọn ít nhất một đơn.").Must(i => i.Count <= 100).WithMessage("Mỗi lần tối đa 100 đơn.");
        RuleFor(x => x.PickupSlot).NotEmpty().When(x => x.PickupMethod == PickupMethod.Pickup).WithMessage("Vui lòng chọn khung giờ lấy hàng.");
    }
}

public sealed class PrepareOrdersHandler(
    IApplicationDbContext db,
    SellerAccess access,
    OrderLocks locks,
    IEnumerable<ICarrier> carriers,
    ISystemParameters parameters,
    IOutbox outbox,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<PrepareOrdersCommand, IReadOnlyList<PrepareResultDto>>
{
    public async Task<IReadOnlyList<PrepareResultDto>> Handle(PrepareOrdersCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.OrderManage, ct);
        if (request.PickupMethod == PickupMethod.Pickup)
        {
            var slots = JsonSerializer.Deserialize<List<string>>(await parameters.GetStringAsync(ParameterKeys.OrderPickupSlots, ct)) ?? [];
            if (!slots.Contains(request.PickupSlot!)) throw new ConflictException("Khung giờ lấy hàng không hợp lệ.", "BAD_SLOT");
        }
        var warehouses = await Parcels.WarehousesAsync(db, request.ShopId, ct);
        if (warehouses.Count == 0) throw new ConflictException("Shop chưa có kho lấy hàng.", "NO_WAREHOUSE");

        var results = new List<PrepareResultDto>();
        foreach (var orderId in request.OrderIds.Distinct())
        {
            var code = await db.Orders.Where(o => o.Id == orderId && o.ShopId == request.ShopId).Select(o => o.Code).FirstOrDefaultAsync(ct);
            if (code is null)
            {
                results.Add(new PrepareResultDto(orderId, "", false, null, "Không tìm thấy đơn hàng."));
                continue;
            }
            try
            {
                results.Add(new PrepareResultDto(orderId, code, true, await PrepareOneAsync(orderId, warehouses, request, ct), null));
            }
            catch (Exception ex) when (ex is ConflictException or Domain.Common.BusinessRuleException)
                // (a real carrier refusing the booking is a CarrierUnavailableException, i.e. a ConflictException)
            {
                db.ClearTracking();
                results.Add(new PrepareResultDto(orderId, code, false, null, ex.Message));
            }
        }
        return results;
    }

    private async Task<string> PrepareOneAsync(Guid orderId, IReadOnlyList<Domain.Shops.ShopWarehouse> warehouses, PrepareOrdersCommand request,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var tx = await db.BeginTransactionAsync(ct);
        var order = await locks.LockAsync(orderId, ct);
        if (await db.OrderCancelRequests.AnyAsync(r => r.OrderId == order.Id && r.Status == CancelRequestStatus.Pending, ct))
            throw new ConflictException($"Đơn {order.Code} đang có yêu cầu huỷ, vui lòng xử lý trước.", "CANCEL_PENDING");
        OrderStateMachine.Transition(order, OrderStatus.ReadyToShip, OrderActor.Seller, currentUser.UserId, "Shop đã xác nhận và chuẩn bị hàng", now);

        var checkout = await db.CheckoutSessions.AsNoTracking().SingleAsync(c => c.Id == order.CheckoutId, ct);
        var receiver = CarrierParties.FromSnapshot(checkout.AddressSnapshot);
        var productIds = order.Items.Select(i => i.ProductId).ToList();
        var dims = await db.Products.IgnoreQueryFilters().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.WeightG, p.LengthMm, p.WidthMm, p.HeightMm }).ToDictionaryAsync(p => p.Id, ct);
        var skuIds = order.Items.Select(i => i.SkuId).ToList();
        var skuWeights = await db.Skus.IgnoreQueryFilters().Where(s => skuIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.WeightG, ct);
        int WeightOf(OrderItem i) => skuWeights.GetValueOrDefault(i.SkuId) ?? dims[i.ProductId].WeightG;

        var carrier = await db.Carriers.AsNoTracking().SingleAsync(c => c.Code == order.CarrierCode, ct);
        var provider = carriers.FirstOrDefault(c => c.Provider == carrier.Provider)
                       ?? throw new ConflictException("Đơn vị vận chuyển của đơn này hiện không khả dụng.", "NO_CARRIER");

        // One shipment per parcel, from its own warehouse (orders before đa kho: one parcel from the default pickup)
        var parcels = OrderParcels.Of(order, warehouses);
        var cods = Parcels.ShareCod(order.GrandTotal, parcels.Select(p => p.Items.Sum(i => i.PaidAmount) + p.ShippingFee - p.ShippingDiscount).ToList());
        var trackings = new List<string>();
        for (var k = 0; k < parcels.Count; k++)
        {
            var p = parcels[k];
            var fromProvince = p.Warehouse.ProvinceCode;
            var toProvince = receiver?.Point.ProvinceCode ?? fromProvince;
            var weight = ShippingCalculator.ChargeableWeightG(p.Items.Select(i =>
                new ParcelItem(WeightOf(i), dims[i.ProductId].LengthMm, dims[i.ProductId].WidthMm, dims[i.ProductId].HeightMm, i.Quantity)));
            var cod = order.PaymentMethod == PaymentMethod.Cod ? cods[k] : 0;
            var tracking = await provider.CreateShipmentAsync(carrier,
                new CarrierParcel(order.Id, parcels.Count > 1 ? $"{order.Code}-{p.No}" : order.Code, fromProvince, toProvince, weight, cod,
                    request.PickupMethod, request.PickupSlot, CarrierParties.FromWarehouse(p.Warehouse), receiver, p.Items.Sum(i => i.LineTotal),
                    p.Items.Select(i => new CarrierItem(i.NameSnapshot, i.Quantity, WeightOf(i))).ToList(), order.BuyerNote), ct);
            var days = carrier.DaysFor(ShippingCalculator.ZoneOf(fromProvince, toProvince));
            var expected = VietnamTime.AddWorkingDays(VietnamTime.Today(now), days, new HashSet<DateOnly>());
            var expectedAt = new DateTimeOffset(expected.ToDateTime(new TimeOnly(18, 0)), TimeSpan.FromHours(7)).ToUniversalTime();
            var shipment = new Shipment(order.Id, carrier.Code, tracking, ShipmentDirection.Outbound, p.ShippingFee, cod, weight, request.PickupMethod,
                request.PickupMethod == PickupMethod.Pickup ? request.PickupSlot : null, expectedAt, now);
            shipment.ForPackage(p.No);
            db.Shipments.Add(shipment);
            trackings.Add(tracking);
        }
        var all = string.Join(", ", trackings);
        outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.Confirmed, all));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return all;
    }
}

public record PickupSlotsQuery : IRequest<IReadOnlyList<string>>;

public sealed class PickupSlotsHandler(ISystemParameters parameters) : IRequestHandler<PickupSlotsQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(PickupSlotsQuery request, CancellationToken ct) =>
        JsonSerializer.Deserialize<List<string>>(await parameters.GetStringAsync(ParameterKeys.OrderPickupSlots, ct)) ?? [];
}

// ---------- documents ----------

public record ShippingLabelsQuery(Guid ShopId, IReadOnlyList<Guid> OrderIds, LabelSize Size) : IRequest<byte[]>;

public sealed class ShippingLabelsValidator : AbstractValidator<ShippingLabelsQuery>
{
    public ShippingLabelsValidator() =>
        RuleFor(x => x.OrderIds).NotEmpty().WithMessage("Chọn ít nhất một đơn.").Must(i => i.Count <= 200).WithMessage("Mỗi lần in tối đa 200 đơn.");
}

/// <summary>The carrier's own label for one order (GHTK prints its own); 404 when the carrier relies on ShopHub's label.</summary>
public record CarrierLabelQuery(Guid ShopId, Guid OrderId, int? PackageNo = null) : IRequest<byte[]>;

public sealed class CarrierLabelHandler(IApplicationDbContext db, SellerAccess access, IEnumerable<ICarrier> carriers)
    : IRequestHandler<CarrierLabelQuery, byte[]>
{
    public async Task<byte[]> Handle(CarrierLabelQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.OrderView, ct);
        var shipment = await db.Shipments.AsNoTracking()
                           .Where(s => s.OrderId == request.OrderId && s.Direction == ShipmentDirection.Outbound
                                       && (request.PackageNo == null || s.PackageNo == request.PackageNo)
                                       && db.Orders.Any(o => o.Id == request.OrderId && o.ShopId == request.ShopId))
                           .OrderBy(s => s.PackageNo).ThenByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException("Đơn hàng chưa có vận đơn.");
        var carrier = await db.Carriers.AsNoTracking().SingleAsync(c => c.Code == shipment.CarrierCode, ct);
        var provider = carriers.FirstOrDefault(c => c.Provider == carrier.Provider);
        var pdf = provider is null ? null : await provider.GetLabelAsync(carrier, shipment.TrackingNo, ct);
        return pdf ?? throw new NotFoundException("Đơn vị vận chuyển này dùng phiếu giao hàng của ShopHub.");
    }
}

public sealed class ShippingLabelsHandler(IApplicationDbContext db, SellerAccess access, IShippingDocuments documents, IClock clock)
    : IRequestHandler<ShippingLabelsQuery, byte[]>
{
    public async Task<byte[]> Handle(ShippingLabelsQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.OrderView, ct);
        var shop = await db.Shops.AsNoTracking().SingleAsync(s => s.Id == request.ShopId, ct);
        var warehouses = await Parcels.WarehousesAsync(db, shop.Id, ct);
        var codes = warehouses.SelectMany(w => new[] { w.WardCode, w.DistrictCode, w.ProvinceCode }).Distinct().ToList();
        var names = await db.AdminDivisions.AsNoTracking().Where(d => codes.Contains(d.Code)).ToDictionaryAsync(d => d.Code, d => d.Name, ct);
        string SenderOf(Domain.Shops.ShopWarehouse w) => string.Join(", ", new[] { w.Street, names.GetValueOrDefault(w.WardCode),
            names.GetValueOrDefault(w.DistrictCode), names.GetValueOrDefault(w.ProvinceCode) }.Where(x => !string.IsNullOrEmpty(x)));

        var ids = request.OrderIds.Distinct().ToList();
        var orders = await db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Packages).AsSplitQuery()
            .Where(o => o.ShopId == shop.Id && ids.Contains(o.Id)).ToListAsync(ct);
        if (orders.Count != ids.Count) throw new NotFoundException("Không tìm thấy đơn hàng.");
        var shipments = await db.Shipments.Where(s => ids.Contains(s.OrderId) && s.Direction == ShipmentDirection.Outbound && s.Status != ShipmentStatus.Cancelled)
            .ToListAsync(ct);
        var carriers = await db.Carriers.AsNoTracking().ToDictionaryAsync(c => c.Code, c => c.Name, ct);
        var checkouts = await db.CheckoutSessions.AsNoTracking().Where(c => orders.Select(o => o.CheckoutId).Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.AddressSnapshot, ct);

        var labels = new List<ShippingLabel>();
        foreach (var o in orders.OrderBy(o => o.Code))
        {
            var own = shipments.Where(x => x.OrderId == o.Id).OrderBy(x => x.PackageNo).ThenBy(x => x.Id).ToList();
            if (own.Count == 0) throw new ConflictException($"Đơn {o.Code} chưa có vận đơn, vui lòng chuẩn bị hàng trước.", "NO_SHIPMENT");
            var address = JsonDocument.Parse(checkouts[o.CheckoutId]).RootElement;
            // One label per parcel: its own warehouse as sender and only its lines
            var parcels = OrderParcels.Of(o, warehouses);
            foreach (var s in own)
            {
                var parcel = parcels.FirstOrDefault(p => p.No == s.PackageNo) ?? parcels[0];
                var code = own.Count > 1 ? $"{o.Code} · kiện {s.PackageNo}/{own.Count}" : o.Code;
                labels.Add(new ShippingLabel(s.TrackingNo, carriers.GetValueOrDefault(s.CarrierCode) ?? s.CarrierCode, code, shop.Name, parcel.Warehouse.Phone,
                    SenderOf(parcel.Warehouse), address.GetProperty("receiverName").GetString() ?? "", address.GetProperty("phone").GetString() ?? "",
                    address.GetProperty("fullAddress").GetString() ?? "", s.CodAmount, s.WeightG,
                    (own.Count > 1 ? parcel.Items : o.Items).OrderBy(i => i.Id).Select(i => new ShippingLabelItem(i.NameSnapshot, i.VariantSnapshot, i.Quantity))
                    .ToList(), o.BuyerNote, s.CreatedAt));
                s.MarkPrinted(clock.UtcNow);
            }
        }
        await db.SaveChangesAsync(ct);
        return documents.RenderLabels(labels, request.Size);
    }
}

public record PickingListQuery(Guid ShopId, IReadOnlyList<Guid> OrderIds) : IRequest<byte[]>;

public sealed class PickingListHandler(IApplicationDbContext db, SellerAccess access, IShippingDocuments documents, IClock clock)
    : IRequestHandler<PickingListQuery, byte[]>
{
    public async Task<byte[]> Handle(PickingListQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.OrderView, ct);
        var ids = request.OrderIds.Distinct().ToList();
        if (ids.Count is 0 or > 200) throw new ConflictException("Chọn từ 1 đến 200 đơn để in phiếu soạn hàng.", "BAD_SELECTION");
        var shopName = await db.Shops.Where(s => s.Id == request.ShopId).Select(s => s.Name).SingleAsync(ct);
        var rows = await (from o in db.Orders.AsNoTracking()
                          from i in o.Items
                          where o.ShopId == request.ShopId && ids.Contains(o.Id)
                          select new { i.SkuId, i.NameSnapshot, i.VariantSnapshot, i.Quantity, o.Code }).ToListAsync(ct);
        if (rows.Select(r => r.Code).Distinct().Count() != ids.Count) throw new NotFoundException("Không tìm thấy đơn hàng.");
        var sellerSkus = await db.Skus.IgnoreQueryFilters().Where(s => rows.Select(r => r.SkuId).Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.SellerSku, ct);
        var lines = rows.GroupBy(r => r.SkuId)
            .Select(g => new PickingLine(sellerSkus.GetValueOrDefault(g.Key), g.First().NameSnapshot, g.First().VariantSnapshot, g.Sum(r => r.Quantity),
                g.Select(r => r.Code).Distinct().OrderBy(c => c).ToList()))
            .OrderBy(l => l.Name).ThenBy(l => l.Variant).ToList();
        return documents.RenderPickingList(shopName, lines, clock.UtcNow);
    }
}

public record ExportShopOrdersQuery(Guid ShopId, ShopOrderTab Tab, DateTimeOffset? From, DateTimeOffset? To) : IRequest<byte[]>;

public sealed class ExportShopOrdersHandler(IApplicationDbContext db, SellerAccess access, IShippingDocuments documents) : IRequestHandler<ExportShopOrdersQuery, byte[]>
{
    public const int MaxRows = 5000;

    public async Task<byte[]> Handle(ExportShopOrdersQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.OrderView, ct);
        var q = ShopOrderQueries.Tab(db, db.Orders.AsNoTracking().Where(o => o.ShopId == request.ShopId), request.Tab);
        if (request.From is { } from) q = q.Where(o => o.CreatedAt >= from);
        if (request.To is { } to) q = q.Where(o => o.CreatedAt <= to);
        if (await q.CountAsync(ct) > MaxRows) throw new ConflictException($"Tối đa {MaxRows} đơn mỗi lần xuất, vui lòng thu hẹp khoảng ngày.", "TOO_MANY");
        var rows = await q.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Select(o => new
        {
            o.Code, o.CreatedAt, o.Status, o.Subtotal, o.ShopDiscount, o.ShippingFee, o.GrandTotal, o.PaymentMethod,
            Buyer = db.Users.Where(u => u.Id == o.BuyerId).Select(u => u.FullName).FirstOrDefault(),
            Address = db.CheckoutSessions.Where(c => c.Id == o.CheckoutId).Select(c => c.AddressSnapshot).FirstOrDefault(),
            Items = o.Items.OrderBy(i => i.Id).Select(i => i.NameSnapshot + (i.VariantSnapshot != null ? " (" + i.VariantSnapshot + ")" : "") + " x" + i.Quantity).ToList(),
            Tracking = db.Shipments.Where(s => s.OrderId == o.Id && s.Direction == ShipmentDirection.Outbound).OrderByDescending(s => s.CreatedAt)
                .Select(s => new { s.TrackingNo, s.CarrierCode }).FirstOrDefault(),
        }).ToListAsync(ct);
        return documents.ExportOrders(rows.Select(r =>
        {
            var a = r.Address is null ? default : JsonDocument.Parse(r.Address).RootElement;
            return new OrderExportRow(r.Code, r.CreatedAt, OrderStateMachine.Label(r.Status), r.Buyer ?? "",
                a.ValueKind == JsonValueKind.Object ? a.GetProperty("phone").GetString() ?? "" : "",
                a.ValueKind == JsonValueKind.Object ? a.GetProperty("fullAddress").GetString() ?? "" : "",
                string.Join("; ", r.Items), r.Subtotal, r.ShopDiscount, r.ShippingFee, r.GrandTotal,
                r.PaymentMethod == PaymentMethod.Cod ? "COD" : "Online", r.Tracking?.TrackingNo, r.Tracking?.CarrierCode);
        }).ToList());
    }
}

// ---------- cancel / cancel requests / notes ----------

public record SellerCancelOrderCommand(Guid ShopId, Guid OrderId, string Reason) : IRequest<Unit>;

public sealed class SellerCancelOrderValidator : AbstractValidator<SellerCancelOrderCommand>
{
    public SellerCancelOrderValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Vui lòng chọn lý do huỷ.").MaximumLength(300).WithMessage("Lý do tối đa 300 ký tự.");
}

public sealed class SellerCancelOrderHandler(IApplicationDbContext db, SellerAccess access, OrderLocks locks, OrderCanceller canceller, ICurrentUser currentUser)
    : IRequestHandler<SellerCancelOrderCommand, Unit>
{
    public async Task<Unit> Handle(SellerCancelOrderCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.OrderManage, ct);
        if (!await db.Orders.AnyAsync(o => o.Id == request.OrderId && o.ShopId == request.ShopId, ct)) throw new NotFoundException("Không tìm thấy đơn hàng.");
        await using var tx = await db.BeginTransactionAsync(ct);
        var order = await locks.LockAsync(request.OrderId, ct);
        await canceller.CancelAsync(order, OrderActor.Seller, currentUser.UserId, request.Reason.Trim(), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}

public record DecideCancelRequestCommand(Guid ShopId, Guid OrderId, bool Approve, string? RejectReason) : IRequest<Unit>;

public sealed class DecideCancelRequestHandler(
    IApplicationDbContext db,
    SellerAccess access,
    OrderLocks locks,
    OrderCanceller canceller,
    IOutbox outbox,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<DecideCancelRequestCommand, Unit>
{
    public async Task<Unit> Handle(DecideCancelRequestCommand request, CancellationToken ct)
    {
        var staff = await access.RequireAsync(request.ShopId, ShopPermissions.OrderManage, ct);
        if (!await db.Orders.AnyAsync(o => o.Id == request.OrderId && o.ShopId == request.ShopId, ct)) throw new NotFoundException("Không tìm thấy đơn hàng.");
        await using var tx = await db.BeginTransactionAsync(ct);
        var order = await locks.LockAsync(request.OrderId, ct);
        var open = await db.OrderCancelRequests.FirstOrDefaultAsync(r => r.OrderId == order.Id && r.Status == CancelRequestStatus.Pending, ct)
                   ?? throw new ConflictException("Đơn này không có yêu cầu huỷ đang chờ.", "NO_REQUEST");
        if (request.Approve)
        {
            open.Approve(staff.UserId, automatic: false, clock.UtcNow);
            await canceller.CancelAsync(order, OrderActor.Seller, currentUser.UserId, $"Shop chấp thuận yêu cầu huỷ: {open.Reason}", ct);
        }
        else
        {
            open.Reject(staff.UserId, request.RejectReason ?? string.Empty, clock.UtcNow);
            outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.CancelRejected, open.RejectReason));
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}

public record SetSellerNoteCommand(Guid ShopId, Guid OrderId, string? Note) : IRequest<Unit>;

public sealed class SetSellerNoteValidator : AbstractValidator<SetSellerNoteCommand>
{
    public SetSellerNoteValidator() => RuleFor(x => x.Note).MaximumLength(500).WithMessage("Ghi chú tối đa 500 ký tự.");
}

public sealed class SetSellerNoteHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<SetSellerNoteCommand, Unit>
{
    public async Task<Unit> Handle(SetSellerNoteCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.OrderManage, ct);
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == request.OrderId && o.ShopId == request.ShopId, ct)
                    ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
        order.SetSellerNote(request.Note);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ---------- dashboard (spec III.2) ----------

public record SalesFigureDto(long Revenue, int Orders);

public record SellerDashboardDto(
    int ToConfirm,
    int ToShip,
    int Shipping,
    int CancelRequests,
    int DeliveryProblems,
    int BannedProducts,
    int LowStockSkus,
    int PenaltyPoints,
    SalesFigureDto Today,
    SalesFigureDto Last7Days,
    SalesFigureDto Last30Days);

public record SellerDashboardQuery(Guid ShopId) : IRequest<SellerDashboardDto>;

public sealed class SellerDashboardHandler(IApplicationDbContext db, SellerAccess access, ISystemParameters parameters, IClock clock)
    : IRequestHandler<SellerDashboardQuery, SellerDashboardDto>
{
    public async Task<SellerDashboardDto> Handle(SellerDashboardQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.OrderView, ct);
        var orders = db.Orders.AsNoTracking().Where(o => o.ShopId == request.ShopId);
        var low = await parameters.GetIntAsync(ParameterKeys.ShopLowStockThreshold, ct);
        var todayStart = new DateTimeOffset(VietnamTime.Today(clock.UtcNow).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();

        async Task<SalesFigureDto> Sales(DateTimeOffset since)
        {
            // Revenue = goods value after the shop's own discount, of orders not cancelled / returned
            var q = orders.Where(o => o.CreatedAt >= since && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned
                                      && o.Status != OrderStatus.PendingPayment);
            return new SalesFigureDto(await q.SumAsync(o => (long?)(o.Subtotal - o.ShopDiscount), ct) ?? 0, await q.CountAsync(ct));
        }

        return new SellerDashboardDto(
            await orders.CountAsync(o => o.Status == OrderStatus.PendingConfirmation, ct),
            await orders.CountAsync(o => o.Status == OrderStatus.ReadyToShip, ct),
            await orders.CountAsync(o => o.Status == OrderStatus.Shipping, ct),
            await orders.CountAsync(o => db.OrderCancelRequests.Any(r => r.OrderId == o.Id && r.Status == CancelRequestStatus.Pending), ct),
            await orders.CountAsync(o => o.Status == OrderStatus.DeliveryFailed || o.Status == OrderStatus.Returning, ct),
            await db.Products.CountAsync(p => p.ShopId == request.ShopId && p.Status == ProductStatus.Banned, ct),
            await db.Skus.CountAsync(s => s.IsActive && s.Stock - s.Reserved <= low
                                          && db.Products.Any(p => p.Id == s.ProductId && p.ShopId == request.ShopId && p.Status == ProductStatus.Active), ct),
            await db.Shops.Where(s => s.Id == request.ShopId).Select(s => s.PenaltyPoints).SingleAsync(ct),
            await Sales(todayStart), await Sales(todayStart.AddDays(-6)), await Sales(todayStart.AddDays(-29)));
    }
}
