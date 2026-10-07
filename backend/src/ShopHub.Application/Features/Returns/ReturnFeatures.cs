using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Checkout;
using ShopHub.Application.Features.Media;
using ShopHub.Application.Features.Reviews;
using ShopHub.Application.Features.Seller;
using ShopHub.Application.Features.Storefront;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Media;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Returns;

// ---------- DTOs ----------

public record ReturnItemDto(Guid OrderItemId, string Name, string? Variant, string? ImageUrl, int Quantity, long RefundAmount, long RefundCoins);

public record ReturnEvidenceDto(ReturnParty Party, ReturnEvidenceType Type, string Url, string? Note);

public record ReturnHistoryDto(ReturnStatus To, string Label, ReturnParty By, string? Note, DateTimeOffset OccurredAt);

public record ReturnDto(
    Guid Id,
    string Code,
    Guid OrderId,
    string OrderCode,
    Guid ShopId,
    string ShopName,
    ReturnType Type,
    ReturnReason Reason,
    string Description,
    ReturnStatus Status,
    string StatusLabel,
    long RequestedAmount,
    long RequestedCoins,
    long? OfferedAmount,
    long? RefundAmount,
    long? RefundCoins,
    string? ShopNote,
    DateTimeOffset? RespondBy,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ReturnItemDto> Items,
    IReadOnlyList<ReturnEvidenceDto> Evidence,
    IReadOnlyList<ReturnHistoryDto> History,
    string? ReturnTrackingNo,
    string? DisputeReason,
    string? DisputeDecision,
    string? DisputeDecisionReason,
    string RefundDestination);

internal static class ReturnViews
{
    public static async Task<ReturnDto> BuildAsync(IApplicationDbContext db, IObjectStorage storage, ReturnRequest r, CancellationToken ct)
    {
        // Evidence in the private bucket: a link valid for a short time, only in this answer (buyer, shop or admin of the return)
        var assetIds = r.Evidence.Where(e => e.AssetId != null).Select(e => e.AssetId!.Value).ToList();
        var assets = await db.MediaAssets.AsNoTracking().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var links = new Dictionary<Guid, string>();
        foreach (var a in assets.Values.Where(a => Buckets.IsPrivate(a.Bucket)))
            links[a.Id] = (await Media.MediaUrls.ToDtoAsync(a, storage, ct)).Url ?? "";
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == r.OrderId, ct);
        var shopName = await db.Shops.Where(s => s.Id == r.ShopId).Select(s => s.Name).SingleAsync(ct);
        var tracking = await db.Shipments.AsNoTracking().Where(s => s.ReturnId == r.Id).OrderByDescending(s => s.CreatedAt).Select(s => s.TrackingNo).FirstOrDefaultAsync(ct);
        var dispute = await db.Disputes.AsNoTracking().FirstOrDefaultAsync(d => d.ReturnId == r.Id, ct);
        var items = r.Items.Select(i =>
        {
            var line = order.Items.Single(x => x.Id == i.OrderItemId);
            return new ReturnItemDto(i.OrderItemId, line.NameSnapshot, line.VariantSnapshot, line.ImageSnapshot, i.Quantity, i.RefundAmount, i.RefundCoins);
        }).ToList();
        return new ReturnDto(r.Id, r.Code, r.OrderId, order.Code, r.ShopId, shopName, r.Type, r.Reason, r.Description, r.Status, ReturnRequest.Label(r.Status),
            r.RequestedAmount, r.RequestedCoins, r.OfferedAmount, r.RefundAmount, r.RefundCoins, r.ShopNote, r.RespondBy, r.CreatedAt, items,
            r.Evidence.Select(e => new ReturnEvidenceDto(e.Party, e.Type, e.AssetId is { } id && links.TryGetValue(id, out var link) ? link : e.Url, e.Note)).ToList(),
            r.History.OrderBy(h => h.OccurredAt).ThenBy(h => h.Id).Select(h => new ReturnHistoryDto(h.ToStatus, ReturnRequest.Label(h.ToStatus), h.By, h.Note, h.OccurredAt)).ToList(),
            tracking, dispute?.Reason, dispute?.Decision?.ToString(), dispute?.DecisionReason,
            order.PaymentMethod == PaymentMethod.Cod ? "Ví ShopHub" : "Phương thức thanh toán ban đầu");
    }

    public static IQueryable<ReturnRequest> WithDetails(IApplicationDbContext db) =>
        db.ReturnRequests.Include(r => r.Items).Include(r => r.Evidence).Include(r => r.History);
}

