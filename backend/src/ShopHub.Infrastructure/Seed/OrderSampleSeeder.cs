using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Account;
using ShopHub.Application.Features.Cart;
using ShopHub.Application.Features.Checkout;
using ShopHub.Application.Features.Media;
using ShopHub.Application.Features.Orders;
using ShopHub.Application.Features.Returns;
using ShopHub.Application.Features.Reviews;
using ShopHub.Application.Features.Seller;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;
using ShopHub.Infrastructure.Commerce;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Persistence;
using ShopHub.Infrastructure.Services;
using SkiaSharp;

namespace ShopHub.Infrastructure.Seed;

/// <summary>
/// Spec 7: ~500 sample orders over the last 90 days in every state and ~800 reviews on completed orders — placed and
/// moved through the REAL commands (cart → PricingEngine → checkout → shop preparation → simulated carrier → received →
/// review / return), with the clock moved back to each order's moment, so sold counts, ratings and the ledger agree.
/// Guard: skipped when the sample buyers already have orders. Deterministic (fixed seed).
/// </summary>
public sealed class OrderSampleSeeder(IServiceProvider services, ShopHubSettings settings, ILogger<OrderSampleSeeder> logger)
{
    private const int Orders = 500;
    private const int ReviewTarget = 800;

    private static readonly string[] ReviewTexts =
    [
        "Hàng đúng mô tả, đóng gói cẩn thận, giao nhanh hơn dự kiến.",
        "Chất lượng tốt so với giá tiền, sẽ ủng hộ shop tiếp.",
        "Sản phẩm dùng ổn, màu giống hình. Shop tư vấn nhiệt tình.",
        "Giao hàng nhanh, hàng nguyên vẹn. Cho 5 sao.",
        "Tạm được, đóng gói hơi sơ sài nhưng sản phẩm không lỗi.",
        "Mua lần thứ hai rồi, vẫn hài lòng như lần đầu.",
        "Đáng đồng tiền, hàng chính hãng có tem đầy đủ.",
        "Chất liệu tốt, dùng thoải mái, đúng như quảng cáo.",
        "",
    ];

    private sealed record SampleSku(Guid SkuId, Guid ShopId, Guid OwnerId, long Price, bool Mall);

    private sealed record Outcome(int Placed, int Reviews, int Returns);

    private sealed class Counters
    {
        public int Placed;
        public int Reviews;
        public int Returns;
        public int Skipped;
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        await using var root = services.CreateAsyncScope();
        var db = root.ServiceProvider.GetRequiredService<ShopHubDbContext>();
        var buyers = await db.Users.AsNoTracking().Where(u => u.Phone != null && u.Phone.StartsWith("09000000") && u.Status == UserStatus.Active)
            .OrderBy(u => u.Phone).Select(u => new { u.Id, u.Phone, u.FullName }).ToListAsync(ct);
        if (buyers.Count == 0) return;
        var buyerIds = buyers.Select(b => b.Id).ToList();
        if (await db.Orders.IgnoreQueryFilters().AnyAsync(o => buyerIds.Contains(o.BuyerId), ct)) return;

        // Products of the sample shops (owned by 09000001xx) with stock to spare
        var owners = await db.Users.AsNoTracking().Where(u => u.Phone != null && u.Phone.StartsWith("09000001")).Select(u => u.Id).ToListAsync(ct);
        var skus = (await (from s in db.Skus.AsNoTracking()
                           join p in db.Products.AsNoTracking() on s.ProductId equals p.Id
                           join sh in db.Shops.AsNoTracking() on p.ShopId equals sh.Id
                           where p.Status == ProductStatus.Active && s.IsActive && s.Stock - s.Reserved >= 30 && sh.Status == ShopStatus.Active
                                 && owners.Contains(sh.OwnerId) && p.MaxPerBuyer == null
                           orderby s.Id
                           select new { s.Id, p.ShopId, sh.OwnerId, s.Price, Mall = sh.Type == ShopType.Mall }).ToListAsync(ct))
            .Select(x => new SampleSku(x.Id, x.ShopId, x.OwnerId, x.Price, x.Mall)).ToList();
        if (skus.Count < 20)
        {
            logger.LogWarning("Not enough sample products to seed orders ({Count})", skus.Count);
            return;
        }
        var rng = new Random(20261006);
        // Each shop's SKUs in a fixed random order: picks lean to the front, so a few products sell a lot (like real
        // shops) and the "đã bán" counts spread out instead of being flat
        var byShop = skus.GroupBy(s => s.ShopId).ToDictionary(g => g.Key, g => g.OrderBy(_ => rng.Next()).ToList());
        // Mall shops get more orders
        var shopIds = byShop.Keys.OrderBy(k => k).SelectMany(k => Enumerable.Repeat(k, byShop[k][0].Mall ? 3 : 1)).ToList();

