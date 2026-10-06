using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Features.Payments;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;
using ShopHub.Domain.SystemConfig;
using ShopHub.Infrastructure.Commerce;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CheckoutTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<(TestUser User, Guid AddressId)> BuyerAsync(string province = "79")
    {
        var user = await factory.CreateUserAsync();
        return (user, await factory.AddAddressAsync(user.Id, province));
    }

    private static async Task<JsonElement> AddAsync(HttpClient client, Guid skuId, int quantity = 1)
    {
        var res = await client.PostAsJsonAsync("/api/cart/items", new { skuId, quantity });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.ReadEnvelopeAsync()).Data;
    }

    private static object Request(Guid addressId, string method = "Cod", string? platform = null, string? freeship = null, bool coins = false,
        params object[] shops) =>
        new { addressId, shops, platformVoucherCode = platform, freeshipVoucherCode = freeship, useCoins = coins, paymentMethod = method };

    private static object Shop(Guid shopId, string? voucher = null, string? carrier = null, string? note = null) =>
        new { shopId, voucherCode = voucher, carrierCode = carrier, note };

    private static async Task<JsonElement> QuoteAsync(HttpClient client, object request)
    {
        var res = await client.PostAsJsonAsync("/api/checkout/quote", request);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.ReadEnvelopeAsync()).Data;
    }

    private static Task<HttpResponseMessage> PlaceAsync(HttpClient client, string key, object request, long expected)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/checkout") { Content = JsonContent.Create(new { checkout = request, expectedGrandTotal = expected }) };
        msg.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(msg);
    }

    private async Task<JsonElement> QuoteAndPlaceAsync(HttpClient client, object request)
    {
        var quote = await QuoteAsync(client, request);
        quote.GetProperty("canPlace").GetBoolean().Should().BeTrue(quote.GetProperty("problems").ToString());
        var res = await PlaceAsync(client, Guid.NewGuid().ToString(), request, quote.GetProperty("grandTotal").GetInt64());
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.ReadEnvelopeAsync()).Data;
    }

    private Task<Sku> SkuAsync(Guid skuId) => factory.WithDbAsync(db => db.Skus.AsNoTracking().SingleAsync(s => s.Id == skuId));

    // ---------- giới hạn mua mỗi người ----------

    [Fact]
    public async Task The_purchase_limit_counts_cart_and_past_orders_and_two_checkouts_at_once_cannot_both_pass()
    {
        var store = await factory.CreateStoreAsync("79", products: [new("Khẩu Trang Giới Hạn", "Đèn Bàn", 50_000, 100, "Việt Nam")]);
        var productId = store.Products["Khẩu Trang Giới Hạn"];
        var sku = store.Skus["Khẩu Trang Giới Hạn"];
        await factory.WithDbAsync(async db =>
        {
            var p = await db.Products.SingleAsync(x => x.Id == productId);
            p.SetPurchaseLimit(2);
            await db.SaveChangesAsync();
        });
        var page = (await (await factory.CreateClient().GetAsync($"/api/products/{productId}")).ReadEnvelopeAsync()).Data;
        page.GetProperty("maxPerBuyer").GetInt32().Should().Be(2);

        var (buyer, address) = await BuyerAsync();
        await AddAsync(buyer.Client, sku, 2);
        var third = await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = sku, quantity = 1 });
        third.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await third.ReadEnvelopeAsync()).Message.Should().Contain("tối đa 2");

        // Two checkouts of the same cart at the same moment (different keys): one order, the other refused
        var request = Request(address, shops: Shop(store.ShopId));
        var quote = await QuoteAsync(buyer.Client, request);
        var total = quote.GetProperty("grandTotal").GetInt64();
        var both = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => PlaceAsync(buyer.Client, Guid.NewGuid().ToString(), request, total))));
        both.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, string.Join(" | ", await Task.WhenAll(both.Select(r => r.Content.ReadAsStringAsync()))));
        (await factory.WithDbAsync(db => db.OrderItems.Where(i => i.ProductId == productId).SumAsync(i => i.Quantity))).Should().Be(2);

        // Already bought 2: one more is refused with what was bought; the quote says so too
        var again = await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = sku, quantity = 1 });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.ReadEnvelopeAsync()).Message.Should().Contain("bạn đã mua 2");

        // A cancelled order gives the allowance back
        var code = await factory.WithDbAsync(db => db.Orders.Where(o => o.BuyerId == buyer.Id).Select(o => o.Code).SingleAsync());
        (await buyer.Client.PostAsJsonAsync($"/api/orders/{code}/cancel", new { reason = "Đặt nhầm số lượng" })).EnsureSuccessStatusCode();
        await AddAsync(buyer.Client, sku, 2);
    }

    // ---------- cart ----------

    [Fact]
    public async Task Guest_cart_is_merged_into_the_account_at_sign_in()
    {
        var store = await factory.CreateStoreAsync("79", products: [new("Áo Thun Gộp Giỏ", "Áo Thun", 120_000, 20, "Việt Nam"), new("Quần Gộp Giỏ", "Quần Jean", 300_000, 20, "Việt Nam")]);
        var user = await factory.CreateUserAsync();
        await AddAsync(user.Client, store.Skus["Áo Thun Gộp Giỏ"], 2);

        var guest = factory.CreateClient(new() { HandleCookies = true });
        await AddAsync(guest, store.Skus["Áo Thun Gộp Giỏ"], 1);
        await AddAsync(guest, store.Skus["Quần Gộp Giỏ"], 1);
        (await guest.PostAsJsonAsync("/api/auth/login", new { identifier = user.Phone, password = ApiFactory.DefaultPassword })).EnsureSuccessStatusCode();
        var cart = (await (await user.Client.GetAsync("/api/cart")).ReadEnvelopeAsync()).Data;
        var guestAfter = (await (await guest.GetAsync("/api/cart")).ReadEnvelopeAsync()).Data;

        var lines = cart.GetProperty("shops").EnumerateArray().SelectMany(s => s.GetProperty("lines").EnumerateArray()).ToList();
        lines.Should().HaveCount(2);
        lines.Single(l => l.Str("skuId") == store.Skus["Áo Thun Gộp Giỏ"].ToString()).GetProperty("quantity").GetInt32().Should().Be(3);
        guestAfter.GetProperty("lineCount").GetInt32().Should().Be(0, "giỏ khách đã được chuyển vào tài khoản");
        (await factory.WithDbAsync(db => db.Carts.CountAsync(c => c.GuestToken != null && c.Items.Any(i => i.SkuId == store.Skus["Quần Gộp Giỏ"]))))
            .Should().Be(0);
    }

    [Fact]
    public async Task Cart_reports_price_changes_and_shortages_per_line_instead_of_dropping_them()
    {
        var store = await factory.CreateStoreAsync(products: [new("Ấm Siêu Tốc", "Nồi & Chảo", 250_000, 5, "Việt Nam")]);
        var (buyer, _) = await BuyerAsync();
        var sku = store.Skus["Ấm Siêu Tốc"];
        await AddAsync(buyer.Client, sku, 3);
        await factory.WithDbAsync(db => db.Skus.Where(s => s.Id == sku).ExecuteUpdateAsync(u => u.SetProperty(s => s.Price, 199_000).SetProperty(s => s.Stock, 2)));

        var line = (await (await buyer.Client.GetAsync("/api/cart")).ReadEnvelopeAsync()).Data.GetProperty("shops")[0].GetProperty("lines")[0];
        var tooMany = await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = sku, quantity = 5 });

        line.GetProperty("price").GetInt64().Should().Be(199_000);
        line.GetProperty("previousPrice").GetInt64().Should().Be(250_000, "giá cũ hiện gạch ngang");
        line.GetProperty("canBuy").GetBoolean().Should().BeFalse();
        line.Str("problem").Should().Be("Chỉ còn 2 sản phẩm, vui lòng giảm số lượng.");
        tooMany.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---------- pricing end to end ----------

    [Fact]
    public async Task Two_shop_checkout_with_every_discount_creates_two_orders_that_add_up_exactly()
    {
        var a = await factory.CreateStoreAsync("79", products: [new("Giày Chạy Bộ", "Giày Chạy Bộ", 333_333, 10, "Việt Nam"), new("Tất Cổ Ngắn", "Sneaker", 49_999, 10, "Việt Nam")]);
        var b = await factory.CreateStoreAsync("01", products: [new("Sổ Tay Bìa Da", "Balo & Túi Laptop", 150_001, 10, "Việt Nam")]);
        var shopVoucher = await factory.CreateVoucherAsync(VoucherOwner.Shop, a.ShopId, VoucherType.Percent, percentBp: 1000, max: 30_000);
        var platform = await factory.CreateVoucherAsync(VoucherOwner.Platform, null, VoucherType.Amount, value: 50_000, minOrder: 250_000, quota: 100);
        var freeship = await factory.CreateVoucherAsync(VoucherOwner.Platform, null, VoucherType.FreeShipping, max: 25_000);
        var (buyer, addressId) = await BuyerAsync("79");
        await factory.GrantCoinsAsync(buyer.Id, 12_345);
        await AddAsync(buyer.Client, a.Skus["Giày Chạy Bộ"], 1);
        await AddAsync(buyer.Client, a.Skus["Tất Cổ Ngắn"], 3);
        await AddAsync(buyer.Client, b.Skus["Sổ Tay Bìa Da"], 2);
        var request = Request(addressId, "Cod", platform.Code, freeship.Code, true, Shop(a.ShopId, shopVoucher.Code), Shop(b.ShopId, note: "Gói quà giúp em"));

        var quote = await QuoteAsync(buyer.Client, request);
        var placed = await QuoteAndPlaceAsync(buyer.Client, request);

        var orders = await factory.WithDbAsync(db => db.Orders.AsNoTracking().Include(o => o.Items).ThenInclude(i => i.Discounts)
            .Where(o => o.CheckoutId == Guid.Parse(placed.Str("checkoutId"))).ToListAsync());
        orders.Should().HaveCount(2, "mỗi shop một đơn");
        orders.Sum(o => o.GrandTotal).Should().Be(quote.GetProperty("grandTotal").GetInt64());
        placed.GetProperty("grandTotal").GetInt64().Should().Be(quote.GetProperty("grandTotal").GetInt64());
        orders.Should().OnlyContain(o => o.Status == OrderStatus.PendingConfirmation && o.PaymentStatus == OrderPaymentStatus.Unpaid);
        var shopA = orders.Single(o => o.ShopId == a.ShopId);
        shopA.ShopDiscount.Should().Be(30_000, "10% của 483.330 vượt trần 30.000");
        shopA.ShopVoucherId.Should().Be(shopVoucher.Id);
        orders.Sum(o => o.PlatformDiscount).Should().Be(50_000);
        orders.Sum(o => o.CoinUsed).Should().Be(12_345);
        orders.Sum(o => o.ShippingDiscount).Should().Be(25_000);
        orders.Single(o => o.ShopId == b.ShopId).BuyerNote.Should().Be("Gói quà giúp em");
        foreach (var o in orders)
        {
            o.Items.SelectMany(i => i.Discounts).Where(d => d.Source == DiscountSource.Shop).Sum(d => d.Amount).Should().Be(o.ShopDiscount);
            o.Items.SelectMany(i => i.Discounts).Where(d => d.Source == DiscountSource.Platform).Sum(d => d.Amount).Should().Be(o.PlatformDiscount);
            o.Items.SelectMany(i => i.Discounts).Where(d => d.Source == DiscountSource.Coin).Sum(d => d.Amount).Should().Be(o.CoinUsed);
            o.GrandTotal.Should().Be(o.Items.Sum(i => i.PaidAmount) + o.ShippingFee - o.ShippingDiscount);
        }

        (await SkuAsync(a.Skus["Tất Cổ Ngắn"])).Reserved.Should().Be(3);
        (await factory.WithDbAsync(db => db.Vouchers.Where(v => v.Id == platform.Id).Select(v => v.UsedCount).SingleAsync())).Should().Be(1);
        (await factory.WithDbAsync(db => db.CoinLedger.Where(c => c.UserId == buyer.Id).SumAsync(c => c.Delta))).Should().Be(0);
        (await (await buyer.Client.GetAsync("/api/cart")).ReadEnvelopeAsync()).Data.GetProperty("lineCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task A_price_change_between_quote_and_order_is_a_409_with_the_new_quote()
    {
        var store = await factory.CreateStoreAsync(products: [new("Đèn Bàn Học", "Đèn Bàn", 180_000, 5, "Việt Nam")]);
        var (buyer, addressId) = await BuyerAsync();
        await AddAsync(buyer.Client, store.Skus["Đèn Bàn Học"]);
        var request = Request(addressId, shops: Shop(store.ShopId));
        var quote = await QuoteAsync(buyer.Client, request);
        await factory.WithDbAsync(db => db.Skus.Where(s => s.Id == store.Skus["Đèn Bàn Học"]).ExecuteUpdateAsync(u => u.SetProperty(s => s.Price, 210_000).SetProperty(s => s.OriginalPrice, 250_000)));

        var res = await PlaceAsync(buyer.Client, Guid.NewGuid().ToString(), request, quote.GetProperty("grandTotal").GetInt64());
        var body = await res.ReadEnvelopeAsync();

        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        body.Message.Should().Contain("Giá hoặc ưu đãi vừa thay đổi");
        body.Data.GetProperty("grandTotal").GetInt64().Should().Be(quote.GetProperty("grandTotal").GetInt64() + 30_000);
        (await factory.WithDbAsync(db => db.Orders.CountAsync(o => o.BuyerId == buyer.Id))).Should().Be(0);
    }

    [Fact]
    public async Task Unusable_vouchers_are_listed_with_the_reason()
    {
        var store = await factory.CreateStoreAsync(products: [new("Cốc Sứ", "Bình Giữ Nhiệt", 60_000, 5, "Việt Nam")]);
        var bigOrder = await factory.CreateVoucherAsync(VoucherOwner.Platform, null, VoucherType.Amount, value: 20_000, minOrder: 95_000);
        var (buyer, addressId) = await BuyerAsync();
        await AddAsync(buyer.Client, store.Skus["Cốc Sứ"]);

        var quote = await QuoteAsync(buyer.Client, Request(addressId, shops: Shop(store.ShopId)));

        var option = quote.GetProperty("platformVouchers").EnumerateArray().Single(v => v.Str("code") == bigOrder.Code);
        option.GetProperty("usable").GetBoolean().Should().BeFalse();
        option.Str("problem").Should().Be("Mua thêm ₫35.000 để dùng mã này.");
    }

    // ---------- concurrency (spec 6.2) ----------

    [Fact]
    public async Task Fifty_parallel_buyers_for_ten_units_get_exactly_ten_orders()
    {
        var store = await factory.CreateStoreAsync(products: [new("Vé Concert Giới Hạn", "Lego & Xếp Hình", 99_000, 10, "Việt Nam")]);
        var sku = store.Skus["Vé Concert Giới Hạn"];
        var buyers = await Task.WhenAll(Enumerable.Range(0, 50).Select(async _ =>
        {
            var (user, addressId) = await BuyerAsync();
            await AddAsync(user.Client, sku);
            var request = Request(addressId, shops: Shop(store.ShopId));
            var quote = await QuoteAsync(user.Client, request);
            return (user, request, total: quote.GetProperty("grandTotal").GetInt64());
        }));

        var results = await Task.WhenAll(buyers.Select(b => PlaceAsync(b.user.Client, Guid.NewGuid().ToString(), b.request, b.total)));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(10);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(40);
        var after = await SkuAsync(sku);
        after.Reserved.Should().Be(10);
        after.Available.Should().Be(0);
        (await factory.WithDbAsync(db => db.OrderItems.CountAsync(i => i.SkuId == sku))).Should().Be(10);
    }

    [Fact]
    public async Task The_same_idempotency_key_in_parallel_creates_one_checkout()
    {
        var store = await factory.CreateStoreAsync(products: [new("Bút Bi Xanh", "Thẻ Nhớ", 5_000, 100, "Việt Nam")]);
        var (buyer, addressId) = await BuyerAsync();
        await AddAsync(buyer.Client, store.Skus["Bút Bi Xanh"], 4);
        var request = Request(addressId, shops: Shop(store.ShopId));
        var total = (await QuoteAsync(buyer.Client, request)).GetProperty("grandTotal").GetInt64();
        var key = Guid.NewGuid().ToString();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PlaceAsync(buyer.Client, key, request, total)));
        var ids = new List<string>();
        foreach (var r in results)
        {
            r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
            ids.Add((await r.ReadEnvelopeAsync()).Data.Str("checkoutId"));
        }

        ids.Distinct().Should().HaveCount(1, "bấm hai lần không sinh hai đơn");
        (await factory.WithDbAsync(db => db.CheckoutSessions.CountAsync(c => c.UserId == buyer.Id))).Should().Be(1);
        (await SkuAsync(store.Skus["Bút Bi Xanh"])).Reserved.Should().Be(4);
    }

    [Fact]
    public async Task Voucher_quota_and_per_user_limit_hold_under_parallel_checkouts()
    {
        var store = await factory.CreateStoreAsync(products: [new("Khăn Tắm Cotton", "Chăn Mền", 80_000, 200, "Việt Nam")]);
        var quota3 = await factory.CreateVoucherAsync(VoucherOwner.Platform, null, VoucherType.Amount, value: 10_000, quota: 3, perUser: 5);
        var once = await factory.CreateVoucherAsync(VoucherOwner.Platform, null, VoucherType.Amount, value: 10_000, perUser: 1);

        async Task<(TestUser, object, long)> Prepare(TestUser? user, string code)
        {
            Guid addressId;
            if (user is null) (user, addressId) = await BuyerAsync();
            else addressId = await factory.AddAddressAsync(user.Id);
            await AddAsync(user.Client, store.Skus["Khăn Tắm Cotton"]);
            var request = Request(addressId, platform: code, shops: Shop(store.ShopId));
            return (user, request, (await QuoteAsync(user.Client, request)).GetProperty("grandTotal").GetInt64());
        }

        var many = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Prepare(null, quota3.Code)));
        var quotaResults = await Task.WhenAll(many.Select(m => PlaceAsync(m.Item1.Client, Guid.NewGuid().ToString(), m.Item2, m.Item3)));
        var solo = await factory.CreateUserAsync();
        var soloRuns = new List<(TestUser, object, long)>();
        for (var i = 0; i < 6; i++) soloRuns.Add(await Prepare(solo, once.Code));
        var limitResults = await Task.WhenAll(soloRuns.Select(m => PlaceAsync(m.Item1.Client, Guid.NewGuid().ToString(), m.Item2, m.Item3)));

        quotaResults.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(3);
        (await factory.WithDbAsync(db => db.Vouchers.Where(v => v.Id == quota3.Id).Select(v => v.UsedCount).SingleAsync())).Should().Be(3);
        limitResults.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, "mỗi người chỉ một lượt");
        (await factory.WithDbAsync(db => db.VoucherUsages.CountAsync(u => u.VoucherId == once.Id))).Should().Be(1);
    }

    // ---------- online payment ----------

    [Fact]
    public async Task Paying_on_the_simulated_gateway_moves_the_orders_to_pending_confirmation()
    {
        var store = await factory.CreateStoreAsync(products: [new("Bàn Phím Cơ", "Bàn Phím", 890_000, 5, "Trung Quốc")]);
        var (buyer, addressId) = await BuyerAsync();
        await AddAsync(buyer.Client, store.Skus["Bàn Phím Cơ"]);

        var placed = await QuoteAndPlaceAsync(buyer.Client, Request(addressId, "Simulated", shops: Shop(store.ShopId)));
        var payment = placed.GetProperty("payment");
        payment.Str("redirectUrl").Should().StartWith("/cong-thanh-toan/");
        placed.GetProperty("orders")[0].Str("status").Should().Be("PendingPayment");
        var page = (await (await factory.CreateClient().GetAsync($"/api/payments/simulated/{payment.Str("paymentId")}")).ReadEnvelopeAsync()).Data;
        (await factory.CreateClient().PostAsync($"/api/payments/simulated/{payment.Str("paymentId")}/success", null)).EnsureSuccessStatusCode();
        var after = (await (await buyer.Client.GetAsync($"/api/checkout/{placed.Str("checkoutId")}")).ReadEnvelopeAsync()).Data;

        page.GetProperty("amount").GetInt64().Should().Be(placed.GetProperty("grandTotal").GetInt64());
        after.Str("status").Should().Be("Placed");
        after.GetProperty("orders")[0].Str("status").Should().Be("PendingConfirmation");
        after.GetProperty("payment").Str("status").Should().Be("Succeeded");
        (await SkuAsync(store.Skus["Bàn Phím Cơ"])).Reserved.Should().Be(1, "đã trả tiền thì vẫn giữ hàng tới khi giao");
    }

    private async Task<(JsonElement Placed, Guid PaymentId)> PlaceOnlineAsync(TestStore store, string product, string? voucher = null, long coins = 0)
    {
        var (buyer, addressId) = await BuyerAsync();
        if (coins > 0) await factory.GrantCoinsAsync(buyer.Id, coins);
        await AddAsync(buyer.Client, store.Skus[product]);
        var placed = await QuoteAndPlaceAsync(buyer.Client, Request(addressId, "Simulated", voucher, null, coins > 0, Shop(store.ShopId)));
        return (placed, Guid.Parse(placed.GetProperty("payment").Str("paymentId")));
    }

    private string SignedCallback(Guid paymentId, long amount, string eventId, out string signature, string status = "SUCCESS")
    {
        var body = JsonSerializer.Serialize(new SimulatedGateway.CallbackBody(eventId, paymentId, $"TX{eventId}", amount, status, null), Json);
        using var scope = factory.Services.CreateScope();
        signature = scope.ServiceProvider.GetRequiredService<SimulatedGateway>().Sign(body);
        return body;
    }

    private Task<HttpResponseMessage> PostWebhookAsync(string body, string signature)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhooks/simulated") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        msg.Headers.Add(SimulatedGateway.SignatureHeader, signature);
        return factory.CreateClient().SendAsync(msg);
    }

    [Fact]
    public async Task A_webhook_is_applied_exactly_once_even_when_delivered_in_parallel()
    {
        var store = await factory.CreateStoreAsync(products: [new("Chuột Không Dây", "Chuột", 250_000, 5, "Trung Quốc")]);
        var (placed, paymentId) = await PlaceOnlineAsync(store, "Chuột Không Dây");
        var eventId = Guid.NewGuid().ToString("N");
        var body = SignedCallback(paymentId, placed.GetProperty("grandTotal").GetInt64(), eventId, out var signature);

        var replies = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PostWebhookAsync(body, signature)));

        replies.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var orderId = Guid.Parse(placed.GetProperty("orders")[0].Str("id"));
        (await factory.WithDbAsync(db => db.OrderStatusHistory.CountAsync(h => h.OrderId == orderId && h.ToStatus == OrderStatus.PendingConfirmation)))
            .Should().Be(1);
        (await factory.WithDbAsync(db => db.PaymentWebhookEvents.CountAsync(e => e.EventId == eventId))).Should().Be(1);
    }

    [Fact]
    public async Task A_forged_or_wrong_amount_webhook_changes_nothing()
    {
        var store = await factory.CreateStoreAsync(products: [new("Loa Mini Kẹp", "Loa Bluetooth", 150_000, 5, "Trung Quốc")]);
        var (placed, paymentId) = await PlaceOnlineAsync(store, "Loa Mini Kẹp");
        var amount = placed.GetProperty("grandTotal").GetInt64();
        var good = SignedCallback(paymentId, amount, Guid.NewGuid().ToString("N"), out var signature);
        var cheap = SignedCallback(paymentId, 1_000, Guid.NewGuid().ToString("N"), out var cheapSignature);

        var forged = await PostWebhookAsync(good, new string('0', 64));
        var wrongAmount = await PostWebhookAsync(cheap, cheapSignature);

        forged.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        wrongAmount.StatusCode.Should().Be(HttpStatusCode.OK, "đã nhận thông báo nhưng không ghi nhận là đã trả");
        (await factory.WithDbAsync(db => db.Payments.Where(p => p.Id == paymentId).Select(p => p.Status).SingleAsync())).Should().Be(PaymentStatus.Initiated);
        _ = signature;
    }

    [Fact]
    public async Task Failed_then_abandoned_payment_gives_back_stock_voucher_and_coins_when_it_expires()
    {
        var store = await factory.CreateStoreAsync(products: [new("Nồi Cơm Điện", "Nồi & Chảo", 900_000, 3, "Nhật Bản")]);
        var voucher = await factory.CreateVoucherAsync(VoucherOwner.Platform, null, VoucherType.Amount, value: 40_000, quota: 10);
        var (placed, paymentId) = await PlaceOnlineAsync(store, "Nồi Cơm Điện", voucher.Code, coins: 20_000);
        var checkoutId = Guid.Parse(placed.Str("checkoutId"));
        var userId = await factory.WithDbAsync(db => db.CheckoutSessions.Where(c => c.Id == checkoutId).Select(c => c.UserId).SingleAsync());
        (await factory.CreateClient().PostAsync($"/api/payments/simulated/{paymentId}/fail", null)).EnsureSuccessStatusCode();
        var whileFailed = await SkuAsync(store.Skus["Nồi Cơm Điện"]);

        await factory.WithDbAsync(db => db.CheckoutSessions.Where(c => c.Id == checkoutId)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.PaymentExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        using (var scope = factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<PaymentExpiryService>().RunAsync(CancellationToken.None)).Should().BeGreaterThan(0);

        whileFailed.Reserved.Should().Be(1, "thất bại vẫn cho thanh toán lại tới khi hết hạn");
        var order = await factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.CheckoutId == checkoutId));
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancelReason.Should().Be("Quá hạn thanh toán");
        (await SkuAsync(store.Skus["Nồi Cơm Điện"])).Reserved.Should().Be(0);
        (await factory.WithDbAsync(db => db.Vouchers.Where(v => v.Id == voucher.Id).Select(v => v.UsedCount).SingleAsync())).Should().Be(0);
        (await factory.WithDbAsync(db => db.CoinLedger.Where(c => c.UserId == userId).SumAsync(c => c.Delta))).Should().Be(20_000);
        (await factory.WithDbAsync(db => db.Payments.Where(p => p.Id == paymentId).Select(p => p.Status).SingleAsync())).Should().Be(PaymentStatus.Expired);
    }

    [Fact]
    public async Task A_lost_callback_is_found_by_asking_the_gateway_before_expiring()
    {
        var store = await factory.CreateStoreAsync(products: [new("Máy Sấy Tóc", "Máy Xay", 400_000, 3, "Trung Quốc")]);
        var (placed, paymentId) = await PlaceOnlineAsync(store, "Máy Sấy Tóc");
        var checkoutId = Guid.Parse(placed.Str("checkoutId"));
        await factory.WithDbAsync(async db =>
        {
            // The gateway recorded the payment but its notification never arrived
            db.SimulatedPayments.Add(new SimulatedPayment(paymentId, "SIMLOST1", placed.GetProperty("grandTotal").GetInt64(), SimulatedPaymentOutcome.Succeeded,
                DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            await db.CheckoutSessions.Where(c => c.Id == checkoutId).ExecuteUpdateAsync(u => u.SetProperty(c => c.PaymentExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        });

        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PaymentExpiryService>().RunAsync(CancellationToken.None);

        (await factory.WithDbAsync(db => db.CheckoutSessions.Where(c => c.Id == checkoutId).Select(c => c.Status).SingleAsync())).Should().Be(CheckoutStatus.Placed);
        (await factory.WithDbAsync(db => db.Orders.Where(o => o.CheckoutId == checkoutId).Select(o => o.Status).SingleAsync()))
            .Should().Be(OrderStatus.PendingConfirmation);
    }

    [Fact]
    public async Task Money_arriving_after_expiry_is_refunded_and_the_order_stays_cancelled()
    {
        var store = await factory.CreateStoreAsync(products: [new("Bình Đun Siêu Tốc", "Nồi & Chảo", 300_000, 3, "Việt Nam")]);
        var (placed, paymentId) = await PlaceOnlineAsync(store, "Bình Đun Siêu Tốc");
        var checkoutId = Guid.Parse(placed.Str("checkoutId"));
        var amount = placed.GetProperty("grandTotal").GetInt64();
        await factory.WithDbAsync(db => db.CheckoutSessions.Where(c => c.Id == checkoutId)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.PaymentExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PaymentExpiryService>().RunAsync(CancellationToken.None);
        await factory.WithDbAsync(async db =>
        {
            db.SimulatedPayments.Add(new SimulatedPayment(paymentId, "SIMLATE1", amount, SimulatedPaymentOutcome.Succeeded, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });

        var eventId = Guid.NewGuid().ToString("N");
        var body = SignedCallback(paymentId, amount, eventId, out var signature);
        (await PostWebhookAsync(body, signature)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await factory.WithDbAsync(db => db.Payments.Where(p => p.Id == paymentId).Select(p => p.Status).SingleAsync())).Should().Be(PaymentStatus.Refunded);
        (await factory.WithDbAsync(db => db.Orders.Where(o => o.CheckoutId == checkoutId).Select(o => o.Status).SingleAsync())).Should().Be(OrderStatus.Cancelled);
        (await factory.WithDbAsync(db => db.PaymentWebhookEvents.Where(e => e.EventId == eventId).Select(e => e.Result).SingleAsync()))
            .Should().Be(WebhookResults.LateRefunded);
    }

    // ---------- ownership ----------

    [Fact]
    public async Task Another_buyers_order_and_checkout_are_not_found()
    {
        var store = await factory.CreateStoreAsync(products: [new("Gương Trang Điểm", "Đèn Ngủ", 120_000, 5, "Việt Nam")]);
        var (owner, addressId) = await BuyerAsync();
        await AddAsync(owner.Client, store.Skus["Gương Trang Điểm"]);
        var placed = await QuoteAndPlaceAsync(owner.Client, Request(addressId, shops: Shop(store.ShopId)));
        var stranger = await factory.CreateUserAsync();

        (await stranger.Client.GetAsync($"/api/orders/{placed.GetProperty("orders")[0].Str("code")}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.Client.GetAsync($"/api/checkout/{placed.Str("checkoutId")}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.Client.PostAsJsonAsync("/api/checkout/quote", Request(addressId, shops: Shop(store.ShopId)))).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "địa chỉ của người khác");
        (await owner.Client.GetAsync($"/api/orders/{placed.GetProperty("orders")[0].Str("code")}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Express_shipping_is_only_offered_within_the_same_province()
    {
        var store = await factory.CreateStoreAsync("79", products: [new("Hộp Cơm Giữ Nhiệt", "Bình Giữ Nhiệt", 150_000, 5, "Việt Nam", WeightG: 800)]);
        var (near, nearAddress) = await BuyerAsync("79");
        var (far, farAddress) = await BuyerAsync("01");
        await AddAsync(near.Client, store.Skus["Hộp Cơm Giữ Nhiệt"]);
        await AddAsync(far.Client, store.Skus["Hộp Cơm Giữ Nhiệt"]);

        var nearQuote = await QuoteAsync(near.Client, Request(nearAddress, shops: Shop(store.ShopId, carrier: "SIM_EXPRESS")));
        var farQuote = await QuoteAsync(far.Client, Request(farAddress, shops: Shop(store.ShopId)));

        string[] Codes(JsonElement q) => q.GetProperty("shops")[0].GetProperty("shippingOptions").EnumerateArray().Select(o => o.Str("code")).ToArray();
        Codes(nearQuote).Should().Contain("SIM_EXPRESS");
        Codes(farQuote).Should().NotContain("SIM_EXPRESS");
        nearQuote.GetProperty("shippingFee").GetInt64().Should().Be(35_000, "hoả tốc nội tỉnh dưới 2 kg");
        var eco = farQuote.GetProperty("shops")[0].GetProperty("shippingOptions").EnumerateArray().Single(o => o.Str("code") == "SIM_ECO");
        eco.GetProperty("fee").GetInt64().Should().Be(30_000, "tiết kiệm liên miền 500 g–1 kg");
    }
}