/// <summary>Resolves uploaded evidence of the caller (purpose "evidence") to public URLs.</summary>
internal static class EvidenceResolver
{
    public static async Task<List<(ReturnEvidenceType, Guid, string)>> ResolveAsync(IApplicationDbContext db, IObjectStorage storage, Guid ownerId,
        IReadOnlyList<Guid> assetIds, CancellationToken ct)
    {
        var wanted = assetIds.Distinct().ToList();
        var assets = await db.MediaAssets.AsNoTracking().Where(a => wanted.Contains(a.Id) && a.Purpose == "evidence" && a.OwnerUserId == ownerId)
            .ToDictionaryAsync(a => a.Id, ct);
        if (assets.Count != wanted.Count) throw new NotFoundException("Không tìm thấy ảnh/video bằng chứng (hoặc tệp không thuộc về bạn).");
        // Private bucket: no lasting link is stored, ReturnViews signs one for whoever may see the return
        return wanted.Select(id => assets[id]).Select(a => (a.Kind == MediaKind.Video ? ReturnEvidenceType.Video : ReturnEvidenceType.Image, a.Id,
            Buckets.IsPrivate(a.Bucket) ? string.Empty
            : a.Kind == MediaKind.Video ? storage.PublicUrl(a.Bucket, a.ObjectKey) : storage.PublicUrl(a.Bucket, ImageSizes.Key(a.ObjectKey, ImageSizes.Large)))).ToList();
    }
}

/// <summary>
/// Gives the money and xu back for a return (spec 3.8): online payments are refunded through the gateway (partial
/// refund of the checkout's payment); COD / wallet orders are refunded to the ShopHub wallet (credited by the finance
/// module, Phase 8); xu used on the lines come back as xu. Returned goods go back into stock when the shop agrees,
/// and the review reward of a refunded line is taken back. Runs inside the caller's transaction and return lock.
/// </summary>
public sealed class ReturnRefunder(
    IWorkingCalendar calendar,
    InventoryWriter inventory,
    IApplicationDbContext db,
    IPaymentGatewayRegistry gateways,
    ReviewRewards rewards,
    ICounterRecomputer counters,
    IOutbox outbox,
    IClock clock)
{
    public async Task RefundAsync(ReturnRequest r, long money, long coins, ReturnParty by, string note, CancellationToken ct)
    {
        var now = clock.UtcNow;
        r.MarkRefunded(money, coins, now);
        r.Move(ReturnStatus.Refunded, by, note, now);
        var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == r.OrderId, ct);

        if (money > 0)
        {
            // Paid through a gateway → refund there; COD and Ví ShopHub → into the wallet (credited by the finance sync)
            var online = order.PaymentMethod.IsOnline();
            // A COD parcel that never arrived was never paid for: the refund only takes it off what the carrier owes
            var uncollected = order.PaymentMethod == PaymentMethod.Cod && r.Reason == ReturnReason.UndeliveredParcel;
            var payment = online
                ? await db.Payments.Where(p => p.CheckoutId == order.CheckoutId && p.Status == PaymentStatus.Succeeded).OrderByDescending(p => p.PaidAt).FirstOrDefaultAsync(ct)
                : null;
            var refund = new Refund(order.Id, payment?.Id, money,
                online ? RefundDestination.Gateway : uncollected ? RefundDestination.Uncollected : RefundDestination.Wallet,
                $"Hoàn tiền trả hàng {r.Code}", now);
            refund.LinkReturn(r.Id);
            db.Refunds.Add(refund);
            if (uncollected) refund.Complete(true, "COD-KHONG-THU", now);
            else if (payment is not null)
            {
                var ok = await gateways.For(payment.Method).RefundAsync(payment, money, $"Trả hàng {r.Code}", ct);
                refund.Complete(ok, payment.ProviderTxnId, now);
            }
            // Wallet destination: stays Pending until the finance sync credits Ví ShopHub (same transaction as the posting)
        }
        if (coins > 0)
            db.CoinLedger.Add(new CoinEntry(r.BuyerId, coins, CoinReason.CheckoutRefund, "return", r.Id, null, $"Hoàn xu trả hàng {r.Code}", now));

        if (r.Type == ReturnType.ReturnAndRefund && r.Restock)
            foreach (var item in r.Items.OrderBy(i => i.OrderItemId))
            {
                var line = order.Items.Single(i => i.Id == item.OrderItemId);
                await inventory.TryMoveAsync(new InventoryMove(line.SkuId, item.Quantity, 0, InventoryReason.ReturnRestock, "return", r.Id, null,
                    $"Hàng trả {r.Code}"), ct);
            }

        var lineIds = r.Items.Select(i => i.OrderItemId).ToList();
        foreach (var review in await db.Reviews.Where(x => lineIds.Contains(x.OrderItemId) && x.Rewarded).ToListAsync(ct))
            await rewards.RevokeAsync(review, ct);
        foreach (var item in r.Items) item.Close();
        outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.ReturnRefunded, r.Code));
        await db.SaveChangesAsync(ct);
        await counters.RecomputeProductSalesAsync(order.Items.Where(i => lineIds.Contains(i.Id)).Select(i => i.ProductId).Distinct().ToList(), ct);
    }

    /// <summary>The return ends without money (shop won, buyer gave up, buyer cancelled): its lines may be returned again later.</summary>
    public void Close(ReturnRequest r, ReturnStatus to, ReturnParty by, string note)
    {
        r.Move(to, by, note, clock.UtcNow);
        foreach (var item in r.Items) item.Close();
        outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(r.OrderId, OrderEvents.ReturnUpdated, r.Code));
    }

    /// <summary>Approved "return & refund": book the parcel back from the buyer to the shop's warehouse (drop-off).</summary>
    public async Task BookReturnParcelAsync(ReturnRequest r, IEnumerable<ICarrier> carriers, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == r.OrderId, ct);
        var checkout = await db.CheckoutSessions.AsNoTracking().SingleAsync(c => c.Id == order.CheckoutId, ct);
        var buyer = CarrierParties.FromSnapshot(checkout.AddressSnapshot);
        var fromProvince = buyer?.Point.ProvinceCode ?? "01";
        var warehouse = await db.ShopWarehouses.AsNoTracking().Where(w => w.ShopId == r.ShopId).OrderByDescending(w => w.IsReturnDefault).ThenBy(w => w.Id)
            .FirstAsync(ct);
        var toProvince = warehouse.ProvinceCode;
        var carrier = await db.Carriers.AsNoTracking().SingleAsync(c => c.Code == order.CarrierCode, ct);
        var provider = carriers.First(c => c.Provider == carrier.Provider);
        var units = r.Items.Sum(i => i.Quantity);
        var weight = Math.Max(100, units * 300);
        var now = clock.UtcNow;
        var items = r.Items.Select(i => (Line: order.Items.First(o => o.Id == i.OrderItemId), i.Quantity))
            .Select(x => new CarrierItem(x.Line.NameSnapshot, x.Quantity, 300)).ToList();
        var tracking = await provider.CreateShipmentAsync(carrier,
            new CarrierParcel(order.Id, $"{order.Code}-{r.Code}", fromProvince, toProvince, weight, 0, PickupMethod.DropOff, null,
                buyer, CarrierParties.FromWarehouse(warehouse), r.Items.Sum(i => order.Items.First(o => o.Id == i.OrderItemId).UnitPrice * i.Quantity),
                items, $"Hàng trả của đơn {order.Code}"), ct);
        var expected = await calendar.AddWorkingDaysAsync(VietnamTime.Today(now), carrier.DaysFor(ShippingCalculator.ZoneOf(fromProvince, toProvince)) + 1, ct);
        var shipment = new Shipment(order.Id, carrier.Code, tracking, ShipmentDirection.Return, 0, 0, weight, PickupMethod.DropOff, null,
            new DateTimeOffset(expected.ToDateTime(new TimeOnly(18, 0)), TimeSpan.FromHours(7)).ToUniversalTime(), now);
        shipment.LinkReturn(r.Id);
        db.Shipments.Add(shipment);
    }
}