        var now = DateTimeOffset.UtcNow;
        var start = now.AddDays(-90);
        var counters = new Counters();
        var started = DateTime.UtcNow;
        logger.LogInformation("SEED sample orders: {Orders} orders over 90 days through the order commands…", Orders);

        // Every sample buyer needs a delivery address: a ward of Hà Nội or TP. Hồ Chí Minh (two-level divisions)
        foreach (var b in buyers)
        {
            await using var scope = services.CreateAsyncScope();
            var sdb = scope.ServiceProvider.GetRequiredService<ShopHubDbContext>();
            if (await sdb.Addresses.AnyAsync(a => a.UserId == b.Id, ct)) continue;
            var province = rng.Next(2) == 0 ? "01" : "79";
            var wards = await sdb.AdminDivisions.AsNoTracking().Where(d => d.ParentCode == province && d.Level == AdminDivisionLevel.Ward && d.IsActive)
                .OrderBy(d => d.Code).Select(d => d.Code).Take(30).ToListAsync(ct);
            var ward = wards[rng.Next(wards.Count)];
            using (SystemClock.TravelTo(start.AddDays(-1)))
                await SendAsync(scope, b.Id, new CreateAddressCommand(new AddressInput(b.FullName, b.Phone!, province, ward,
                    $"{rng.Next(1, 200)} Phố Mẫu", null, null, AddressType.Home, true)), ct);
        }

        for (var i = 0; i < Orders; i++)
        {
            var at = start.AddMinutes(i * ((90.0 * 24 - 8) * 60 / Orders) + rng.Next(0, 60));
            var buyer = buyers[i % buyers.Count].Id;
            try
            {
                await OneOrderAsync(buyer, at, now, rng, byShop, shopIds, counters, ct);
            }
            catch (Exception ex) when (ex is ConflictException or Domain.Common.BusinessRuleException or NotFoundException or FluentValidation.ValidationException)
            {
                // A sample order the rules refuse at that moment (stock, a voucher used up…) stops where it is
                if (++counters.Skipped <= 3) logger.LogWarning("Sample order {Index} stopped by a business rule: {Message}", i, ex.Message);
            }
            if ((i + 1) % 100 == 0)
                logger.LogInformation("SEED sample orders: {Done}/{Total} ({Seconds:0}s)", i + 1, Orders, (DateTime.UtcNow - started).TotalSeconds);
        }

        // Back to now: what the scheduler would have done since (expiry, auto-complete, returns, payouts)
        await using (var scope = services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            await sp.GetRequiredService<Application.Features.Payments.PaymentExpiryService>().RunAsync(ct);
            await sp.GetRequiredService<OrderAutomationService>().RunAsync(ct);
            await sp.GetRequiredService<ReturnAutomationService>().RunAsync(ct);
            await sp.GetRequiredService<Application.Features.Finance.SettlementService>().RunAsync(ct);
        }
        logger.LogInformation("SEED sample orders: {Placed} orders, {Reviews} reviews, {Returns} returns, {Skipped} stopped early, in {Seconds:0}s",
            counters.Placed, counters.Reviews, counters.Returns, counters.Skipped, (DateTime.UtcNow - started).TotalSeconds);
    }

