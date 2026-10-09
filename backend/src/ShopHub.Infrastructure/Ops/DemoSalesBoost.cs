using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Account;
using ShopHub.Application.Features.Cart;
using ShopHub.Application.Features.Checkout;
using ShopHub.Application.Features.Orders;
using ShopHub.Application.Features.Seller;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;
using ShopHub.Infrastructure.Commerce;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Persistence;
using ShopHub.Infrastructure.Services;

namespace ShopHub.Infrastructure.Ops;

/// <summary>
/// G-VIS: a demo where the best sellers say "Đã bán 1,2k" needs that many units really sold — the spec forbids writing
/// counters. In demo mode only (SITE.MODE), this hourly background job gives the top 20 % of the catalogue (by sales)
/// a target of DEMO.SALES_BOOST_MAX_UNITS for the best one, tapering down (≥ 100 units), and places the missing orders
/// through the REAL commands: COD checkout of a sample buyer → shop preparation → the simulated carrier's signed
/// webhooks → "Đã nhận được hàng", each dated 7–89 days ago (old enough to be delivered and received by now) (the clock moved back like the
/// sample-order seeder). When a SKU runs short the shop restocks it first (an inventory movement, like a real seller).
/// Every run stops after a time budget and the next run continues, so the first start-up is never held up.
/// The orders are placed by 30 buyers of its own (0900000901…930, random passwords never printed, so nobody signs in as
/// them): the 20 sample accounts people use to try the demo keep their carts. SH_DEMO_SALES_BOOST=false switches the job
/// off for a process (the e2e stacks: CI and local runs must not see orders appear under them).
/// </summary>
public sealed class DemoSalesBoost(IServiceProvider services, ISystemParameters parameters, ShopHubSettings settings, ILogger<DemoSalesBoost> logger)
{
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(4);

    private sealed record Target(Guid ProductId, Guid ShopId, Guid OwnerId, int Sold, int Goal);

    public async Task<int> RunAsync(CancellationToken ct)
    {
        if (!settings.DemoSalesBoost || !await SiteMode.IsDemoAsync(parameters, ct)) return 0;
        var max = (int)await parameters.GetIntAsync(ParameterKeys.DemoSalesBoostMaxUnits, ct);
        if (max <= 0) return 0;

        List<Guid> buyers;
        List<Target> targets;
        await using (var root = services.CreateAsyncScope())
        {
            var db = root.ServiceProvider.GetRequiredService<ShopHubDbContext>();
            buyers = await EnsureBuyersAsync(root, ct);
            if (buyers.Count == 0) return 0;
            var catalogue = await (from p in db.Products.AsNoTracking()
                                   join s in db.Shops.AsNoTracking() on p.ShopId equals s.Id
                                   where p.Status == ProductStatus.Active && s.Status == ShopStatus.Active && p.MaxPerBuyer == null
                                   orderby p.SoldCount descending, p.Id
                                   select new { p.Id, p.ShopId, s.OwnerId, p.SoldCount }).ToListAsync(ct);
            var top = (int)Math.Ceiling(catalogue.Count * 0.2);
            // The best seller gets the whole target, the others taper (rank^-0.7) but never under 100 units
            targets = catalogue.Take(top)
                .Select((p, rank) => new Target(p.Id, p.ShopId, p.OwnerId, p.SoldCount, Math.Max(Math.Min(100, max), (int)(max * Math.Pow(rank + 1, -0.7)))))
                .Where(t => t.Sold < t.Goal).ToList();
        }
        var rng = new Random();
        var flash = await FlashBuysAsync(buyers, rng, ct);
        if (targets.Count == 0) return flash;

        var now = DateTimeOffset.UtcNow;
        var started = DateTime.UtcNow;
        var placed = 0;
        var remaining = targets.ToDictionary(t => t.ProductId, t => t.Goal - t.Sold);
        while (DateTime.UtcNow - started < Budget && remaining.Values.Any(v => v > 0))
        {
            foreach (var t in targets.Where(t => remaining[t.ProductId] > 0))
            {
                if (DateTime.UtcNow - started >= Budget) break;
                var quantity = Math.Min(remaining[t.ProductId], rng.Next(3, 21));
                try
                {
                    if (await OneOrderAsync(t, buyers[rng.Next(buyers.Count)], quantity, now.AddMinutes(-rng.Next(60 * 24 * 7, 60 * 24 * 89)), rng, ct))
                    {
                        remaining[t.ProductId] -= quantity;
                        placed++;
                    }
                    else remaining[t.ProductId] = 0;
                }
                catch (Exception ex) when (ex is Application.Common.ConflictException or Domain.Common.BusinessRuleException
                                               or Application.Common.NotFoundException or FluentValidation.ValidationException)
                {
                    // A rule refused this one (a price programme's limit, a shop on vacation…): leave the product for now
                    logger.LogInformation("Demo sales boost: order for {Product} refused: {Message}", t.ProductId, ex.Message);
                    remaining[t.ProductId] = 0;
                }
            }
        }

        // The buyers would long have read the notifications of these old orders
        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ShopHubDbContext>().Database.ExecuteSqlRawAsync("""
                update engage.notifications set is_read = true, read_at = least(now(), created_at + interval '1 hour')
                where not is_read and created_at < now() - interval '3 days'
                """, ct);
        if (placed + flash > 0) logger.LogInformation("Demo sales boost: {Orders} orders placed ({Flash} in the running Flash Sale) in {Seconds:0}s", placed + flash, flash, (DateTime.UtcNow - started).TotalSeconds);
        return placed + flash;
    }