// ---------- buyer: create / follow up ----------

public record ReturnableLineDto(Guid OrderItemId, string Name, string? Variant, string? ImageUrl, int Quantity, int Returnable, long UnitRefundEstimate);

public record ReturnableDto(bool CanReturn, string? Reason, DateTimeOffset? Deadline, IReadOnlyList<ReturnableLineDto> Lines);

internal static class Returnability
{
    public static async Task<Dictionary<Guid, int>> UsedQuantitiesAsync(IApplicationDbContext db, IReadOnlyCollection<Guid> lineIds, CancellationToken ct) =>
        await (from i in db.ReturnItems
               join r in db.ReturnRequests on i.ReturnId equals r.Id
               where lineIds.Contains(i.OrderItemId) && (i.IsOpen || r.Status == ReturnStatus.Refunded)
               group i by i.OrderItemId into g
               select new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

    public static async Task<Dictionary<Guid, int>> RefundedQuantitiesAsync(IApplicationDbContext db, IReadOnlyCollection<Guid> lineIds, CancellationToken ct) =>
        await (from i in db.ReturnItems
               join r in db.ReturnRequests on i.ReturnId equals r.Id
               where lineIds.Contains(i.OrderItemId) && r.Status == ReturnStatus.Refunded
               group i by i.OrderItemId into g
               select new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

    public static long PaidMoney(OrderItem i) => i.LineTotal - i.Discounts.Sum(d => d.Amount);

    public static long PaidCoins(OrderItem i) => i.Discounts.Where(d => d.Source == DiscountSource.Coin).Sum(d => d.Amount);

    public static string? WhyNot(Order order, DateTimeOffset? deadline, DateTimeOffset now) =>
        order.Status is not (OrderStatus.Delivered or OrderStatus.Completed) ? "Chỉ yêu cầu trả hàng được khi đơn đã giao."
        : deadline < now ? "Đã quá hạn yêu cầu trả hàng / hoàn tiền." : null;
}

public record ReturnableQuery(string Code) : IRequest<ReturnableDto>;

public sealed class ReturnableHandler(IApplicationDbContext db, ISystemParameters parameters, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<ReturnableQuery, ReturnableDto>
{
    public async Task<ReturnableDto> Handle(ReturnableQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var code = request.Code.Trim().ToUpperInvariant();
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).ThenInclude(i => i.Discounts)
            .FirstOrDefaultAsync(o => o.Code == code && o.BuyerId == userId, ct) ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
        var deadline = order.DeliveredAt?.AddDays(await parameters.GetIntAsync(ParameterKeys.ReturnWindowDays, ct));
        var used = await Returnability.UsedQuantitiesAsync(db, order.Items.Select(i => i.Id).ToList(), ct);
        var why = Returnability.WhyNot(order, deadline, clock.UtcNow);
        var lines = order.Items.OrderBy(i => i.Id).Select(i => new ReturnableLineDto(i.Id, i.NameSnapshot, i.VariantSnapshot, i.ImageSnapshot, i.Quantity,
            Math.Max(0, i.Quantity - used.GetValueOrDefault(i.Id)), Returnability.PaidMoney(i) / i.Quantity)).ToList();
        if (why is null && lines.All(l => l.Returnable == 0)) why = "Mọi sản phẩm của đơn đã được trả hoặc đang có yêu cầu trả hàng.";
        return new ReturnableDto(why is null, why, deadline, lines);
    }
}

public record ReturnLineInput(Guid OrderItemId, int Quantity);

public record CreateReturnCommand(string Code, ReturnType Type, ReturnReason Reason, string Description, IReadOnlyList<ReturnLineInput> Lines,
    IReadOnlyList<Guid> EvidenceAssetIds) : IRequest<ReturnDto>;

public sealed class CreateReturnValidator : AbstractValidator<CreateReturnCommand>
{
    public CreateReturnValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Chọn ít nhất một sản phẩm cần trả.");
        RuleForEach(x => x.Lines).ChildRules(l => l.RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Số lượng trả phải từ 1 trở lên."));
        RuleFor(x => x.Description).NotEmpty().WithMessage("Vui lòng mô tả vấn đề.").MinimumLength(10).WithMessage("Vui lòng mô tả vấn đề (ít nhất 10 ký tự).")
            .MaximumLength(1000).WithMessage("Mô tả tối đa 1.000 ký tự.");
        RuleFor(x => x.EvidenceAssetIds).NotEmpty().WithMessage("Vui lòng đính kèm ít nhất một ảnh hoặc video bằng chứng.")
            .Must(e => e.Count <= 10).WithMessage("Tối đa 10 bằng chứng.");
    }
}

public sealed class CreateReturnHandler(
    IApplicationDbContext db,
    IObjectStorage storage,
    ISystemParameters parameters,
    IOutbox outbox,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<CreateReturnCommand, ReturnDto>
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<ReturnDto> Handle(CreateReturnCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var code = request.Code.Trim().ToUpperInvariant();
        var orderId = await db.Orders.Where(o => o.Code == code && o.BuyerId == userId).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct)
                      ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
        var now = clock.UtcNow;
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"order:{orderId}", ct);
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).ThenInclude(i => i.Discounts).SingleAsync(o => o.Id == orderId, ct);
        var deadline = order.DeliveredAt?.AddDays(await parameters.GetIntAsync(ParameterKeys.ReturnWindowDays, ct));
        if (Returnability.WhyNot(order, deadline, now) is { } why) throw new ConflictException(why, "NOT_RETURNABLE");