    /// <summary>One checkout of one buyer at <paramref name="at"/>, then as far along its life as its age and its luck allow.</summary>
    private async Task OneOrderAsync(Guid buyerId, DateTimeOffset at, DateTimeOffset now, Random rng, Dictionary<Guid, List<SampleSku>> byShop,
        List<Guid> shopIds, Counters counters, CancellationToken ct)
    {
        // ----- cart: 1–2 shops, 1–3 products each (all variants are real SKUs) -----
        var shops = Enumerable.Range(0, rng.Next(100) < 25 ? 2 : 1).Select(_ => shopIds[rng.Next(shopIds.Count)]).Distinct().ToList();
        CheckoutResultDto result;
        var online = settings.PaymentSimulated && rng.Next(100) < 25;
        await using (var scope = services.CreateAsyncScope())
        using (SystemClock.TravelTo(at))
        {
            var cart = await SendAsync(scope, buyerId, new GetCartQuery(new CartOwner(buyerId, null)), ct);
            var leftovers = cart.Shops.SelectMany(s => s.Lines).Select(l => l.SkuId).ToList();
            if (leftovers.Count > 0) await SendAsync(scope, buyerId, new RemoveCartItemsCommand(new CartOwner(buyerId, null), leftovers), ct);
            foreach (var shopId in shops)
            {
                var pool = byShop[shopId];
                foreach (var sku in Enumerable.Range(0, rng.Next(1, 4)).Select(_ => pool[(int)(pool.Count * Math.Pow(rng.NextDouble(), 2.4))]).DistinctBy(s => s.SkuId))
                {
                    // Cheap everyday items are often bought by twos and threes
                    var quantity = sku.Price < 200_000 ? rng.Next(100) switch { < 55 => 1, < 80 => 2, < 93 => 3, _ => 5 } : rng.Next(100) < 88 ? 1 : 2;
                    await SendAsync(scope, buyerId, new AddCartItemCommand(new CartOwner(buyerId, null), sku.SkuId, quantity), ct);
                }
            }
            var addressId = await scope.ServiceProvider.GetRequiredService<ShopHubDbContext>().Addresses.AsNoTracking()
                .Where(a => a.UserId == buyerId && a.IsDefault).Select(a => a.Id).FirstAsync(ct);
            var request = new CheckoutRequest(addressId, shops.Select(s => new CheckoutShopChoice(s, null, null, null)).ToList(),
                rng.Next(100) < 10 ? "SHOPHUB50" : null, rng.Next(100) < 10 ? "FREESHIP" : null, false, online ? PaymentMethod.Simulated : PaymentMethod.Cod);
            var quote = await SendAsync(scope, buyerId, new QuoteCheckoutQuery(request), ct);
            if (!quote.CanPlace)
            {
                // A sample voucher that does not fit this basket: buy without it
                request = request with { PlatformVoucherCode = null, FreeshipVoucherCode = null };
                quote = await SendAsync(scope, buyerId, new QuoteCheckoutQuery(request), ct);
                if (!quote.CanPlace) return;
            }
            result = await SendAsync(scope, buyerId, new PlaceOrderCommand(Guid.NewGuid().ToString("N"), request, quote.GrandTotal), ct);
        }
        counters.Placed += result.Orders.Count;
        var age = now - at;

        // ----- payment: online orders are paid within minutes, a few are abandoned (expired at the end) -----
        if (online && result.Payment is { } payment)
        {
            if (rng.Next(100) < 10) return;
            await using var scope = services.CreateAsyncScope();
            using (SystemClock.TravelTo(at.AddMinutes(rng.Next(1, 8))))
                await scope.ServiceProvider.GetRequiredService<SimulatedGatewayDesk>().CompleteAsync(payment.PaymentId, success: true, ct);
        }

        foreach (var order in result.Orders)
            await FollowAsync(order, buyerId, at, now, age, rng, counters, ct);
    }