    private static readonly string[] FamilyNames = ["Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Vũ", "Đặng", "Bùi", "Đỗ", "Hồ", "Ngô", "Dương"];
    private static readonly string[] GivenNames = ["Minh Anh", "Gia Bảo", "Thu Trang", "Quốc Khánh", "Ngọc Hân", "Đức Thịnh", "Thanh Tâm", "Hải Yến",
        "Tuấn Kiệt", "Phương Linh", "Bảo Châu", "Khánh Vy", "Hoàng Nam", "Mai Chi", "Trung Hiếu"];

    /// <summary>The job's own 30 buyers (created once, each with a default address in Hà Nội or TP. Hồ Chí Minh).</summary>
    private async Task<List<Guid>> EnsureBuyersAsync(AsyncServiceScope root, CancellationToken ct)
    {
        var db = root.ServiceProvider.GetRequiredService<ShopHubDbContext>();
        var phones = Enumerable.Range(901, 30).Select(n => $"0900000{n}").ToList();
        var existing = await db.Users.IgnoreQueryFilters().Where(u => u.Phone != null && phones.Contains(u.Phone)).Select(u => u.Phone!).ToListAsync(ct);
        if (existing.Count < phones.Count)
        {
            var hasher = root.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var now = DateTimeOffset.UtcNow;
            foreach (var (phone, i) in phones.Select((p, i) => (p, i)).Where(x => !existing.Contains(x.p)))
                db.Users.Add(User.Register(phone, null, hasher.Hash(Guid.NewGuid().ToString("N") + "Aa1"),
                    $"{FamilyNames[i % FamilyNames.Length]} {GivenNames[i % GivenNames.Length]}", now));
            await db.SaveChangesAsync(ct);
        }
        var users = await db.Users.AsNoTracking().Where(u => u.Phone != null && phones.Contains(u.Phone) && u.Status == UserStatus.Active)
            .OrderBy(u => u.Phone).Select(u => new { u.Id, u.Phone, u.FullName, HasAddress = db.Addresses.Any(a => a.UserId == u.Id && a.IsDefault) })
            .ToListAsync(ct);
        foreach (var u in users.Where(u => !u.HasAddress))
        {
            await using var scope = services.CreateAsyncScope();
            var sdb = scope.ServiceProvider.GetRequiredService<ShopHubDbContext>();
            var province = u.Phone!.EndsWith('1') || u.Phone.EndsWith('3') ? "01" : "79";
            var ward = await sdb.AdminDivisions.AsNoTracking()
                .Where(d => d.ParentCode == province && d.Level == AdminDivisionLevel.Ward && d.IsActive).OrderBy(d => d.Code)
                .Skip(int.Parse(u.Phone[^2..]) % 25).Select(d => d.Code).FirstOrDefaultAsync(ct);
            if (ward is null) continue;
            await SendAsync(scope, u.Id, new CreateAddressCommand(new AddressInput(u.FullName, u.Phone, province, ward,
                $"{10 + int.Parse(u.Phone[^2..])} Đường Mẫu", null, null, AddressType.Home, true)), ct);
        }
        return users.Select(u => u.Id).ToList();
    }