        var used = await Returnability.UsedQuantitiesAsync(db, order.Items.Select(i => i.Id).ToList(), ct);
        var refunded = await Returnability.RefundedQuantitiesAsync(db, order.Items.Select(i => i.Id).ToList(), ct);
        var respondDays = await parameters.GetIntAsync(ParameterKeys.ReturnShopResponseDays, ct);
        var r = new ReturnRequest(order.Id, userId, order.ShopId, NewCode(now), request.Type, request.Reason, request.Description, now.AddDays(respondDays), now);
        long money = 0, coins = 0;
        foreach (var lineInput in request.Lines.GroupBy(l => l.OrderItemId).Select(g => new ReturnLineInput(g.Key, g.Sum(x => x.Quantity))))
        {
            var line = order.Items.FirstOrDefault(i => i.Id == lineInput.OrderItemId) ?? throw new NotFoundException("Không tìm thấy sản phẩm trong đơn.");
            if (used.GetValueOrDefault(line.Id) > refunded.GetValueOrDefault(line.Id))
                throw new ConflictException($"\"{line.NameSnapshot}\" đang có yêu cầu trả hàng chưa xử lý xong.", "RETURN_OPEN");
            var (m, c) = ReturnPricing.ForUnits(Returnability.PaidMoney(line), Returnability.PaidCoins(line), line.Quantity, refunded.GetValueOrDefault(line.Id),
                lineInput.Quantity);
            r.Items.Add(new ReturnItem(r.Id, line.Id, lineInput.Quantity, m, c));
            money += m;
            coins += c;
        }
        r.SetAmounts(money, coins);
        foreach (var (type, assetId, url) in await EvidenceResolver.ResolveAsync(db, storage, userId, request.EvidenceAssetIds, ct))
            r.AddEvidence(ReturnParty.Buyer, type, assetId, url, null);
        db.ReturnRequests.Add(r);
        outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.ReturnRequested, r.Code));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ReturnViews.BuildAsync(db, storage, await ReturnViews.WithDetails(db).AsNoTracking().SingleAsync(x => x.Id == r.Id, ct), ct);
    }

    internal static string NewCode(DateTimeOffset now)
    {
        var random = string.Create(6, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = CodeAlphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        });
        return $"RT{VietnamTime.ToLocal(now):yyMMdd}{random}";
    }
}