    private async Task FollowAsync(PlacedOrderDto order, Guid buyerId, DateTimeOffset at, DateTimeOffset now, TimeSpan age, Random rng, Counters counters,
        CancellationToken ct)
    {
        var fate = rng.Next(100);
        // The newest orders (under 40 hours, inside the shop's 2-day preparation deadline) are still on their way
        bool StopsHere(int percent) => age < TimeSpan.FromHours(40) && rng.Next(100) < percent;

        if (fate < 5)
        {
            await using var scope = services.CreateAsyncScope();
            using (SystemClock.TravelTo(at.AddMinutes(rng.Next(10, 90))))
                await SendAsync(scope, buyerId, new CancelMyOrderCommand(order.Code, "Đổi ý, không muốn mua nữa"), ct);
            return;
        }
        if (StopsHere(60)) return;

        // ----- the shop prepares it (drop-off at the post office) -----
        var shopOwner = await OwnerAsync(order.ShopId, ct);
        var prepared = at.AddHours(rng.Next(2, 30));
        await using (var scope = services.CreateAsyncScope())
        using (SystemClock.TravelTo(prepared))
            await SendAsync(scope, shopOwner, new PrepareOrdersCommand(order.ShopId, [order.Id], PickupMethod.DropOff, null), ct);
        if (StopsHere(50)) return;

        // ----- the simulated carrier moves it one step per run: picked → in transit → out for delivery → delivered / failed → back -----
        var moment = prepared;
        var failsDelivery = rng.Next(100) < 3;
        for (var step = 0; step < 6; step++)
        {
            moment = moment.AddHours(rng.Next(6, 20));
            if (moment > now) return;
            await using var scope = services.CreateAsyncScope();
            using (SystemClock.TravelTo(moment))
            {
                if (failsDelivery && await ShipmentAsync(order.Id, ct) is { Status: ShipmentStatus.OutForDelivery } parcel)
                    await ReportFailedDeliveryAsync(scope, parcel.TrackingNo, moment, ct);
                else
                    await scope.ServiceProvider.GetRequiredService<CarrierSimulator>().RunAsync(ct);
            }
            var status = await StatusAsync(order.Id, ct);
            if (status is OrderStatus.Delivered or OrderStatus.Returned or OrderStatus.Cancelled) break;
        }
        if (await StatusAsync(order.Id, ct) != OrderStatus.Delivered) return;

        // ----- the buyer: "Đã nhận được hàng" (or leaves it for the auto-completion), then reviews or a return -----
        var received = moment.AddHours(rng.Next(4, 60));
        if (received > now || rng.Next(100) < 12) return;
        if (rng.Next(100) < 4)
        {
            await ReturnAsync(order, buyerId, received, rng, counters, ct);
            return;
        }
        await using (var scope = services.CreateAsyncScope())
        using (SystemClock.TravelTo(received))
            await SendAsync(scope, buyerId, new ConfirmReceivedCommand(order.Code), ct);

        if (counters.Reviews >= ReviewTarget) return;
        var items = await ItemsAsync(order.Id, ct);
        var reviewed = received.AddHours(rng.Next(2, 96));
        if (reviewed > now) return;
        foreach (var itemId in items)
        {
            if (counters.Reviews >= ReviewTarget || rng.Next(100) >= 96) continue;
            var rating = rng.Next(100) switch { < 65 => 5, < 95 => 4, < 99 => 3, _ => 2 };
            var text = ReviewTexts[rng.Next(ReviewTexts.Length)];
            IReadOnlyList<string> tags = rating >= 4 ? Domain.Engage.Review.AllowedTags.OrderBy(_ => rng.Next()).Take(rng.Next(1, 3)).ToList() : [];
            await using var scope = services.CreateAsyncScope();
            using (SystemClock.TravelTo(reviewed))
                await SendAsync(scope, buyerId, new WriteReviewCommand(order.Code, itemId, rating, text.Length == 0 ? null : text, tags, rng.Next(100) < 15, null), ct);
            counters.Reviews++;
        }
    }

