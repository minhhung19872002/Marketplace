using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Features.Orders;
using ShopHub.Application.Features.Payments;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Sales;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>Phase 11: VNPay, MoMo, GHN and GHTK through their documented contracts (sandboxes faked in process).</summary>
[Collection(ApiCollection.Name)]
public class ProviderTests(ApiFactory factory)
{
    private const string Product = "Bình Giữ Nhiệt";
    private FakeProviders Fakes => factory.Providers;

    private sealed record Placed(TestUser Buyer, TestStore Store, Guid CheckoutId, Guid OrderId, string Code, long Total, JsonElement Payment);

    private async Task<Placed> PlaceAsync(string method, string? carrier = null, TestStore? store = null)
    {
        store ??= await factory.CreateStoreAsync("79", products: [new(Product, "Đèn Bàn", 150_000, 20, "Việt Nam")]);
        var buyer = await factory.CreateUserAsync();
        var addressId = await factory.AddAddressAsync(buyer.Id, "01");
        (await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = store.Skus[Product], quantity = 1 })).EnsureSuccessStatusCode();
        var request = new
        {
            addressId,
            shops = new[] { new { shopId = store.ShopId, voucherCode = (string?)null, carrierCode = carrier, note = (string?)null } },
            platformVoucherCode = (string?)null, freeshipVoucherCode = (string?)null, useCoins = false, paymentMethod = method,
        };
        var quote = (await (await buyer.Client.PostAsJsonAsync("/api/checkout/quote", request)).ReadEnvelopeAsync()).Data;
        quote.GetProperty("canPlace").GetBoolean().Should().BeTrue(quote.GetProperty("problems").ToString());
        if (carrier is not null) quote.GetProperty("shops")[0].GetProperty("carrierCode").GetString().Should().Be(carrier);
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { checkout = request, expectedGrandTotal = quote.GetProperty("grandTotal").GetInt64() }),
        };
        msg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var res = await buyer.Client.SendAsync(msg);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var placed = (await res.ReadEnvelopeAsync()).Data;
        var order = placed.GetProperty("orders")[0];
        return new Placed(buyer, store, Guid.Parse(placed.Str("checkoutId")), Guid.Parse(order.Str("id")), order.Str("code"),
            placed.GetProperty("grandTotal").GetInt64(), placed.GetProperty("payment"));
    }

    private async Task<HttpClient> SellerAsync(TestStore store)
    {
        var staff = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(store.ShopId, staff.Id, Domain.Shops.ShopStaffRole.Manager, Application.Security.ShopPermissions.All));
            await db.SaveChangesAsync();
        });
        return staff.Client;
    }

    private static Dictionary<string, string> QueryOf(string url) =>
        QueryHelpers.ParseQuery(new Uri(url).Query).ToDictionary(q => q.Key, q => q.Value.ToString());

    /// <summary>An IPN as VNPay sends it: the vnp_* fields plus vnp_SecureHash over them (documented hashData).</summary>
    private static string VnPayIpn(Dictionary<string, string> fields)
    {
        var hash = FakeProviders.Hmac512(FakeProviders.VnPayHashData(fields));
        return "/api/payments/webhooks/vnpay?" + FakeProviders.VnPayHashData(fields) + "&vnp_SecureHashType=HmacSHA512&vnp_SecureHash=" + hash;
    }

    private static Dictionary<string, string> VnPayResult(Guid paymentId, long amount, string txnNo, string response = "00", string status = "00") => new()
    {
        ["vnp_Amount"] = (amount * 100).ToString(), ["vnp_BankCode"] = "NCB", ["vnp_BankTranNo"] = "VNP" + txnNo, ["vnp_CardType"] = "ATM",
        ["vnp_OrderInfo"] = "Thanh toan don hang", ["vnp_PayDate"] = "20261006103000", ["vnp_ResponseCode"] = response, ["vnp_TmnCode"] = FakeProviders.VnPayTmn,
        ["vnp_TransactionNo"] = txnNo, ["vnp_TransactionStatus"] = status, ["vnp_TxnRef"] = paymentId.ToString("N"),
    };

    private static async Task<string> RspCodeAsync(HttpResponseMessage res)
    {
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonNode.Parse(await res.Content.ReadAsStringAsync())!["RspCode"]!.GetValue<string>();
    }

    [Fact]
    public async Task Vnpay_pays_through_a_signed_url_and_ipn_once_and_refunds_on_cancel()
    {
        var p = await PlaceAsync("VnPay");
        var url = p.Payment.Str("redirectUrl");
        url.Should().StartWith("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?");
        var q = QueryOf(url);
        q["vnp_TmnCode"].Should().Be(FakeProviders.VnPayTmn);
        q["vnp_Amount"].Should().Be((p.Total * 100).ToString(), "VNPay nhận số tiền × 100");
        q["vnp_ReturnUrl"].Should().EndWith($"/thanh-toan/ket-qua/{p.CheckoutId}");
        q["vnp_OrderInfo"].Should().StartWith("Thanh toan don hang ", "mô tả không dấu");
        var signed = q.Where(f => f.Key != "vnp_SecureHash").ToDictionary(f => f.Key, f => f.Value);
        q["vnp_SecureHash"].Should().Be(FakeProviders.Hmac512(FakeProviders.VnPayHashData(signed)), "chữ ký HMAC-SHA512 đúng tài liệu VNPay");

        var paymentId = Guid.Parse(p.Payment.Str("paymentId"));
        var client = factory.CreateClient();
        // Forged / tampered notifications are refused with 97
        var tampered = VnPayIpn(VnPayResult(paymentId, p.Total, "14000001")).Replace($"vnp_Amount={p.Total * 100}", "vnp_Amount=100");
        (await RspCodeAsync(await client.GetAsync(tampered))).Should().Be("97");
        // A signed notification with another amount is not applied (04)
        (await RspCodeAsync(await client.GetAsync(VnPayIpn(VnPayResult(paymentId, p.Total - 1_000, "14000002"))))).Should().Be("04");

        (await RspCodeAsync(await client.GetAsync(VnPayIpn(VnPayResult(paymentId, p.Total, "14000003"))))).Should().Be("00");
        (await RspCodeAsync(await client.GetAsync(VnPayIpn(VnPayResult(paymentId, p.Total, "14000003"))))).Should().Be("02", "IPN lặp lại chỉ được áp dụng một lần");
        var order = await factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == p.OrderId));
        order.PaymentStatus.Should().Be(OrderPaymentStatus.Paid);
        order.Status.Should().Be(OrderStatus.PendingConfirmation);
        (await factory.WithDbAsync(db => db.Payments.AsNoTracking().SingleAsync(x => x.Id == paymentId))).ProviderTxnId.Should().Be("14000003");

        // Cancelling the paid order refunds through VNPay's refund API (full refund, type 02)
        Fakes.VnPayPaid[paymentId.ToString("N")] = ("14000003", p.Total);
        (await p.Buyer.Client.PostAsJsonAsync($"/api/orders/{p.Code}/cancel", new { reason = "Đổi ý" })).EnsureSuccessStatusCode();
        var refund = Fakes.CallsTo(FakeProviders.VnPayHost, "/merchant_webapi").Select(c => JsonNode.Parse(c.Body)!)
            .Last(b => b["vnp_Command"]!.GetValue<string>() == "refund" && b["vnp_TxnRef"]!.GetValue<string>() == paymentId.ToString("N"));
        refund["vnp_TransactionType"]!.GetValue<string>().Should().Be("02");
        refund["vnp_Amount"]!.GetValue<string>().Should().Be((p.Total * 100).ToString());
        refund["vnp_TransactionNo"]!.GetValue<string>().Should().Be("14000003");
        (await factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == p.OrderId))).PaymentStatus.Should().Be(OrderPaymentStatus.Refunded);
    }

    [Fact]
    public async Task A_vnpay_payment_whose_ipn_never_came_is_found_by_querying_vnpay_before_the_order_expires()
    {
        var p = await PlaceAsync("VnPay");
        var paymentId = Guid.Parse(p.Payment.Str("paymentId"));
        Fakes.VnPayPaid[paymentId.ToString("N")] = ("14009999", p.Total);
        await factory.WithDbAsync(db => db.CheckoutSessions.Where(c => c.Id == p.CheckoutId)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.PaymentExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PaymentExpiryService>().RunAsync(CancellationToken.None);

        var order = await factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == p.OrderId));
        order.PaymentStatus.Should().Be(OrderPaymentStatus.Paid, "querydr cho biết đã trả tiền nên đơn không bị huỷ");
        Fakes.CallsTo(FakeProviders.VnPayHost, "/merchant_webapi").Select(c => JsonNode.Parse(c.Body)!)
            .Should().Contain(b => b["vnp_Command"]!.GetValue<string>() == "querydr" && b["vnp_TxnRef"]!.GetValue<string>() == paymentId.ToString("N"));
    }

    private static object MoMoIpn(string orderId, long amount, long transId, int resultCode = 0, string? signature = null)
    {
        var n = new
        {
            partnerCode = FakeProviders.MoMoPartner, orderId, requestId = orderId, amount, orderInfo = "Nap tien vao Vi ShopHub", orderType = "momo_wallet",
            transId, resultCode, message = resultCode == 0 ? "Thành công." : "Giao dịch bị từ chối.", payType = "qr", responseTime = 1_791_000_000_000L,
            extraData = "",
        };
        var raw = $"accessKey={FakeProviders.MoMoAccess}&amount={n.amount}&extraData={n.extraData}&message={n.message}&orderId={n.orderId}" +
                  $"&orderInfo={n.orderInfo}&orderType={n.orderType}&partnerCode={n.partnerCode}&payType={n.payType}&requestId={n.requestId}" +
                  $"&responseTime={n.responseTime}&resultCode={n.resultCode}&transId={n.transId}";
        return new
        {
            n.partnerCode, n.orderId, n.requestId, n.amount, n.orderInfo, n.orderType, n.transId, n.resultCode, n.message, n.payType, n.responseTime,
            n.extraData, signature = signature ?? FakeProviders.Hmac256(raw),
        };
    }

    [Fact]
    public async Task A_momo_wallet_topup_is_credited_once_from_the_signed_ipn()
    {
        var user = await factory.CreateUserAsync();
        var gateways = (await (await user.Client.GetAsync("/api/wallet/topup-gateways")).ReadEnvelopeAsync()).Data;
        gateways.EnumerateArray().Select(g => g.Str("method")).Should().Contain(["VnPay", "MoMo", "Simulated"]);

        var started = (await (await user.Client.PostAsJsonAsync("/api/wallet/topups", new { amount = 200_000, method = "MoMo" })).ReadEnvelopeAsync()).Data;
        var orderId = Guid.Parse(started.Str("paymentId")).ToString("N");
        started.Str("redirectUrl").Should().StartWith("https://test-payment.momo.vn/");
        var create = JsonNode.Parse(Fakes.CallsTo(FakeProviders.MoMoHost, "/v2/gateway/api/create").Last(c => c.Body.Contains(orderId)).Body)!;
        create["ipnUrl"]!.GetValue<string>().Should().Be("https://callback.shophub.test/api/payments/webhooks/momo");
        create["redirectUrl"]!.GetValue<string>().Should().EndWith($"/tai-khoan/vi?topup={started.Str("topupId")}");

        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/payments/webhooks/momo", MoMoIpn(orderId, 200_000, 3_100_000_001, signature: new string('0', 64))))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "chữ ký sai");
        (await client.PostAsJsonAsync("/api/payments/webhooks/momo", MoMoIpn(orderId, 200_000, 3_100_000_001))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync("/api/payments/webhooks/momo", MoMoIpn(orderId, 200_000, 3_100_000_001))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var wallet = (await (await user.Client.GetAsync("/api/wallet")).ReadEnvelopeAsync()).Data;
        wallet.GetProperty("balance").GetInt64().Should().Be(200_000, "IPN lặp lại không cộng tiền hai lần");
        (await (await user.Client.GetAsync($"/api/wallet/topups/{started.Str("topupId")}")).ReadEnvelopeAsync()).Data.Str("status").Should().Be("Succeeded");
    }

    [Fact]
    public async Task Ghn_quotes_books_with_its_own_district_ids_and_follows_webhooks_and_missed_events()
    {
        await factory.SetCarrierActiveAsync("GHN_STD", true);
        try
        {
            var p = await PlaceAsync("Cod", carrier: "GHN_STD");
            var fee = JsonNode.Parse(Fakes.CallsTo(FakeProviders.GhnHost, "/shiip/public-api/v2/shipping-order/fee").Last().Body)!;
            var (warehouseDistrict, buyerDistrict, buyerWard) = await factory.WithDbAsync(async db =>
            {
                var w = await db.ShopWarehouses.AsNoTracking().SingleAsync(x => x.ShopId == p.Store.ShopId);
                var a = await db.Addresses.AsNoTracking().Where(x => x.UserId == p.Buyer.Id).SingleAsync();
                return (w.DistrictCode, a.DistrictCode, a.WardCode);
            });
            fee["from_district_id"]!.GetValue<int>().Should().Be(int.Parse(warehouseDistrict) + 100_000, "mã quận của GHN, không phải mã Tổng cục Thống kê");
            fee["to_ward_code"]!.GetValue<string>().Should().Be($"W{buyerWard}");

            var seller = await SellerAsync(p.Store);
            var prepared = await seller.PostAsJsonAsync($"/api/seller/shops/{p.Store.ShopId}/orders/prepare",
                new { orderIds = new[] { p.OrderId }, pickupMethod = "Pickup", pickupSlot = "08:00 - 12:00" });
            prepared.StatusCode.Should().Be(HttpStatusCode.OK, await prepared.Content.ReadAsStringAsync());
            var tracking = (await prepared.ReadEnvelopeAsync()).Data[0].Str("trackingNo");
            tracking.Should().StartWith("GHN");
            var create = Fakes.CallsTo(FakeProviders.GhnHost, "/shiip/public-api/v2/shipping-order/create").Select(c => JsonNode.Parse(c.Body)!)
                .Single(b => b["client_order_code"]!.GetValue<string>() == p.Code);
            create["to_district_id"]!.GetValue<int>().Should().Be(int.Parse(buyerDistrict) + 100_000);
            create["cod_amount"]!.GetValue<long>().Should().Be(p.Total, "đơn COD: GHN thu hộ đúng tổng tiền");
            create["to_name"]!.GetValue<string>().Should().Be("Người Nhận Thử");
            create["items"]!.AsArray().Should().ContainSingle();

            var client = factory.CreateClient();
            HttpRequestMessage Hook(string token, string status, DateTimeOffset at) =>
                new(HttpMethod.Post, $"/api/logistics/webhooks/GHN?token={token}")
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { OrderCode = tracking, Status = status, Time = at, Type = "switch_status", Reason = "" }),
                        Encoding.UTF8, "application/json"),
                };
            (await client.SendAsync(Hook("sai-token", "picked", DateTimeOffset.UtcNow))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await client.SendAsync(Hook(FakeProviders.GhnWebhookToken, "picked", DateTimeOffset.UtcNow))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == p.OrderId))).Status.Should().Be(OrderStatus.Shipping);

            // GHN delivered the parcel but its webhooks were lost: the sync job asks GHN and catches up
            var now = DateTimeOffset.UtcNow;
            Fakes.GhnLogs[tracking] = [("picked", now.AddHours(-3)), ("transporting", now.AddHours(-2)), ("delivering", now.AddHours(-1)), ("delivered", now)];
            await factory.WithDbAsync(db => db.Shipments.Where(s => s.TrackingNo == tracking)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.LastEventAt, DateTimeOffset.UtcNow.AddHours(-5))));
            using (var scope = factory.Services.CreateScope())
                (await scope.ServiceProvider.GetRequiredService<CarrierSyncService>().RunAsync(CancellationToken.None)).Should().BeGreaterThanOrEqualTo(3);
            (await factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == p.OrderId))).Status.Should().Be(OrderStatus.Delivered);
            (await factory.WithDbAsync(db => db.Shipments.AsNoTracking().SingleAsync(s => s.TrackingNo == tracking))).Status.Should().Be(ShipmentStatus.Delivered);
        }
        finally
        {
            await factory.SetCarrierActiveAsync("GHN_STD", false);
        }
    }

    [Fact]
    public async Task A_carrier_outage_drops_only_that_option_and_checkout_goes_on()
    {
        await factory.SetCarrierActiveAsync("GHN_STD", true);
        Fakes.GhnDown = true;
        try
        {
            var store = await factory.CreateStoreAsync("79", products: [new(Product, "Đèn Bàn", 90_000, 5, "Việt Nam")]);
            var buyer = await factory.CreateUserAsync();
            var addressId = await factory.AddAddressAsync(buyer.Id, "01");
            (await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = store.Skus[Product], quantity = 1 })).EnsureSuccessStatusCode();
            var quote = (await (await buyer.Client.PostAsJsonAsync("/api/checkout/quote", new
            {
                addressId, shops = new[] { new { shopId = store.ShopId, voucherCode = (string?)null, carrierCode = "GHN_STD", note = (string?)null } },
                paymentMethod = "Cod", useCoins = false,
            })).ReadEnvelopeAsync()).Data;
            quote.GetProperty("canPlace").GetBoolean().Should().BeTrue(quote.GetProperty("problems").ToString());
            var options = quote.GetProperty("shops")[0].GetProperty("shippingOptions").EnumerateArray().Select(o => o.Str("code")).ToList();
            options.Should().NotContain("GHN_STD").And.Contain("SIM_FAST");
        }
        finally
        {
            Fakes.GhnDown = false;
            await factory.SetCarrierActiveAsync("GHN_STD", false);
        }
    }

    [Fact]
    public async Task Ghtk_books_by_place_names_serves_its_own_label_and_takes_form_webhooks()
    {
        await factory.SetCarrierActiveAsync("GHTK_STD", true);
        try
        {
            var p = await PlaceAsync("Cod", carrier: "GHTK_STD");
            p.Total.Should().Be(150_000 + 26_000, "phí theo bảng giá GHTK");
            var seller = await SellerAsync(p.Store);
            var prepared = await seller.PostAsJsonAsync($"/api/seller/shops/{p.Store.ShopId}/orders/prepare",
                new { orderIds = new[] { p.OrderId }, pickupMethod = "DropOff", pickupSlot = (string?)null });
            prepared.StatusCode.Should().Be(HttpStatusCode.OK, await prepared.Content.ReadAsStringAsync());
            var label = (await prepared.ReadEnvelopeAsync()).Data[0].Str("trackingNo");
            var order = JsonNode.Parse(Fakes.CallsTo(FakeProviders.GhtkHost, "/services/shipment/order").Last().Body)!["order"]!;
            order["id"]!.GetValue<string>().Should().Be(p.Code);
            order["pick_option"]!.GetValue<string>().Should().Be("post", "shop tự mang ra bưu cục");
            order["pick_money"]!.GetValue<long>().Should().Be(p.Total);
            var province = await factory.WithDbAsync(db => db.AdminDivisions.AsNoTracking().Where(d => d.Code == "01").Select(d => d.Name).SingleAsync());
            order["province"]!.GetValue<string>().Should().Be(province, "GHTK nhận tên tỉnh / quận / phường");

            var pdf = await seller.GetAsync($"/api/seller/shops/{p.Store.ShopId}/orders/{p.OrderId}/carrier-label");
            pdf.StatusCode.Should().Be(HttpStatusCode.OK);
            (await pdf.Content.ReadAsByteArrayAsync()).Take(4).Should().Equal("%PDF"u8.ToArray());

            var client = factory.CreateClient();
            HttpRequestMessage Hook(string token, int status) =>
                new(HttpMethod.Post, $"/api/logistics/webhooks/GHTK?token={token}")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["label_id"] = label, ["partner_id"] = p.Code, ["status_id"] = status.ToString(), ["action_time"] = DateTimeOffset.UtcNow.ToString("O"),
                        ["reason_code"] = "", ["reason"] = "", ["weight"] = "0.3", ["fee"] = "26000",
                    }),
                };
            (await client.SendAsync(Hook("khong-dung", 3))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var picked = await client.SendAsync(Hook(FakeProviders.GhtkWebhookToken, 3));
            picked.StatusCode.Should().Be(HttpStatusCode.OK, await picked.Content.ReadAsStringAsync());
            await Task.Delay(1_100); // a later action_time (second resolution) for the next status
            (await client.SendAsync(Hook(FakeProviders.GhtkWebhookToken, 5))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == p.OrderId))).Status.Should().Be(OrderStatus.Delivered);
        }
        finally
        {
            await factory.SetCarrierActiveAsync("GHTK_STD", false);
        }
    }
}