    /// <summary>
    /// The running platform Flash Sale gets real buyers too: each item is bought up to a share of its quota (30–85 %,
    /// stable per item) by sample buyers within their per-user limit, at the flash price, now. The shop prepares the
    /// order straight away and the simulated carrier delivers it over the following hours like any other parcel, so
    /// the bar reads "Đang bán chạy" / "Chỉ còn N" from units really taken.
    /// </summary>
    private async Task<int> FlashBuysAsync(List<Guid> buyers, Random rng, CancellationToken ct)
    {
        List<(Guid ItemId, Guid SkuId, Guid ShopId, Guid OwnerId, int Quota, int Sold, int Limit)> items;
        await using (var root = services.CreateAsyncScope())
        {
            var db = root.ServiceProvider.GetRequiredService<ShopHubDbContext>();
            var now = DateTimeOffset.UtcNow;
            items = (await (from i in db.FlashSaleItems.AsNoTracking()
                            join sl in db.FlashSaleSlots.AsNoTracking() on i.SlotId equals sl.Id
                            join sh in db.Shops.AsNoTracking() on i.ShopId equals sh.Id
                            where sl.Owner == Domain.Promo.FlashSaleOwner.Platform && sl.StartAt <= now && sl.EndAt > now
                                  && i.Status == Domain.Promo.FlashItemStatus.Approved
                            orderby i.Id
                            select new { i.Id, i.SkuId, i.ShopId, sh.OwnerId, i.Quota, i.Sold, i.PerUserLimit }).ToListAsync(ct))
                .Select(x => (x.Id, x.SkuId, x.ShopId, x.OwnerId, x.Quota, x.Sold, x.PerUserLimit)).ToList();
        }
        var placed = 0;
        foreach (var item in items)
        {
            // Stable share per item (string.GetHashCode is randomised per process)
            var share = 30 + item.ItemId.ToByteArray()[0] % 56;
            var goal = item.Quota * share / 100;
            var sold = item.Sold;
            foreach (var buyer in buyers.OrderBy(_ => rng.Next()))
            {
                if (sold >= goal) break;
                var quantity = Math.Max(1, Math.Min(item.Limit, goal - sold));
                try
                {
                    await using var scope = services.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<ShopHubDbContext>();
                    var owner = new CartOwner(buyer, null);
                    var cart = await SendAsync(scope, buyer, new GetCartQuery(owner), ct);
                    var leftovers = cart.Shops.SelectMany(s => s.Lines).Select(l => l.SkuId).ToList();
                    if (leftovers.Count > 0) await SendAsync(scope, buyer, new RemoveCartItemsCommand(owner, leftovers), ct);
                    await SendAsync(scope, buyer, new AddCartItemCommand(owner, item.SkuId, quantity), ct);
                    var addressId = await db.Addresses.AsNoTracking().Where(a => a.UserId == buyer && a.IsDefault).Select(a => a.Id).FirstAsync(ct);
                    var request = new CheckoutRequest(addressId, [new CheckoutShopChoice(item.ShopId, null, null, null)], null, null, false, PaymentMethod.Cod);
                    var quote = await SendAsync(scope, buyer, new QuoteCheckoutQuery(request), ct);
                    if (!quote.CanPlace) continue;
                    var order = (await SendAsync(scope, buyer, new PlaceOrderCommand(Guid.NewGuid().ToString("N"), request, quote.GrandTotal), ct)).Orders.Single();
                    await SendAsync(scope, item.OwnerId, new PrepareOrdersCommand(item.ShopId, [order.Id], PickupMethod.DropOff, null), ct);
                    sold += quantity;
                    placed++;
                }
                catch (Exception ex) when (ex is Application.Common.ConflictException or Domain.Common.BusinessRuleException
                                               or Application.Common.NotFoundException or FluentValidation.ValidationException)
                {
                    // This buyer already used their limit, or the stock / quota ran out: try the next one
                }
            }
        }
        return placed;
    }