public record MyReturnsQuery(int Page = 1, int PageSize = 20) : IRequest<PagedResult<ReturnDto>>, IPagedRequest;

public sealed class MyReturnsValidator : AbstractValidator<MyReturnsQuery>
{
    public MyReturnsValidator() => this.ApplyPagingRules();
}

public sealed class MyReturnsHandler(IApplicationDbContext db, IObjectStorage storage, ICurrentUser currentUser) : IRequestHandler<MyReturnsQuery, PagedResult<ReturnDto>>
{
    public async Task<PagedResult<ReturnDto>> Handle(MyReturnsQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var page = await ReturnViews.WithDetails(db).AsNoTracking().Where(r => r.BuyerId == userId)
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).ToPagedResultAsync(request, ct);
        var items = new List<ReturnDto>();
        foreach (var r in page.Items) items.Add(await ReturnViews.BuildAsync(db, storage, r, ct));
        return new PagedResult<ReturnDto>(items, page.TotalCount, page.Page, page.PageSize);
    }
}

public record GetMyReturnQuery(string Code) : IRequest<ReturnDto>;

public sealed class GetMyReturnHandler(IApplicationDbContext db, IObjectStorage storage, ICurrentUser currentUser) : IRequestHandler<GetMyReturnQuery, ReturnDto>
{
    public async Task<ReturnDto> Handle(GetMyReturnQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var code = request.Code.Trim().ToUpperInvariant();
        var r = await ReturnViews.WithDetails(db).AsNoTracking().FirstOrDefaultAsync(x => x.Code == code && x.BuyerId == userId, ct)
                ?? throw new NotFoundException("Không tìm thấy yêu cầu trả hàng.");
        return await ReturnViews.BuildAsync(db, storage, r, ct);
    }
}

public enum BuyerReturnAction
{
    Cancel,       // huỷ yêu cầu
    AcceptOffer,  // đồng ý mức hoàn một phần
    Dispute,      // khiếu nại lên sàn
}

public record BuyerReturnActionCommand(string Code, BuyerReturnAction Action, string? Reason) : IRequest<ReturnDto>;

public sealed class BuyerReturnActionHandler(IApplicationDbContext db, IObjectStorage storage, ReturnRefunder refunder, IOutbox outbox, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<BuyerReturnActionCommand, ReturnDto>
{
    public async Task<ReturnDto> Handle(BuyerReturnActionCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var code = request.Code.Trim().ToUpperInvariant();
        var id = await db.ReturnRequests.Where(x => x.Code == code && x.BuyerId == userId).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct)
                 ?? throw new NotFoundException("Không tìm thấy yêu cầu trả hàng.");
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"return:{id}", ct);
        var r = await ReturnViews.WithDetails(db).SingleAsync(x => x.Id == id, ct);
        switch (request.Action)
        {
            case BuyerReturnAction.Cancel:
                if (r.Status is not (ReturnStatus.Requested or ReturnStatus.PartialOffered or ReturnStatus.Rejected or ReturnStatus.AwaitingReturn))
                    throw new ConflictException("Không thể huỷ yêu cầu ở trạng thái này.", "CANNOT_CANCEL");
                refunder.Close(r, ReturnStatus.Cancelled, ReturnParty.Buyer, "Người mua huỷ yêu cầu");
                break;
            case BuyerReturnAction.AcceptOffer:
                if (r.Status != ReturnStatus.PartialOffered || r.OfferedAmount is null) throw new ConflictException("Không có đề nghị hoàn một phần nào để đồng ý.", "NO_OFFER");
                // Xu follow the same share as the money
                var coins = r.RequestedAmount == 0 ? 0 : (long)((Int128)r.RequestedCoins * r.OfferedAmount.Value / r.RequestedAmount);
                await refunder.RefundAsync(r, r.OfferedAmount.Value, coins, ReturnParty.Buyer, "Người mua đồng ý mức hoàn một phần", ct);
                break;
            case BuyerReturnAction.Dispute:
                if (r.Status is not (ReturnStatus.Rejected or ReturnStatus.PartialOffered))
                    throw new ConflictException("Chỉ khiếu nại được khi shop từ chối hoặc đề nghị hoàn một phần.", "CANNOT_DISPUTE");
                if (string.IsNullOrWhiteSpace(request.Reason)) throw new ConflictException("Vui lòng nêu lý do khiếu nại.", "NO_REASON");
                db.Disputes.Add(new Dispute(r.Id, userId, request.Reason, clock.UtcNow));
                r.Move(ReturnStatus.Disputed, ReturnParty.Buyer, request.Reason.Trim(), clock.UtcNow);
                outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(r.OrderId, OrderEvents.DisputeOpened, r.Code));
                break;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ReturnViews.BuildAsync(db, storage, await ReturnViews.WithDetails(db).AsNoTracking().SingleAsync(x => x.Id == id, ct), ct);
    }
}

