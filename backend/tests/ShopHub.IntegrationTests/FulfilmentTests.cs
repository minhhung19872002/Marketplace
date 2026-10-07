using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Orders;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;
using ShopHub.Infrastructure.Commerce;
using ShopHub.IntegrationTests.Infrastructure;
using UglyToad.PdfPig;

namespace ShopHub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class FulfilmentTests(ApiFactory factory)
{
    private sealed record Placed(TestUser Buyer, TestStore Store, Guid OrderId, string Code, Guid SkuId, HttpClient Seller);

    private async Task<HttpClient> SellerClientAsync(TestStore store)
    {
        // The fixture's owner has no usable password: sign in as a fresh staff member with every shop permission
        var staff = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(store.ShopId, staff.Id, Domain.Shops.ShopStaffRole.Manager, Application.Security.ShopPermissions.All));
            await db.SaveChangesAsync();
        });
        return staff.Client;
    }

    private async Task<Placed> PlaceAsync(string method = "Cod", int quantity = 1, int stock = 10, string? shopVoucher = null, long coins = 0,
        TestStore? store = null, string product = "Hộp Bút Gỗ")
    {
        store ??= await factory.CreateStoreAsync("79", products: [new(product, "Đèn Bàn", 120_000, stock, "Việt Nam")]);
        var buyer = await factory.CreateUserAsync();
        var addressId = await factory.AddAddressAsync(buyer.Id, "01");
        if (coins > 0) await factory.GrantCoinsAsync(buyer.Id, coins);
        var sku = store.Skus[product];
        (await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = sku, quantity })).EnsureSuccessStatusCode();
        var request = new
        {
            addressId,
            shops = new[] { new { shopId = store.ShopId, voucherCode = shopVoucher, carrierCode = (string?)null, note = "Giao giờ hành chính" } },
            platformVoucherCode = (string?)null, freeshipVoucherCode = (string?)null, useCoins = coins > 0, paymentMethod = method,
        };
        var quote = (await (await buyer.Client.PostAsJsonAsync("/api/checkout/quote", request)).ReadEnvelopeAsync()).Data;
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { checkout = request, expectedGrandTotal = quote.GetProperty("grandTotal").GetInt64() }),
        };
        msg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var res = await buyer.Client.SendAsync(msg);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var placed = (await res.ReadEnvelopeAsync()).Data;
        if (method == "Simulated")
            (await factory.CreateClient().PostAsync($"/api/payments/simulated/{placed.GetProperty("payment").Str("paymentId")}/success", null)).EnsureSuccessStatusCode();
        var order = placed.GetProperty("orders")[0];
        return new Placed(buyer, store, Guid.Parse(order.Str("id")), order.Str("code"), sku, await SellerClientAsync(store));
    }

    private static async Task<JsonElement> PrepareAsync(Placed p)
    {
        var res = await p.Seller.PostAsJsonAsync($"/api/seller/shops/{p.Store.ShopId}/orders/prepare",
            new { orderIds = new[] { p.OrderId }, pickupMethod = "Pickup", pickupSlot = "08:00 - 12:00" });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.ReadEnvelopeAsync()).Data[0];
    }

    private async Task<Order> OrderAsync(Guid id) => await factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == id));

    private Task<Domain.Catalog.Sku> SkuAsync(Guid id) => factory.WithDbAsync(db => db.Skus.AsNoTracking().SingleAsync(s => s.Id == id));

    private async Task WithParameterAsync(string key, string value, Func<Task> body)
    {
        var old = await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == key).Select(p => p.Value).SingleAsync());
        async Task Set(string v)
        {
            await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == key).ExecuteUpdateAsync(u => u.SetProperty(p => p.Value, v)));
            factory.Services.GetRequiredService<ISystemParameters>().Invalidate(key);
        }
        await Set(value);
        try
        {
            await body();
        }
        finally
        {
            await Set(old);
        }
    }

    private async Task<int> RunCarrierAsync(int times = 1)
    {
        var moved = 0;
        for (var i = 0; i < times; i++)
        {
            using var scope = factory.Services.CreateScope();
            moved += await scope.ServiceProvider.GetRequiredService<CarrierSimulator>().RunAsync(CancellationToken.None);
        }
        return moved;
    }

    private async Task RunAutomationAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OrderAutomationService>().RunAsync(CancellationToken.None);
    }

    // ---------- the happy path ----------

    [Fact]
    public async Task Cod_order_goes_from_confirmation_to_completed_through_the_simulated_carrier()
    {
        var p = await PlaceAsync(quantity: 2);
        var prepared = await PrepareAsync(p);
        var tracking = prepared.Str("trackingNo");

        var label = await p.Seller.GetAsync($"/api/seller/shops/{p.Store.ShopId}/orders/labels?ids={p.OrderId}");
        var bytes = await label.Content.ReadAsByteArrayAsync();
        label.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        using (var pdf = PdfDocument.Open(bytes))
        {
            var text = string.Join(" ", pdf.GetPage(1).GetWords().Select(w => w.Text));
            pdf.NumberOfPages.Should().Be(1);
            text.Should().Contain(tracking).And.Contain(p.Code).And.Contain("Người").And.Contain("Nhận").And.Contain("Thử", "chữ có dấu đọc lại đúng như ghi vào");
            text.Should().Contain("₫").And.Contain("Hộp");
        }

        (await OrderAsync(p.OrderId)).Status.Should().Be(OrderStatus.ReadyToShip);
        await WithParameterAsync(ParameterKeys.LogisticsSimStepSeconds, "0", async () =>
        {
            await RunCarrierAsync();
            var picked = await SkuAsync(p.SkuId);
            picked.Stock.Should().Be(8, "hãng lấy hàng thì trừ kho thật");
            picked.Reserved.Should().Be(0);
            (await OrderAsync(p.OrderId)).Status.Should().Be(OrderStatus.Shipping);
            await RunCarrierAsync(3);
        });

        var delivered = await OrderAsync(p.OrderId);
        delivered.Status.Should().Be(OrderStatus.Delivered);
        delivered.PaymentStatus.Should().Be(OrderPaymentStatus.Paid, "COD thu tiền khi giao");
        delivered.AutoCompleteAt.Should().NotBeNull();
        var track = (await (await factory.CreateClient().GetAsync($"/api/tracking/{tracking}")).ReadEnvelopeAsync()).Data;
        track.GetProperty("events").GetArrayLength().Should().Be(5);
        track.ToString().Should().NotContain("Người Nhận Thử", "tra cứu công khai không lộ thông tin người nhận");

        (await p.Buyer.Client.PostAsync($"/api/orders/{p.Code}/received", null)).EnsureSuccessStatusCode();
        (await OrderAsync(p.OrderId)).Status.Should().Be(OrderStatus.Completed);
        (await factory.WithDbAsync(db => db.Products.Where(x => x.Id == p.Store.Products["Hộp Bút Gỗ"]).Select(x => x.SoldCount).SingleAsync())).Should().Be(2);

        await factory.DispatchOutboxAsync();
        var titles = await factory.WithDbAsync(db => db.Notifications.Where(n => n.UserId == p.Buyer.Id).Select(n => n.Title).ToListAsync());
        titles.Should().Contain(["Đặt hàng thành công", "Shop đã xác nhận đơn hàng", "Đơn hàng đang được giao", "Giao hàng thành công", "Đơn hàng đã hoàn thành"]);
        var unread = (await (await p.Buyer.Client.GetAsync("/api/notifications/unread")).ReadEnvelopeAsync()).Data;
        unread.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public async Task Order_notifications_use_the_template_the_platform_edited_and_unknown_placeholders_are_refused()
    {
        var admin = await factory.ClientWithPermissionsAsync(Application.Security.Permissions.ContentManage);
        var templates = (await (await admin.GetAsync("/api/admin/message-templates")).ReadEnvelopeAsync()).Data;
        var codBuyer = templates.EnumerateArray().Single(t => t.Str("key") == "ORDER.PLACED_COD.BUYER" && t.Str("channel") == "InApp");
        var id = codBuyer.Str("id");
        var original = new { subject = codBuyer.Str("subject"), body = codBuyer.Str("body") };
        try
        {
            (await admin.PutAsJsonAsync($"/api/admin/message-templates/{id}", new { subject = "Đã nhận đơn {{code}}", body = "{{shop}} sẽ gọi xác nhận đơn {{code}} trị giá {{total}}." }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await admin.PutAsJsonAsync($"/api/admin/message-templates/{id}", new { subject = "x", body = "Mã giảm {{voucher}}" }))
                .StatusCode.Should().Be(HttpStatusCode.Conflict, "{{voucher}} is not a placeholder of order templates");
            (await admin.PutAsJsonAsync($"/api/admin/message-templates/{id}", new { subject = "", body = "Đơn {{code}}" }))
                .StatusCode.Should().Be(HttpStatusCode.Conflict, "an in-app notification needs a title");

            var p = await PlaceAsync();
            await factory.DispatchOutboxAsync();
            var n = await factory.WithDbAsync(db => db.Notifications.SingleAsync(x => x.UserId == p.Buyer.Id && x.RefId == p.OrderId));
            n.Title.Should().Be($"Đã nhận đơn {p.Code}");
            var total = await factory.WithDbAsync(db => db.Orders.Where(o => o.Id == p.OrderId).Select(o => o.GrandTotal).SingleAsync());
            n.Body.Should().Be($"Shop {p.Store.Marker} sẽ gọi xác nhận đơn {p.Code} trị giá ₫{total.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"))}.");
        }
        finally
        {
            (await admin.PutAsJsonAsync($"/api/admin/message-templates/{id}", original)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Deleting_the_account_waits_for_open_orders_and_the_wallet_and_the_export_holds_the_orders()
    {
        var p = await PlaceAsync();
        var password = ApiFactory.DefaultPassword;

        // "Tải dữ liệu của tôi" carries the order with its lines
        var export = (await (await p.Buyer.Client.GetAsync("/api/account/export")).ReadEnvelopeAsync()).Data;
        var order = export.GetProperty("orders").EnumerateArray().Single(o => o.Str("code") == p.Code);
        order.GetProperty("items")[0].Str("name").Should().StartWith("Hộp Bút Gỗ");

        // An order still waiting for the shop blocks the deletion, with what to do
        var blocked = await p.Buyer.Client.PostAsJsonAsync("/api/account/delete", new { password });
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blocked.ReadEnvelopeAsync()).Message.Should().Contain("đơn hàng chưa hoàn tất");

        (await p.Buyer.Client.PostAsJsonAsync($"/api/orders/{p.Code}/cancel", new { reason = "Đổi ý, không muốn mua nữa" })).EnsureSuccessStatusCode();

        // Money in Ví ShopHub blocks it too
        await factory.WithDbAsync(async db =>
        {
            db.LedgerAccounts.Add(new Domain.Finance.LedgerAccount(Domain.Finance.LedgerOwnerType.Buyer, p.Buyer.Id,
                Domain.Finance.LedgerAccountType.BuyerWallet, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE finance.ledger_accounts SET balance = 25000 WHERE owner_id = {p.Buyer.Id} AND type = 'BuyerWallet'");
        });
        var wallet = await p.Buyer.Client.PostAsJsonAsync("/api/account/delete", new { password });
        wallet.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await wallet.ReadEnvelopeAsync()).Message.Should().Contain("Ví ShopHub còn");
        await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE finance.ledger_accounts SET balance = 0 WHERE owner_id = {p.Buyer.Id} AND type = 'BuyerWallet'"));

        // Nothing left open: the account is anonymised, the order kept for accounting
        (await p.Buyer.Client.PostAsJsonAsync("/api/account/delete", new { password })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await OrderAsync(p.OrderId)).Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task A_parcel_that_cannot_be_delivered_comes_back_into_stock_and_the_money_goes_back()
    {
        var p = await PlaceAsync("Simulated", quantity: 1, coins: 5_000);
        await PrepareAsync(p);
        await WithParameterAsync(ParameterKeys.LogisticsSimStepSeconds, "0", () =>
            WithParameterAsync(ParameterKeys.LogisticsSimFailPercent, "100", () => RunCarrierAsync(6)));

        var order = await OrderAsync(p.OrderId);
        order.Status.Should().Be(OrderStatus.Returned);
        order.PaymentStatus.Should().Be(OrderPaymentStatus.Refunded);
        (await SkuAsync(p.SkuId)).Stock.Should().Be(10, "hàng hoàn về kho");
        (await factory.WithDbAsync(db => db.Refunds.Where(r => r.OrderId == p.OrderId).Select(r => r.Status).SingleAsync())).Should().Be(RefundStatus.Succeeded);
        (await factory.WithDbAsync(db => db.CoinLedger.Where(c => c.UserId == p.Buyer.Id).SumAsync(c => c.Delta))).Should().Be(5_000);
    }

    // ---------- cancellations ----------

    [Fact]
    public async Task Buyer_cancels_before_confirmation_and_gets_stock_voucher_and_coins_back()
    {
        var store = await factory.CreateStoreAsync(products: [new("Hộp Bút Gỗ", "Đèn Bàn", 120_000, 10, "Việt Nam")]);
        var voucher = await factory.CreateVoucherAsync(VoucherOwner.Shop, store.ShopId, VoucherType.Amount, value: 10_000, quota: 5);
        var p = await PlaceAsync(shopVoucher: voucher.Code, coins: 3_000, store: store);

        (await p.Buyer.Client.PostAsJsonAsync($"/api/orders/{p.Code}/cancel", new { reason = "Đổi ý, không muốn mua nữa" })).EnsureSuccessStatusCode();

        var order = await OrderAsync(p.OrderId);
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancelledBy.Should().Be(OrderActor.Buyer);
        (await SkuAsync(p.SkuId)).Reserved.Should().Be(0);
        (await factory.WithDbAsync(db => db.Vouchers.Where(v => v.Id == voucher.Id).Select(v => v.UsedCount).SingleAsync())).Should().Be(0);
        (await factory.WithDbAsync(db => db.CoinLedger.Where(c => c.UserId == p.Buyer.Id).SumAsync(c => c.Delta))).Should().Be(3_000);
    }

    [Fact]
    public async Task After_confirmation_the_buyer_must_ask_and_the_shop_may_refuse()
    {
        var p = await PlaceAsync();
        await PrepareAsync(p);

        var direct = await p.Buyer.Client.PostAsJsonAsync($"/api/orders/{p.Code}/cancel", new { reason = "Đặt nhầm" });
        (await p.Buyer.Client.PostAsJsonAsync($"/api/orders/{p.Code}/cancel-request", new { reason = "Đặt nhầm" })).EnsureSuccessStatusCode();
        var twice = await p.Buyer.Client.PostAsJsonAsync($"/api/orders/{p.Code}/cancel-request", new { reason = "Đặt nhầm" });
        var reject = await p.Seller.PostAsJsonAsync($"/api/seller/shops/{p.Store.ShopId}/orders/{p.OrderId}/cancel-request",
            new { approve = false, rejectReason = "Hàng đã đóng gói và bàn giao" });
        var again = await p.Buyer.Client.PostAsJsonAsync($"/api/orders/{p.Code}/cancel-request", new { reason = "Xin huỷ lần nữa" });

        direct.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await direct.ReadEnvelopeAsync()).Message.Should().Contain("gửi yêu cầu huỷ");
        twice.StatusCode.Should().Be(HttpStatusCode.Conflict, "mỗi đơn chỉ một yêu cầu huỷ đang chờ");
        reject.StatusCode.Should().Be(HttpStatusCode.OK);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await OrderAsync(p.OrderId)).Status.Should().Be(OrderStatus.ReadyToShip);
        await factory.DispatchOutboxAsync();
        (await factory.WithDbAsync(db => db.Notifications.AnyAsync(n => n.UserId == p.Buyer.Id && n.Title == "Shop từ chối yêu cầu huỷ"))).Should().BeTrue();
    }

    [Fact]
    public async Task An_unanswered_cancel_request_is_approved_automatically_and_the_pickup_is_called_off()
    {
        var p = await PlaceAsync();
        var tracking = (await PrepareAsync(p)).Str("trackingNo");
        (await p.Buyer.Client.PostAsJsonAsync($"/api/orders/{p.Code}/cancel-request", new { reason = "Tìm được chỗ rẻ hơn" })).EnsureSuccessStatusCode();
        await factory.WithDbAsync(db => db.OrderCancelRequests.Where(r => r.OrderId == p.OrderId)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.DueAt, DateTimeOffset.UtcNow.AddMinutes(-1))));

        await RunAutomationAsync();

        (await OrderAsync(p.OrderId)).Status.Should().Be(OrderStatus.Cancelled);
        (await factory.WithDbAsync(db => db.OrderCancelRequests.Where(r => r.OrderId == p.OrderId).Select(r => r.Status).SingleAsync()))
            .Should().Be(CancelRequestStatus.AutoApproved);
        (await factory.WithDbAsync(db => db.Shipments.Where(s => s.TrackingNo == tracking).Select(s => s.Status).SingleAsync())).Should().Be(ShipmentStatus.Cancelled);
        (await SkuAsync(p.SkuId)).Reserved.Should().Be(0);
    }

    [Fact]
    public async Task A_seller_cancelling_a_paid_order_refunds_it_to_the_gateway()
    {
        var p = await PlaceAsync("Simulated");
        var res = await p.Seller.PostAsJsonAsync($"/api/seller/shops/{p.Store.ShopId}/orders/{p.OrderId}/cancel", new { reason = "Hết hàng" });

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var order = await OrderAsync(p.OrderId);
        order.PaymentStatus.Should().Be(OrderPaymentStatus.Refunded);
        var paymentId = await factory.WithDbAsync(db => db.Payments.Where(x => x.CheckoutId == order.CheckoutId && x.Status == PaymentStatus.Succeeded)
            .Select(x => x.Id).SingleAsync());
        (await factory.WithDbAsync(db => db.SimulatedPayments.Where(s => s.PaymentId == paymentId).Select(s => s.RefundedAmount).SingleAsync()))
            .Should().Be(order.GrandTotal);
    }

    [Fact]
    public async Task Buyer_cancel_racing_the_seller_confirm_has_exactly_one_winner()
    {
        for (var round = 0; round < 3; round++)
        {
            var p = await PlaceAsync();
            var cancel = p.Buyer.Client.PostAsJsonAsync($"/api/orders/{p.Code}/cancel", new { reason = "Đổi ý" });
            var prepare = p.Seller.PostAsJsonAsync($"/api/seller/shops/{p.Store.ShopId}/orders/prepare",
                new { orderIds = new[] { p.OrderId }, pickupMethod = "DropOff", pickupSlot = (string?)null });
            await Task.WhenAll(cancel, prepare);

            var order = await OrderAsync(p.OrderId);
            var prepared = (await prepare.Result.ReadEnvelopeAsync()).Data[0].GetProperty("ok").GetBoolean();
            var shipments = await factory.WithDbAsync(db => db.Shipments.Where(s => s.OrderId == p.OrderId && s.Status != ShipmentStatus.Cancelled).CountAsync());
            if (order.Status == OrderStatus.Cancelled)
            {
                prepared.Should().BeFalse();
                shipments.Should().Be(0);
                (await SkuAsync(p.SkuId)).Reserved.Should().Be(0);
            }
            else
            {
                order.Status.Should().Be(OrderStatus.ReadyToShip);
                cancel.Result.StatusCode.Should().Be(HttpStatusCode.Conflict);
                prepared.Should().BeTrue();
                shipments.Should().Be(1);
            }
        }
    }

    // ---------- automation ----------

    [Fact]
    public async Task Delivered_orders_complete_themselves_and_late_shops_are_penalised_once()
    {
        var delivered = await PlaceAsync();
        await PrepareAsync(delivered);
        await WithParameterAsync(ParameterKeys.LogisticsSimStepSeconds, "0", () => RunCarrierAsync(4));
        await factory.WithDbAsync(db => db.Orders.Where(o => o.Id == delivered.OrderId)
            .ExecuteUpdateAsync(u => u.SetProperty(o => o.AutoCompleteAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        var late = await PlaceAsync();
        await factory.WithDbAsync(db => db.Orders.Where(o => o.Id == late.OrderId)
            .ExecuteUpdateAsync(u => u.SetProperty(o => o.CreatedAt, DateTimeOffset.UtcNow.AddDays(-10))));

        await RunAutomationAsync();
        await RunAutomationAsync();

        (await OrderAsync(delivered.OrderId)).Status.Should().Be(OrderStatus.Completed);
        var lateOrder = await OrderAsync(late.OrderId);
        lateOrder.Status.Should().Be(OrderStatus.Cancelled);
        lateOrder.CancelReason.Should().Be("Shop không chuẩn bị hàng đúng hạn");
        (await factory.WithDbAsync(db => db.Shops.Where(s => s.Id == late.Store.ShopId).Select(s => s.PenaltyPoints).SingleAsync())).Should().Be(1);
    }

    [Fact]
    public async Task Holidays_extend_the_shops_preparation_deadline_so_no_order_is_cancelled_or_penalised_over_tet()
    {
        var p = await PlaceAsync();
        var created = DateTimeOffset.UtcNow.AddDays(-5);
        await factory.WithDbAsync(db => db.Orders.Where(o => o.Id == p.OrderId).ExecuteUpdateAsync(u => u.SetProperty(o => o.CreatedAt, created)));
        // Every day from the order up to tomorrow is a public holiday (a Tết week): 2 working days have not passed yet
        var start = Application.Common.VietnamTime.Today(created);
        var holidays = Enumerable.Range(1, 7).Select(d => start.AddDays(d).ToString("yyyy-MM-dd")).ToList();
        await WithParameterAsync(ParameterKeys.LogisticsHolidays, System.Text.Json.JsonSerializer.Serialize(holidays), async () =>
        {
            await RunAutomationAsync();
            var order = await OrderAsync(p.OrderId);
            order.Status.Should().Be(OrderStatus.PendingConfirmation, "ngày lễ không tính vào hạn chuẩn bị hàng");
            (await factory.WithDbAsync(db => db.ShopPenalties.CountAsync(x => x.OrderId == p.OrderId))).Should().Be(0);
            var detail = (await (await p.Seller.GetAsync($"/api/seller/shops/{p.Store.ShopId}/orders/{p.OrderId}")).ReadEnvelopeAsync()).Data;
            DateOnly.Parse(detail.Str("shipDeadline")).Should().Be(Application.Common.WorkingCalendar.Add(start, 2,
                holidays.Select(DateOnly.Parse).ToHashSet(), new HashSet<DayOfWeek> { DayOfWeek.Sunday }), "hạn hiện cho shop cũng bỏ qua ngày lễ");
        });
    }

    // ---------- carrier webhook & ownership ----------

    [Fact]
    public async Task Carrier_webhooks_need_the_signature_and_replays_change_nothing()
    {
        var p = await PlaceAsync();
        var tracking = (await PrepareAsync(p)).Str("trackingNo");
        string body;
        string signature;
        using (var scope = factory.Services.CreateScope())
        {
            var carrier = scope.ServiceProvider.GetRequiredService<SimulatedCarrier>();
            body = carrier.Serialize(new SimulatedCarrier.WebhookBody($"EVT-{Guid.NewGuid():N}", tracking, ShipmentStatus.Picked, "Bưu cục", "Đã lấy hàng",
                DateTimeOffset.UtcNow));
            signature = carrier.Sign(body);
        }
        HttpRequestMessage Post(string sig)
        {
            var m = new HttpRequestMessage(HttpMethod.Post, "/api/logistics/webhooks/SIMULATED") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            m.Headers.Add(SimulatedCarrier.SignatureHeader, sig);
            return m;
        }

        var forged = await factory.CreateClient().SendAsync(Post(new string('a', 64)));
        var replies = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => factory.CreateClient().SendAsync(Post(signature))));

        forged.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        replies.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        (await factory.WithDbAsync(db => db.ShipmentEvents.CountAsync(e => db.Shipments.Any(s => s.Id == e.ShipmentId && s.TrackingNo == tracking))))
            .Should().Be(2, "tạo vận đơn + một lần lấy hàng");
        (await SkuAsync(p.SkuId)).Stock.Should().Be(9, "chỉ trừ kho một lần");
    }

    [Fact]
    public async Task Shops_and_buyers_only_reach_their_own_orders()
    {
        var p = await PlaceAsync();
        var otherStore = await factory.CreateStoreAsync(products: [new("Ly Thuỷ Tinh", "Bình Giữ Nhiệt", 50_000, 5, "Việt Nam")]);
        var otherSeller = await SellerClientAsync(otherStore);
        var stranger = await factory.CreateUserAsync();

        (await otherSeller.GetAsync($"/api/seller/shops/{otherStore.ShopId}/orders/{p.OrderId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var prepare = await otherSeller.PostAsJsonAsync($"/api/seller/shops/{otherStore.ShopId}/orders/prepare",
            new { orderIds = new[] { p.OrderId }, pickupMethod = "DropOff" });
        (await prepare.ReadEnvelopeAsync()).Data[0].GetProperty("ok").GetBoolean().Should().BeFalse();
        (await otherSeller.GetAsync($"/api/seller/shops/{p.Store.ShopId}/orders")).StatusCode.Should().Be(HttpStatusCode.NotFound, "không phải nhân viên shop này");
        (await stranger.Client.PostAsJsonAsync($"/api/orders/{p.Code}/cancel", new { reason = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await OrderAsync(p.OrderId)).Status.Should().Be(OrderStatus.PendingConfirmation);
    }

    [Fact]
    public async Task Picking_list_and_excel_export_are_real_files()
    {
        var store = await factory.CreateStoreAsync(products: [new("Hộp Bút Gỗ", "Đèn Bàn", 120_000, 20, "Việt Nam")]);
        var a = await PlaceAsync(quantity: 2, store: store);
        var b = await PlaceAsync(quantity: 3, store: store);
        await PrepareAsync(a);

        var picking = await a.Seller.GetAsync($"/api/seller/shops/{store.ShopId}/orders/picking-list?ids={a.OrderId}&ids={b.OrderId}");
        // Xuất Excel runs as a background task (6.4): the request only queues it; the file is not ready until it ran
        var started = await a.Seller.PostAsJsonAsync($"/api/seller/shops/{store.ShopId}/orders/export-tasks", new { tab = "All" });
        started.StatusCode.Should().Be(HttpStatusCode.OK);
        var taskId = (await started.ReadEnvelopeAsync()).Data.Str("id");
        (await a.Seller.GetAsync($"/api/seller/shops/{store.ShopId}/tasks/{taskId}/file")).StatusCode.Should().Be(HttpStatusCode.Conflict, "đang tạo tệp");
        using (var scope = factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<Application.Features.Seller.BulkTaskRunner>().RunAsync(Guid.Parse(taskId), CancellationToken.None))
                .Should().BeTrue();
        (await (await a.Seller.GetAsync($"/api/seller/shops/{store.ShopId}/bulk/tasks/{taskId}")).ReadEnvelopeAsync()).Data.Str("status").Should().Be("Done");
        var stranger = await SellerClientAsync(await factory.CreateStoreAsync());
        (await stranger.GetAsync($"/api/seller/shops/{store.ShopId}/tasks/{taskId}/file")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var excel = await a.Seller.GetAsync($"/api/seller/shops/{store.ShopId}/tasks/{taskId}/file");

        using (var pdf = PdfDocument.Open(await picking.Content.ReadAsByteArrayAsync()))
        {
            var words = string.Join(" ", pdf.GetPage(1).GetWords().Select(w => w.Text));
            words.Should().Contain("SOẠN").And.Contain(a.Code).And.Contain(b.Code).And.Contain(" 5 ");
        }
        using var book = new XLWorkbook(new MemoryStream(await excel.Content.ReadAsByteArrayAsync()));
        var sheet = book.Worksheet(1);
        sheet.Cell(1, 1).GetString().Should().Be("Mã đơn");
        sheet.RowsUsed().Count().Should().Be(3);
        sheet.Column(1).CellsUsed().Select(c => c.GetString()).Should().Contain([a.Code, b.Code]);
    }
}
