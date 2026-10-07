using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Checkout;
using ShopHub.Application.Features.Finance;
using ShopHub.Application.Features.Orders;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Sales;
using ShopHub.Infrastructure.Commerce;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>
/// Đa kho (spec 3.5): one order per shop, one parcel per ship-from warehouse — quoted on its own route, shipped on its own
/// tracking, its stock taken and given back on its own, and a parcel that comes back undelivered refunded on its own.
/// Plus the shop's warehouses (III.9) and its carrier choices (on / COD, per product).
/// </summary>
[Collection(ApiCollection.Name)]
public class MultiWarehouseTests(ApiFactory factory)
{
    private sealed record Setup(TestStore Store, HttpClient Seller, Guid NorthWarehouse, Guid SouthWarehouse, TestUser Buyer, Guid AddressId);

    private async Task<HttpClient> SellerClientAsync(TestStore store)
    {
        var staff = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(store.ShopId, staff.Id, Domain.Shops.ShopStaffRole.Manager, Application.Security.ShopPermissions.All));
            await db.SaveChangesAsync();
        });
        return staff.Client;
    }

    private async Task<object> AddressOf(string province) => await factory.WithDbAsync(async db =>
    {
        var district = await db.AdminDivisions.Where(d => d.ParentCode == province).OrderBy(d => d.Code).FirstAsync();
        var ward = await db.AdminDivisions.Where(d => d.ParentCode == district.Code).OrderBy(d => d.Code).FirstAsync();
        return (object)new { contactName = "Thủ Kho", phone = "0987654321", provinceCode = province, districtCode = district.Code, wardCode = ward.Code,
            street = "5 Đường Kho" };
    });

    /// <summary>Kho Hà Nội (default, from the fixture) + Kho Sài Gòn; "Quạt" ships from Hà Nội, "Nồi" from Sài Gòn; the buyer is in Sài Gòn.</summary>
    private async Task<Setup> SetupAsync(int stock = 10)
    {
        var store = await factory.CreateStoreAsync("01", products:
        [
            new("Quạt Đa Kho", "Đèn Bàn", 200_000, stock, "Việt Nam", WeightG: 1_500), new("Nồi Đa Kho", "Đèn Bàn", 100_000, stock, "Việt Nam", WeightG: 800),
        ]);
        var seller = await SellerClientAsync(store);
        var north = await factory.WithDbAsync(db => db.ShopWarehouses.Where(w => w.ShopId == store.ShopId).Select(w => w.Id).SingleAsync());

        // Turning đa kho on needs two warehouses
        (await seller.PutAsJsonAsync($"/api/seller/shops/{store.ShopId}/multi-warehouse", new { enabled = true })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var added = await seller.PostAsJsonAsync($"/api/seller/shops/{store.ShopId}/warehouses",
            new { name = "Kho Sài Gòn", address = await AddressOf("79"), isPickupDefault = false, isReturnDefault = false });
        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        var south = Guid.Parse((await added.ReadEnvelopeAsync()).Data.GetString()!);
        (await seller.PutAsJsonAsync($"/api/seller/shops/{store.ShopId}/multi-warehouse", new { enabled = true })).EnsureSuccessStatusCode();
        await factory.WithDbAsync(async db =>
        {
            var pot = await db.Products.SingleAsync(p => p.Id == store.Products["Nồi Đa Kho"]);
            pot.ShipFrom(south);
            await db.SaveChangesAsync();
        });

        var buyer = await factory.CreateUserAsync();
        var addressId = await factory.AddAddressAsync(buyer.Id, "79");
        foreach (var name in new[] { "Quạt Đa Kho", "Nồi Đa Kho" })
            (await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = store.Skus[name], quantity = 2 })).EnsureSuccessStatusCode();
        return new Setup(store, seller, north, south, buyer, addressId);
    }

    private static object Request(Setup s, string method = "Cod", string? carrier = null) => new
    {
        addressId = s.AddressId,
        shops = new[] { new { shopId = s.Store.ShopId, voucherCode = (string?)null, carrierCode = carrier, note = (string?)null } },
        platformVoucherCode = (string?)null, freeshipVoucherCode = (string?)null, useCoins = false, paymentMethod = method,
    };

    private static async Task<JsonElement> QuoteAsync(Setup s, string method = "Cod", string? carrier = null) =>
        (await (await s.Buyer.Client.PostAsJsonAsync("/api/checkout/quote", Request(s, method, carrier))).ReadEnvelopeAsync()).Data;

    private async Task<(Guid OrderId, string Code)> PlaceAsync(Setup s, string method = "Cod")
    {
        var quote = await QuoteAsync(s, method);
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { checkout = Request(s, method), expectedGrandTotal = quote.GetProperty("grandTotal").GetInt64() }),
        };
        msg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var res = await s.Buyer.Client.SendAsync(msg);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var placed = (await res.ReadEnvelopeAsync()).Data;
        if (method == "Simulated")
            (await factory.CreateClient().PostAsync($"/api/payments/simulated/{placed.GetProperty("payment").Str("paymentId")}/success", null)).EnsureSuccessStatusCode();
        placed.GetProperty("orders").GetArrayLength().Should().Be(1, "một shop một đơn, dù gửi từ hai kho");
        var order = placed.GetProperty("orders")[0];
        return (Guid.Parse(order.Str("id")), order.Str("code"));
    }

    private static async Task PrepareAsync(Setup s, Guid orderId)
    {
        var res = await s.Seller.PostAsJsonAsync($"/api/seller/shops/{s.Store.ShopId}/orders/prepare",
            new { orderIds = new[] { orderId }, pickupMethod = "Pickup", pickupSlot = "08:00 - 12:00" });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        (await res.ReadEnvelopeAsync()).Data[0].GetProperty("ok").GetBoolean().Should().BeTrue();
    }

    private async Task<List<Shipment>> ShipmentsAsync(Guid orderId) =>
        await factory.WithDbAsync(db => db.Shipments.AsNoTracking().Where(x => x.OrderId == orderId && x.Direction == ShipmentDirection.Outbound)
            .OrderBy(x => x.PackageNo).ToListAsync());

    private async Task CarrierSaysAsync(string tracking, params ShipmentStatus[] statuses)
    {
        foreach (var status in statuses)
        {
            using var scope = factory.Services.CreateScope();
            var carrier = scope.ServiceProvider.GetRequiredService<SimulatedCarrier>();
            var body = carrier.Serialize(new SimulatedCarrier.WebhookBody($"{tracking}-{status}-{Guid.NewGuid():N}", tracking, status, "Bưu cục",
                $"Hãng báo {status}", DateTimeOffset.UtcNow));
            var (accepted, result) = await scope.ServiceProvider.GetRequiredService<CarrierWebhookIntake>().HandleAsync(SimulatedCarrier.ProviderName,
                InboundWebhook.FromBody(new Dictionary<string, string> { [SimulatedCarrier.SignatureHeader] = carrier.Sign(body) }, body),
                CancellationToken.None);
            accepted.Should().BeTrue();
            result.Should().Be("APPLIED", $"{status} on {tracking}");
        }
    }

    private Task<Domain.Catalog.Sku> SkuAsync(Guid id) => factory.WithDbAsync(db => db.Skus.AsNoTracking().SingleAsync(x => x.Id == id));

    private async Task<Order> OrderAsync(Guid id) =>
        await factory.WithDbAsync(db => db.Orders.AsNoTracking().Include(o => o.Items).ThenInclude(i => i.Discounts).Include(o => o.Packages)
            .AsSplitQuery().SingleAsync(o => o.Id == id));

    [Fact]
    public async Task Two_warehouses_make_one_order_in_two_parcels_quoted_shipped_and_stocked_each_on_its_own()
    {
        var s = await SetupAsync();
        var quote = await QuoteAsync(s);
        var shop = quote.GetProperty("shops")[0];
        var parcels = shop.GetProperty("parcels");
        parcels.GetArrayLength().Should().Be(2);
        parcels[0].Str("warehouseName").Should().Be("Kho", "kiện 1 từ kho lấy hàng mặc định");
        parcels[1].Str("warehouseName").Should().Be("Kho Sài Gòn");

        // Each parcel is quoted on its own route and weight: the shop's fee is exactly their sum
        async Task<long> Expected(string from, int weightG)
        {
            using var scope = factory.Services.CreateScope();
            var calc = scope.ServiceProvider.GetRequiredService<ShippingCalculator>();
            var points = await factory.WithDbAsync(async db =>
            {
                var w = await db.ShopWarehouses.AsNoTracking().SingleAsync(x => x.ShopId == s.Store.ShopId && x.ProvinceCode == from);
                var a = await db.Addresses.AsNoTracking().SingleAsync(x => x.Id == s.AddressId);
                return (new RoutePoint(w.ProvinceCode, w.DistrictCode, w.WardCode), new RoutePoint(a.ProvinceCode, a.DistrictCode, a.WardCode));
            });
            var options = await calc.QuoteAsync(points.Item1, points.Item2, weightG, 0, CancellationToken.None);
            return options.Single(o => o.Code == shop.Str("carrierCode")).Fee;
        }
        var northFee = await Expected("01", 3_000);
        var fee = northFee + await Expected("79", 1_600);
        parcels[0].GetProperty("shippingFee").GetInt64().Should().Be(northFee);
        shop.GetProperty("shippingFee").GetInt64().Should().Be(fee);

        var (orderId, code) = await PlaceAsync(s);
        var order = await OrderAsync(orderId);
        order.ShippingFee.Should().Be(fee);
        order.Packages.OrderBy(p => p.No).Select(p => p.WarehouseId).Should().Equal(s.NorthWarehouse, s.SouthWarehouse);
        order.Items.Single(i => i.SkuId == s.Store.Skus["Nồi Đa Kho"]).PackageNo.Should().Be(2);

        await PrepareAsync(s, orderId);
        var shipments = await ShipmentsAsync(orderId);
        shipments.Select(x => x.PackageNo).Should().Equal(1, 2);
        shipments.Sum(x => x.CodAmount).Should().Be(order.GrandTotal, "COD chia theo kiện, cộng lại đúng tổng đơn");
        shipments.Select(x => x.Fee).Should().Equal(order.Packages.OrderBy(p => p.No).Select(p => p.ShippingFee));

        // Each parcel takes its own stock when picked up; the order ships at the first pickup and is delivered with the last parcel
        await CarrierSaysAsync(shipments[0].TrackingNo, ShipmentStatus.Picked);
        (await OrderAsync(orderId)).Status.Should().Be(OrderStatus.Shipping);
        (await SkuAsync(s.Store.Skus["Quạt Đa Kho"])).Stock.Should().Be(8);
        (await SkuAsync(s.Store.Skus["Nồi Đa Kho"])).Should().Match<Domain.Catalog.Sku>(k => k.Stock == 10 && k.Reserved == 2, "kiện 2 chưa rời kho");
        await CarrierSaysAsync(shipments[1].TrackingNo, ShipmentStatus.Picked);
        (await SkuAsync(s.Store.Skus["Nồi Đa Kho"])).Should().Match<Domain.Catalog.Sku>(k => k.Stock == 8 && k.Reserved == 0);
        await CarrierSaysAsync(shipments[0].TrackingNo, ShipmentStatus.Delivered);
        (await OrderAsync(orderId)).Status.Should().Be(OrderStatus.Shipping, "còn một kiện đang đi");
        await CarrierSaysAsync(shipments[1].TrackingNo, ShipmentStatus.Delivered);
        (await OrderAsync(orderId)).Status.Should().Be(OrderStatus.Delivered);

        // The buyer sees both parcels with their own tracking; the seller prints one label per parcel
        var detail = (await (await s.Buyer.Client.GetAsync($"/api/orders/{code}")).ReadEnvelopeAsync()).Data;
        detail.GetProperty("parcels").EnumerateArray().Select(p => p.GetProperty("shipment").Str("trackingNo"))
            .Should().Equal(shipments.Select(x => x.TrackingNo));
        var label = await s.Seller.GetAsync($"/api/seller/shops/{s.Store.ShopId}/orders/labels?ids={orderId}");
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(await label.Content.ReadAsByteArrayAsync());
        pdf.NumberOfPages.Should().Be(2);
        string.Join(" ", pdf.GetPage(2).GetWords().Select(w => w.Text)).Should().Contain(shipments[1].TrackingNo).And.Contain("Nồi").And.NotContain("Quạt");
    }

    [Fact]
    public async Task A_parcel_that_comes_back_while_the_other_arrives_is_refunded_on_its_own_and_the_books_balance()
    {
        var s = await SetupAsync();
        var (orderId, code) = await PlaceAsync(s, "Simulated");
        await PrepareAsync(s, orderId);
        var shipments = await ShipmentsAsync(orderId);
        await CarrierSaysAsync(shipments[0].TrackingNo, ShipmentStatus.Picked, ShipmentStatus.Delivered);
        await CarrierSaysAsync(shipments[1].TrackingNo, ShipmentStatus.Picked, ShipmentStatus.Failed);
        (await OrderAsync(orderId)).Status.Should().Be(OrderStatus.Shipping, "một kiện giao lỗi chưa phải cả đơn thất bại");
        await CarrierSaysAsync(shipments[1].TrackingNo, ShipmentStatus.Returning, ShipmentStatus.Returned);

        var order = await OrderAsync(orderId);
        order.Status.Should().Be(OrderStatus.Delivered, "kiện còn lại đã tới tay người mua");
        (await SkuAsync(s.Store.Skus["Nồi Đa Kho"])).Should().Match<Domain.Catalog.Sku>(k => k.Stock == 10 && k.Reserved == 0, "kiện hoàn về nhập lại kho");
        (await SkuAsync(s.Store.Skus["Quạt Đa Kho"])).Stock.Should().Be(8);

        var pot = order.Items.Single(i => i.PackageNo == 2);
        var package = order.Packages.Single(p => p.No == 2);
        var system = await factory.WithDbAsync(db => db.ReturnRequests.AsNoTracking().Include(r => r.Items).SingleAsync(r => r.OrderId == orderId));
        system.Reason.Should().Be(ReturnReason.UndeliveredParcel);
        system.Status.Should().Be(ReturnStatus.Refunded);
        system.RefundAmount.Should().Be(pot.PaidAmount + package.ShippingFee - package.ShippingDiscount, "tiền hàng của kiện + phí ship của kiện");
        var refund = await factory.WithDbAsync(db => db.Refunds.AsNoTracking().SingleAsync(r => r.OrderId == orderId));
        refund.Amount.Should().Be(system.RefundAmount!.Value);
        refund.Destination.Should().Be(RefundDestination.Gateway, "trả online thì hoàn về cổng");

        // Completed: the shop earns the delivered parcel only, the carrier is owed the delivered parcel only, and the books balance
        (await s.Buyer.Client.PostAsync($"/api/orders/{code}/received", null)).EnsureSuccessStatusCode();
        for (var i = 0; i < 20 && (await factory.DispatchOutboxAsync()).Processed > 0; i++) { }
        var breakdown = await factory.WithDbAsync(async db =>
        {
            using var scope = factory.Services.CreateScope();
            var ledger = scope.ServiceProvider.GetRequiredService<OrderLedger>();
            return await ledger.BreakdownAsync(await OrderAsync(orderId), CancellationToken.None);
        });
        breakdown.ShippingFee.Should().Be(order.Packages.Single(p => p.No == 1).ShippingFee);
        breakdown.MoneyKept.Should().Be(order.GrandTotal - system.RefundAmount!.Value);
        breakdown.RefundsBorne.Should().Be(pot.LineTotal - pot.Discounts.Where(d => d.Source is DiscountSource.Shop or DiscountSource.Combo).Sum(d => d.Amount));
        var entries = await factory.WithDbAsync(db => db.LedgerEntries.AsNoTracking().Where(e => e.RefId == orderId).ToListAsync());
        entries.Should().NotBeEmpty();
        entries.Where(e => e.Direction == Domain.Finance.LedgerDirection.Debit).Sum(e => e.Amount)
            .Should().Be(entries.Where(e => e.Direction == Domain.Finance.LedgerDirection.Credit).Sum(e => e.Amount));
    }

    [Fact]
    public async Task A_cod_parcel_that_never_arrives_is_not_paid_back_to_the_buyer_and_all_parcels_back_return_the_order()
    {
        var s = await SetupAsync();
        var (orderId, _) = await PlaceAsync(s);
        await PrepareAsync(s, orderId);
        var shipments = await ShipmentsAsync(orderId);
        await CarrierSaysAsync(shipments[0].TrackingNo, ShipmentStatus.Picked, ShipmentStatus.Delivered);
        await CarrierSaysAsync(shipments[1].TrackingNo, ShipmentStatus.Picked, ShipmentStatus.Failed, ShipmentStatus.Returning, ShipmentStatus.Returned);
        var refund = await factory.WithDbAsync(db => db.Refunds.AsNoTracking().SingleAsync(r => r.OrderId == orderId));
        refund.Destination.Should().Be(RefundDestination.Uncollected, "COD của kiện không giao được chưa từng thu");
        refund.Amount.Should().Be(shipments[1].CodAmount, "đúng số tiền hãng không thu được");

        // Every parcel back → the whole order is returned, as for a single parcel
        var t = await SetupAsync();
        var (other, _) = await PlaceAsync(t);
        await PrepareAsync(t, other);
        foreach (var x in await ShipmentsAsync(other))
            await CarrierSaysAsync(x.TrackingNo, ShipmentStatus.Picked, ShipmentStatus.Failed, ShipmentStatus.Returning, ShipmentStatus.Returned);
        (await OrderAsync(other)).Status.Should().Be(OrderStatus.Returned);
        (await SkuAsync(t.Store.Skus["Quạt Đa Kho"])).Should().Match<Domain.Catalog.Sku>(k => k.Stock == 10 && k.Reserved == 0);
        (await SkuAsync(t.Store.Skus["Nồi Đa Kho"])).Should().Match<Domain.Catalog.Sku>(k => k.Stock == 10 && k.Reserved == 0);
    }

    [Fact]
    public async Task Multi_warehouse_off_ships_everything_from_the_default_pickup_in_one_parcel()
    {
        var s = await SetupAsync();
        (await s.Seller.PutAsJsonAsync($"/api/seller/shops/{s.Store.ShopId}/multi-warehouse", new { enabled = false })).EnsureSuccessStatusCode();
        var parcels = (await QuoteAsync(s)).GetProperty("shops")[0].GetProperty("parcels");
        parcels.GetArrayLength().Should().Be(1);
        parcels[0].GetProperty("productIds").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Shop_carrier_choices_and_product_carriers_decide_the_options_and_cod()
    {
        var s = await SetupAsync();
        var logistics = (await (await s.Seller.GetAsync($"/api/seller/shops/{s.Store.ShopId}/logistics")).ReadEnvelopeAsync()).Data;
        var channels = logistics.GetProperty("channels").EnumerateArray().Where(c => c.GetProperty("carrierActive").GetBoolean()).ToList();
        var before = (await QuoteAsync(s)).GetProperty("shops")[0].GetProperty("shippingOptions").EnumerateArray().Select(o => o.Str("code")).ToList();
        before.Count.Should().BeGreaterThan(1);
        var off = before[0];
        var kept = before[1];
        try
        {
            (await s.Seller.PutAsJsonAsync($"/api/seller/shops/{s.Store.ShopId}/shipping-channels/{off}", new { enabled = false, codEnabled = false }))
                .EnsureSuccessStatusCode();
            var after = await QuoteAsync(s);
            after.GetProperty("shops")[0].GetProperty("shippingOptions").EnumerateArray().Select(o => o.Str("code")).Should().NotContain(off);

            // COD off for the carrier the buyer gets → COD is not offered
            (await s.Seller.PutAsJsonAsync($"/api/seller/shops/{s.Store.ShopId}/shipping-channels/{kept}", new { enabled = true, codEnabled = false }))
                .EnsureSuccessStatusCode();
            var noCod = await QuoteAsync(s, carrier: kept);
            noCod.GetProperty("paymentMethods").EnumerateArray().Single(m => m.Str("code") == "Cod").GetProperty("available").GetBoolean().Should().BeFalse();

            // Every active carrier off → refused
            foreach (var c in channels.Select(c => c.Str("carrierCode")).Where(c => c != off && c != kept))
                (await s.Seller.PutAsJsonAsync($"/api/seller/shops/{s.Store.ShopId}/shipping-channels/{c}", new { enabled = false, codEnabled = true }))
                    .EnsureSuccessStatusCode();
            (await s.Seller.PutAsJsonAsync($"/api/seller/shops/{s.Store.ShopId}/shipping-channels/{kept}", new { enabled = false, codEnabled = true }))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var c in channels)
                await s.Seller.PutAsJsonAsync($"/api/seller/shops/{s.Store.ShopId}/shipping-channels/{c.Str("carrierCode")}", new { enabled = true, codEnabled = true });
        }

        // A product limited to one carrier: only that carrier is offered for the shop
        await factory.WithDbAsync(async db =>
        {
            var fan = await db.Products.SingleAsync(p => p.Id == s.Store.Products["Quạt Đa Kho"]);
            fan.LimitCarriers([kept]);
            await db.SaveChangesAsync();
        });
        (await QuoteAsync(s)).GetProperty("shops")[0].GetProperty("shippingOptions").EnumerateArray().Select(o => o.Str("code")).Should().Equal(kept);
    }

    [Fact]
    public async Task Warehouses_keep_one_default_each_and_cannot_vanish_under_waiting_parcels_or_other_shops()
    {
        var s = await SetupAsync();
        var url = $"/api/seller/shops/{s.Store.ShopId}/warehouses";
        // The south warehouse becomes the return address: the flag moves, it is never on two or none
        (await s.Seller.PutAsJsonAsync($"{url}/{s.SouthWarehouse}",
            new { name = "Kho Sài Gòn", address = await AddressOf("79"), isPickupDefault = false, isReturnDefault = true })).EnsureSuccessStatusCode();
        var list = (await (await s.Seller.GetAsync($"/api/seller/shops/{s.Store.ShopId}/logistics")).ReadEnvelopeAsync()).Data.GetProperty("warehouses");
        list.EnumerateArray().Count(w => w.GetProperty("isReturnDefault").GetBoolean()).Should().Be(1);
        list.EnumerateArray().Single(w => w.GetProperty("isReturnDefault").GetBoolean()).Str("id").Should().Be(s.SouthWarehouse.ToString());
        (await s.Seller.PutAsJsonAsync($"{url}/{s.SouthWarehouse}",
            new { name = "Kho Sài Gòn", address = await AddressOf("79"), isPickupDefault = false, isReturnDefault = false }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "bỏ cờ của địa chỉ trả hàng duy nhất");
        (await s.Seller.DeleteAsync($"{url}/{s.NorthWarehouse}")).StatusCode.Should().Be(HttpStatusCode.Conflict, "kho lấy hàng mặc định");

        // Another shop's staff: 404, not 403
        var stranger = await SellerClientAsync(await factory.CreateStoreAsync("01"));
        (await stranger.DeleteAsync($"{url}/{s.SouthWarehouse}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A parcel still waiting to leave from it keeps it; once the order is gone, deleting moves its products to the default
        (await s.Seller.PutAsJsonAsync($"{url}/{s.NorthWarehouse}",
            new { name = "Kho", address = await AddressOf("01"), isPickupDefault = true, isReturnDefault = true })).EnsureSuccessStatusCode();
        var (orderId, code) = await PlaceAsync(s);
        (await s.Seller.DeleteAsync($"{url}/{s.SouthWarehouse}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await s.Buyer.Client.PostAsJsonAsync($"/api/orders/{code}/cancel", new { reason = "Đổi ý" })).EnsureSuccessStatusCode();
        (await s.Seller.DeleteAsync($"{url}/{s.SouthWarehouse}")).EnsureSuccessStatusCode();
        (await factory.WithDbAsync(db => db.Products.Where(p => p.Id == s.Store.Products["Nồi Đa Kho"]).Select(p => p.WarehouseId).SingleAsync()))
            .Should().BeNull();
        (await OrderAsync(orderId)).Status.Should().Be(OrderStatus.Cancelled);
    }
}