// ---------- seller ----------

public record ShopReturnsQuery(Guid ShopId, ReturnStatus? Status = null, int Page = 1, int PageSize = 20) : IRequest<PagedResult<ReturnDto>>, IPagedRequest;

public sealed class ShopReturnsValidator : AbstractValidator<ShopReturnsQuery>
{
    public ShopReturnsValidator() => this.ApplyPagingRules();
}

public sealed class ShopReturnsHandler(IApplicationDbContext db, IObjectStorage storage, SellerAccess access) : IRequestHandler<ShopReturnsQuery, PagedResult<ReturnDto>>
{
    public async Task<PagedResult<ReturnDto>> Handle(ShopReturnsQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.OrderView, ct);
        var q = ReturnViews.WithDetails(db).AsNoTracking().Where(r => r.ShopId == request.ShopId);
        if (request.Status is { } status) q = q.Where(r => r.Status == status);
        var page = await q.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).ToPagedResultAsync(request, ct);
        var items = new List<ReturnDto>();
        foreach (var r in page.Items) items.Add(await ReturnViews.BuildAsync(db, storage, r, ct));
        return new PagedResult<ReturnDto>(items, page.TotalCount, page.Page, page.PageSize);
    }
}

public enum ShopReturnAction
{
    Approve,
    Reject,
    OfferPartial,
    ConfirmReceived,
}

public record ShopReturnActionCommand(Guid ShopId, Guid ReturnId, ShopReturnAction Action, string? Note, long? Amount, bool Restock,
    IReadOnlyList<Guid>? EvidenceAssetIds) : IRequest<ReturnDto>;

public sealed class ShopReturnActionHandler(
    IApplicationDbContext db,
    SellerAccess access,
    ReturnRefunder refunder,
    IEnumerable<ICarrier> carriers,
    IObjectStorage storage,
    ISystemParameters parameters,
    IOutbox outbox,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<ShopReturnActionCommand, ReturnDto>
{
    public async Task<ReturnDto> Handle(ShopReturnActionCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.OrderManage, ct);
        if (!await db.ReturnRequests.AnyAsync(r => r.Id == request.ReturnId && r.ShopId == request.ShopId, ct))
            throw new NotFoundException("Không tìm thấy yêu cầu trả hàng.");
        var now = clock.UtcNow;
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"return:{request.ReturnId}", ct);
        var r = await ReturnViews.WithDetails(db).SingleAsync(x => x.Id == request.ReturnId, ct);
        if (request.EvidenceAssetIds is { Count: > 0 } evidence)
            foreach (var (type, assetId, url) in await EvidenceResolver.ResolveAsync(db, storage, UserGuard.Require(currentUser), evidence, ct))
                r.AddEvidence(ReturnParty.Shop, type, assetId, url, request.Note);
        var disputeDays = await parameters.GetIntAsync(ParameterKeys.ReturnDisputeDays, ct);
        switch (request.Action)
        {
            case ShopReturnAction.Approve when r.Status == ReturnStatus.Requested:
                await ApproveAsync(r, request.Restock, ReturnParty.Shop, request.Note ?? "Shop đồng ý", ct);
                break;
            case ShopReturnAction.Reject when r.Status == ReturnStatus.Requested:
                if (string.IsNullOrWhiteSpace(request.Note)) throw new ConflictException("Từ chối cần nêu lý do.", "NO_REASON");
                r.Move(ReturnStatus.Rejected, ReturnParty.Shop, request.Note.Trim(), now, now.AddDays(disputeDays));
                outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(r.OrderId, OrderEvents.ReturnUpdated, r.Code));
                break;
            case ShopReturnAction.OfferPartial when r.Status == ReturnStatus.Requested:
                r.OfferPartial(request.Amount ?? 0);
                r.Move(ReturnStatus.PartialOffered, ReturnParty.Shop, request.Note ?? $"Đề nghị hoàn {Domain.Common.Money.Vnd(r.OfferedAmount!.Value)}", now,
                    now.AddDays(disputeDays));
                outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(r.OrderId, OrderEvents.ReturnUpdated, r.Code));
                break;
            case ShopReturnAction.ConfirmReceived when r.Status == ReturnStatus.AwaitingShopCheck:
                r.SetRestock(request.Restock);
                await refunder.RefundAsync(r, r.RequestedAmount, r.RequestedCoins, ReturnParty.Shop, request.Note ?? "Shop đã nhận hàng trả", ct);
                break;
            default:
                throw new ConflictException($"Không thể thực hiện thao tác này khi yêu cầu đang \"{ReturnRequest.Label(r.Status)}\".", "BAD_STATE");
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ReturnViews.BuildAsync(db, storage, await ReturnViews.WithDetails(db).AsNoTracking().SingleAsync(x => x.Id == r.Id, ct), ct);
    }

    private async Task ApproveAsync(ReturnRequest r, bool restock, ReturnParty by, string note, CancellationToken ct)
    {
        r.SetRestock(restock);
        if (r.Type == ReturnType.RefundOnly)
        {
            await refunder.RefundAsync(r, r.RequestedAmount, r.RequestedCoins, by, note, ct);
            return;
        }
        r.Move(ReturnStatus.AwaitingReturn, by, note, clock.UtcNow);
        await refunder.BookReturnParcelAsync(r, carriers, ct);
        outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(r.OrderId, OrderEvents.ReturnUpdated, r.Code));
    }
}