    /// <summary>One COD order of <paramref name="quantity"/> units, carried to "Hoàn thành". False when the product has no buyable SKU.</summary>
    private async Task<bool> OneOrderAsync(Target t, Guid buyerId, int quantity, DateTimeOffset at, Random rng, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopHubDbContext>();
        var sku = await db.Skus.AsNoTracking().Where(s => s.ProductId == t.ProductId && s.IsActive).OrderBy(s => s.Price).ThenBy(s => s.Id)
            .Select(s => new { s.Id, Available = s.Stock - s.Reserved }).FirstOrDefaultAsync(ct);
        if (sku is null) return false;
        if (sku.Available < quantity + 5)
            using (SystemClock.TravelTo(at.AddHours(-1)))
                await SendAsync(scope, t.OwnerId, new AdjustStockCommand(t.ShopId, sku.Id, quantity + 100, "Nhập thêm hàng"), ct);

        PlacedOrderDto order;
        using (SystemClock.TravelTo(at))
        {
            var owner = new CartOwner(buyerId, null);
            var cart = await SendAsync(scope, buyerId, new GetCartQuery(owner), ct);
            var leftovers = cart.Shops.SelectMany(s => s.Lines).Select(l => l.SkuId).ToList();
            if (leftovers.Count > 0) await SendAsync(scope, buyerId, new RemoveCartItemsCommand(owner, leftovers), ct);
            await SendAsync(scope, buyerId, new AddCartItemCommand(owner, sku.Id, quantity), ct);
            var addressId = await db.Addresses.AsNoTracking().Where(a => a.UserId == buyerId && a.IsDefault).Select(a => a.Id).FirstAsync(ct);
            var request = new CheckoutRequest(addressId, [new CheckoutShopChoice(t.ShopId, null, null, null)], null, null, false, PaymentMethod.Cod);
            var quote = await SendAsync(scope, buyerId, new QuoteCheckoutQuery(request), ct);
            if (!quote.CanPlace) return false;
            order = (await SendAsync(scope, buyerId, new PlaceOrderCommand(Guid.NewGuid().ToString("N"), request, quote.GrandTotal), ct)).Orders.Single();
        }

        var moment = at.AddHours(rng.Next(2, 20));
        using (SystemClock.TravelTo(moment))
            await SendAsync(scope, t.OwnerId, new PrepareOrdersCommand(t.ShopId, [order.Id], PickupMethod.DropOff, null), ct);
        var tracking = await db.Shipments.AsNoTracking().Where(s => s.OrderId == order.Id && s.Direction == ShipmentDirection.Outbound)
            .OrderByDescending(s => s.CreatedAt).Select(s => s.TrackingNo).FirstAsync(ct);
        // The simulated carrier's signed webhooks, one step every few hours
        var carrier = scope.ServiceProvider.GetRequiredService<SimulatedCarrier>();
        var intake = scope.ServiceProvider.GetRequiredService<CarrierWebhookIntake>();
        foreach (var (status, text) in new[] { (ShipmentStatus.Picked, "Đã lấy hàng tại kho người gửi"), (ShipmentStatus.InTransit, "Đang trung chuyển tới kho phân loại"),
                     (ShipmentStatus.OutForDelivery, "Đang giao hàng tới người nhận"), (ShipmentStatus.Delivered, "Giao hàng thành công") })
        {
            moment = moment.AddHours(rng.Next(4, 16));
            var body = carrier.Serialize(new SimulatedCarrier.WebhookBody($"{tracking}-boost-{status}", tracking, status, null, text, moment));
            using (SystemClock.TravelTo(moment))
                await intake.HandleAsync(SimulatedCarrier.ProviderName,
                    InboundWebhook.FromBody(new Dictionary<string, string> { [SimulatedCarrier.SignatureHeader] = carrier.Sign(body) }, body), ct);
        }
        using (SystemClock.TravelTo(moment.AddHours(rng.Next(3, 48))))
            await SendAsync(scope, buyerId, new ConfirmReceivedCommand(order.Code), ct);
        return true;
    }

    private static async Task<T> SendAsync<T>(AsyncServiceScope scope, Guid actAs, IRequest<T> request, CancellationToken ct)
    {
        scope.ServiceProvider.GetRequiredService<ActingUser>().UserId = actAs;
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, ct);
    }
}

/// <summary>Hourly in demo mode (JOB.DEMO_SALES_BOOST_CRON); each run is time-boxed and resumes where the last stopped.</summary>
public sealed class DemoSalesBoostJob(DemoSalesBoost boost)
{
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 600)]
    [Hangfire.AutomaticRetry(Attempts = 0)]
    public Task RunJobAsync() => boost.RunAsync(CancellationToken.None);
}