    /// <summary>"Chỉ hoàn tiền" for one line with a photo; the shop agrees — the refund follows the money back where it came from.</summary>
    private async Task ReturnAsync(PlacedOrderDto order, Guid buyerId, DateTimeOffset at, Random rng, Counters counters, CancellationToken ct)
    {
        var item = (await ItemsAsync(order.Id, ct)).First();
        ReturnDto request;
        await using (var scope = services.CreateAsyncScope())
        using (SystemClock.TravelTo(at))
        {
            var photo = await SendAsync(scope, buyerId, new UploadMediaCommand(EvidencePhoto(rng), MediaPurpose.Evidence), ct);
            request = await SendAsync(scope, buyerId, new CreateReturnCommand(order.Code, ReturnType.RefundOnly, ReturnReason.Damaged,
                "Sản phẩm bị móp méo khi nhận, hộp rách một góc.", [new ReturnLineInput(item, 1)], [photo.Id]), ct);
        }
        var shopOwner = await OwnerAsync(order.ShopId, ct);
        await using (var scope = services.CreateAsyncScope())
        using (SystemClock.TravelTo(at.AddHours(rng.Next(3, 30))))
            await SendAsync(scope, shopOwner, new ShopReturnActionCommand(order.ShopId, request.Id, ShopReturnAction.Approve, "Shop xin lỗi, đã hoàn tiền.", null,
                false, null), ct);
        counters.Returns++;
    }

    private sealed record Parcel(string TrackingNo, ShipmentStatus Status);

    private async Task<Parcel?> ShipmentAsync(Guid orderId, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ShopHubDbContext>().Shipments.AsNoTracking()
            .Where(s => s.OrderId == orderId && s.Direction == ShipmentDirection.Outbound).OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id)
            .Select(s => new Parcel(s.TrackingNo, s.Status)).FirstOrDefaultAsync(ct);
    }

    /// <summary>The simulated carrier reports a failed delivery through its signed webhook; it then brings the parcel back on its own.</summary>
    private static async Task ReportFailedDeliveryAsync(AsyncServiceScope scope, string trackingNo, DateTimeOffset at, CancellationToken ct)
    {
        var carrier = scope.ServiceProvider.GetRequiredService<SimulatedCarrier>();
        var body = carrier.Serialize(new SimulatedCarrier.WebhookBody($"{trackingNo}-seed-failed", trackingNo, ShipmentStatus.Failed, "Bưu cục giao hàng",
            "Giao không thành công: người nhận hẹn lại nhiều lần", at));
        await scope.ServiceProvider.GetRequiredService<CarrierWebhookIntake>().HandleAsync(SimulatedCarrier.ProviderName,
            InboundWebhook.FromBody(new Dictionary<string, string> { [SimulatedCarrier.SignatureHeader] = carrier.Sign(body) }, body), ct);
    }

    private async Task<Guid> OwnerAsync(Guid shopId, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ShopHubDbContext>().Shops.AsNoTracking().Where(s => s.Id == shopId).Select(s => s.OwnerId)
            .FirstAsync(ct);
    }

    private async Task<OrderStatus> StatusAsync(Guid orderId, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ShopHubDbContext>().Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.Status)
            .FirstAsync(ct);
    }

    private async Task<List<Guid>> ItemsAsync(Guid orderId, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ShopHubDbContext>().OrderItems.AsNoTracking().Where(i => i.OrderId == orderId)
            .OrderBy(i => i.Id).Select(i => i.Id).ToListAsync(ct);
    }

    private static async Task<T> SendAsync<T>(AsyncServiceScope scope, Guid actAs, IRequest<T> request, CancellationToken ct)
    {
        scope.ServiceProvider.GetRequiredService<ActingUser>().UserId = actAs;
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, ct);
    }

    private static byte[] EvidencePhoto(Random rng)
    {
        using var bmp = new SKBitmap(480, 360);
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(new SKColor((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256)));
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }
}