// ---------- admin: disputes ----------

public record DisputesQuery(bool Open = true, int Page = 1, int PageSize = 20) : IRequest<PagedResult<ReturnDto>>, IPagedRequest;

public sealed class DisputesValidator : AbstractValidator<DisputesQuery>
{
    public DisputesValidator() => this.ApplyPagingRules();
}

public sealed class DisputesHandler(IApplicationDbContext db, IObjectStorage storage) : IRequestHandler<DisputesQuery, PagedResult<ReturnDto>>
{
    public async Task<PagedResult<ReturnDto>> Handle(DisputesQuery request, CancellationToken ct)
    {
        var q = ReturnViews.WithDetails(db).AsNoTracking()
            .Where(r => db.Disputes.Any(d => d.ReturnId == r.Id && (request.Open ? d.ClosedAt == null : d.ClosedAt != null)));
        var page = await q.OrderBy(r => r.UpdatedAt).ThenBy(r => r.Id).ToPagedResultAsync(request, ct);
        var items = new List<ReturnDto>();
        foreach (var r in page.Items) items.Add(await ReturnViews.BuildAsync(db, storage, r, ct));
        return new PagedResult<ReturnDto>(items, page.TotalCount, page.Page, page.PageSize);
    }
}

/// <param name="RefundAmount">For the buyer: how much (defaults to everything paid for the lines).</param>
/// <param name="RequireReturn">For a "return &amp; refund": the buyer must send the goods back before the refund.</param>
public record DecideDisputeCommand(Guid ReturnId, DisputeDecision Decision, string Reason, long? RefundAmount, bool RequireReturn) : IRequest<ReturnDto>;

public sealed class DecideDisputeValidator : AbstractValidator<DecideDisputeCommand>
{
    public DecideDisputeValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Phân xử cần ghi rõ lý do.").MaximumLength(1000).WithMessage("Lý do tối đa 1.000 ký tự.");
        RuleFor(x => x.RefundAmount).GreaterThanOrEqualTo(0).When(x => x.RefundAmount is not null).WithMessage("Số tiền hoàn không được âm.");
    }
}

public sealed class DecideDisputeHandler(
    IApplicationDbContext db,
    IObjectStorage storage,
    ReturnRefunder refunder,
    IEnumerable<ICarrier> carriers,
    IOutbox outbox,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<DecideDisputeCommand, ReturnDto>
{
    public async Task<ReturnDto> Handle(DecideDisputeCommand request, CancellationToken ct)
    {
        var adminId = UserGuard.Require(currentUser);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"return:{request.ReturnId}", ct);
        var r = await ReturnViews.WithDetails(db).FirstOrDefaultAsync(x => x.Id == request.ReturnId, ct) ?? throw new NotFoundException("Không tìm thấy khiếu nại.");
        var dispute = await db.Disputes.FirstOrDefaultAsync(d => d.ReturnId == r.Id, ct) ?? throw new NotFoundException("Không tìm thấy khiếu nại.");
        if (r.Status != ReturnStatus.Disputed) throw new ConflictException("Khiếu nại này đã được phân xử.", "DECIDED");
        var amount = Math.Min(request.RefundAmount ?? r.RequestedAmount, r.RequestedAmount);
        dispute.Decide(adminId, request.Decision, request.Reason, request.Decision == DisputeDecision.FavorBuyer ? amount : null, clock.UtcNow);
        var note = $"Sàn phân xử: {request.Reason.Trim()}";
        if (request.Decision == DisputeDecision.FavorShop)
            refunder.Close(r, ReturnStatus.Closed, ReturnParty.Admin, note);
        else if (r.Type == ReturnType.ReturnAndRefund && request.RequireReturn)
        {
            r.Move(ReturnStatus.AwaitingReturn, ReturnParty.Admin, note, clock.UtcNow);
            await refunder.BookReturnParcelAsync(r, carriers, ct);
        }
        else
        {
            var coins = r.RequestedAmount == 0 ? 0 : (long)((Int128)r.RequestedCoins * amount / r.RequestedAmount);
            await refunder.RefundAsync(r, amount, coins, ReturnParty.Admin, note, ct);
        }
        outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(r.OrderId, OrderEvents.DisputeDecided, request.Decision.ToString()));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ReturnViews.BuildAsync(db, storage, await ReturnViews.WithDetails(db).AsNoTracking().SingleAsync(x => x.Id == r.Id, ct), ct);
    }
}

// ---------- automation & return parcels ----------

/// <summary>
/// Return deadlines (run with the order automation job): unanswered requests are approved, refused / partially
/// offered ones the buyer left alone are closed, and returned goods the shop did not check are refunded.
/// </summary>
public sealed class ReturnAutomationService(
    IApplicationDbContext db,
    ReturnRefunder refunder,
    IEnumerable<ICarrier> carriers,
    IClock clock,
    ILogger<ReturnAutomationService> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await db.ReturnRequests.AsNoTracking()
            .Where(r => r.RespondBy <= now && (r.Status == ReturnStatus.Requested || r.Status == ReturnStatus.Rejected
                                               || r.Status == ReturnStatus.PartialOffered || r.Status == ReturnStatus.AwaitingShopCheck))
            .OrderBy(r => r.RespondBy).ThenBy(r => r.Id).Select(r => r.Id).Take(500).ToListAsync(ct);
        var done = 0;
        foreach (var id in due)
        {
            try
            {
                await using var tx = await db.BeginTransactionAsync(ct);
                await db.LockAsync($"return:{id}", ct);
                var r = await ReturnViews.WithDetails(db).SingleAsync(x => x.Id == id, ct);
                if (r.RespondBy > now) continue;
                switch (r.Status)
                {
                    case ReturnStatus.Requested when r.Type == ReturnType.RefundOnly:
                        await refunder.RefundAsync(r, r.RequestedAmount, r.RequestedCoins, ReturnParty.Admin, "Shop không phản hồi đúng hạn — tự chấp thuận", ct);
                        break;
                    case ReturnStatus.Requested:
                        r.Move(ReturnStatus.AwaitingReturn, ReturnParty.Admin, "Shop không phản hồi đúng hạn — tự chấp thuận", now);
                        await refunder.BookReturnParcelAsync(r, carriers, ct);
                        break;
                    case ReturnStatus.Rejected or ReturnStatus.PartialOffered:
                        refunder.Close(r, ReturnStatus.Closed, ReturnParty.Admin, "Người mua không khiếu nại trong hạn");
                        break;
                    case ReturnStatus.AwaitingShopCheck:
                        await refunder.RefundAsync(r, r.RequestedAmount, r.RequestedCoins, ReturnParty.Admin, "Shop không kiểm hàng đúng hạn — tự hoàn tiền", ct);
                        break;
                }
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                db.ClearTracking();
                done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ClearTracking();
                logger.LogError(ex, "Return automation failed for {ReturnId}", id);
            }
        }
        return done;
    }

    /// <summary>Carrier events on a return parcel (called from the shipment processor, inside its transaction).</summary>
    public static async Task OnReturnParcelAsync(IApplicationDbContext db, Guid returnId, ShipmentStatus status, ISystemParameters parameters, DateTimeOffset now,
        CancellationToken ct)
    {
        await db.LockAsync($"return:{returnId}", ct);
        var r = await ReturnViews.WithDetails(db).SingleAsync(x => x.Id == returnId, ct);
        if (status == ShipmentStatus.Picked && r.Status == ReturnStatus.AwaitingReturn)
            r.Move(ReturnStatus.Returning, ReturnParty.Buyer, "Người mua đã gửi hàng trả", now);
        else if (status == ShipmentStatus.Delivered && r.Status == ReturnStatus.Returning)
            r.Move(ReturnStatus.AwaitingShopCheck, ReturnParty.Admin, "Hàng trả đã về tới shop", now,
                now.AddDays(await parameters.GetIntAsync(ParameterKeys.ReturnShopCheckDays, ct)));
    }
}
