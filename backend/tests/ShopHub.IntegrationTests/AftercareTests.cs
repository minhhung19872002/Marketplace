using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Returns;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;
using ShopHub.Infrastructure.Commerce;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AftercareTests(ApiFactory factory)
{
    private sealed record Bought(TestUser Buyer, TestStore Store, HttpClient Seller, Guid OrderId, string Code, Guid ItemId, Guid SkuId, Guid ProductId);

    private async Task<HttpClient> StaffAsync(TestStore store)
    {
        var staff = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(store.ShopId, staff.Id, Domain.Shops.ShopStaffRole.Manager, ShopPermissions.All));
            await db.SaveChangesAsync();
        });
        return staff.Client;
    }

    private async Task SetParameterAsync(string key, string value)
    {
        await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == key).ExecuteUpdateAsync(u => u.SetProperty(p => p.Value, value)));
        factory.Services.GetRequiredService<ISystemParameters>().Invalidate(key);
    }

    private async Task CarrierStepsAsync(int times)
    {
        await SetParameterAsync(ParameterKeys.LogisticsSimStepSeconds, "0");
        try
        {
            for (var i = 0; i < times; i++)
            {
                using var scope = factory.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CarrierSimulator>().RunAsync(CancellationToken.None);
            }
        }
        finally
        {
            await SetParameterAsync(ParameterKeys.LogisticsSimStepSeconds, "120");
        }
    }

    /// <summary>Buy <paramref name="quantity"/> units, ship and deliver them; optionally complete the order.</summary>
    private async Task<Bought> BuyAsync(int quantity = 3, string method = "Cod", bool complete = true, string? shopVoucher = null, string? platformVoucher = null,
        long coins = 0, TestStore? store = null)
    {
        store ??= await factory.CreateStoreAsync("79", products: [new("Ly Sứ Cao Cấp", "Bình Giữ Nhiệt", 99_999, 20, "Việt Nam")]);
        var buyer = await factory.CreateUserAsync();
        var addressId = await factory.AddAddressAsync(buyer.Id, "01");
        if (coins > 0) await factory.GrantCoinsAsync(buyer.Id, coins);
        var sku = store.Skus["Ly Sứ Cao Cấp"];
        (await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = sku, quantity })).EnsureSuccessStatusCode();
        var request = new
        {
            addressId,
            shops = new[] { new { shopId = store.ShopId, voucherCode = shopVoucher, carrierCode = (string?)null, note = (string?)null } },
            platformVoucherCode = platformVoucher, freeshipVoucherCode = (string?)null, useCoins = coins > 0, paymentMethod = method,
        };
        var quote = (await (await buyer.Client.PostAsJsonAsync("/api/checkout/quote", request)).ReadEnvelopeAsync()).Data;
        quote.GetProperty("canPlace").GetBoolean().Should().BeTrue(quote.GetProperty("problems").ToString());
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { checkout = request, expectedGrandTotal = quote.GetProperty("grandTotal").GetInt64() }),
        };
        msg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var placed = (await (await buyer.Client.SendAsync(msg)).ReadEnvelopeAsync()).Data;
        if (method == "Simulated")
            (await factory.CreateClient().PostAsync($"/api/payments/simulated/{placed.GetProperty("payment").Str("paymentId")}/success", null)).EnsureSuccessStatusCode();
        var order = placed.GetProperty("orders")[0];
        var orderId = Guid.Parse(order.Str("id"));
        var seller = await StaffAsync(store);
        (await seller.PostAsJsonAsync($"/api/seller/shops/{store.ShopId}/orders/prepare",
            new { orderIds = new[] { orderId }, pickupMethod = "DropOff", pickupSlot = (string?)null })).EnsureSuccessStatusCode();
        await CarrierStepsAsync(4);
        if (complete) (await buyer.Client.PostAsync($"/api/orders/{order.Str("code")}/received", null)).EnsureSuccessStatusCode();
        var itemId = await factory.WithDbAsync(db => db.OrderItems.Where(i => i.OrderId == orderId).Select(i => i.Id).SingleAsync());
        return new Bought(buyer, store, seller, orderId, order.Str("code"), itemId, sku, store.Products["Ly Sứ Cao Cấp"]);
    }

    private async Task<Guid> UploadAsync(HttpClient client, string purpose)
    {
        using var form = new MultipartFormDataContent();
        using var bmp = new SkiaSharp.SKBitmap(400, 300);
        using (var canvas = new SkiaSharp.SKCanvas(bmp)) canvas.Clear(new SkiaSharp.SKColor(200, 80, 60));
        using var encoded = bmp.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        var png = new ByteArrayContent(encoded.ToArray());
        png.Headers.ContentType = new("image/png");
        form.Add(png, "file", "sample.png");
        var res = await client.PostAsync($"/api/media/{purpose}", form);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return Guid.Parse((await res.ReadEnvelopeAsync()).Data.Str("id"));
    }

    private static Task<HttpResponseMessage> ReturnAsync(Bought b, int quantity, Guid evidence, string type = "RefundOnly") =>
        b.Buyer.Client.PostAsJsonAsync($"/api/orders/{b.Code}/returns", new
        {
            type, reason = "Damaged", description = "Ly bị nứt khi mở hộp, có ảnh đính kèm",
            lines = new[] { new { orderItemId = b.ItemId, quantity } }, evidenceAssetIds = new[] { evidence },
        });

    // ---------- reviews ----------

    [Fact]
    public async Task A_completed_order_can_be_reviewed_once_with_a_coin_reward_and_ratings_follow()
    {
        var b = await BuyAsync(quantity: 1);
        var photo = await UploadAsync(b.Buyer.Client, "review");
        var text = new string('t', 60);

        var first = await b.Buyer.Client.PostAsJsonAsync($"/api/orders/{b.Code}/items/{b.ItemId}/review",
            new { rating = 4, content = text, tags = new[] { "Đúng mô tả", "Giao hàng nhanh" }, anonymous = false, mediaAssetIds = new[] { photo } });
        var again = await b.Buyer.Client.PostAsJsonAsync($"/api/orders/{b.Code}/items/{b.ItemId}/review",
            new { rating = 5, content = "lần hai", anonymous = false });
        var reviewId = Guid.Parse((await first.ReadEnvelopeAsync()).Data.GetString()!);
        var edit = await b.Buyer.Client.PutAsJsonAsync($"/api/reviews/{reviewId}", new { rating = 2, content = text, anonymous = true, mediaAssetIds = new[] { photo } });
        var editAgain = await b.Buyer.Client.PutAsJsonAsync($"/api/reviews/{reviewId}", new { rating = 5, content = text, anonymous = true });
        var reply = await b.Seller.PostAsJsonAsync($"/api/seller/shops/{b.Store.ShopId}/reviews/{reviewId}/reply", new { text = "Cảm ơn bạn đã ủng hộ shop!" });
        var replyAgain = await b.Seller.PostAsJsonAsync($"/api/seller/shops/{b.Store.ShopId}/reviews/{reviewId}/reply", new { text = "Lần hai" });
        var page = (await (await factory.CreateClient().GetAsync($"/api/products/{b.ProductId}/reviews?withMedia=true")).ReadEnvelopeAsync()).Data;

        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        again.StatusCode.Should().Be(HttpStatusCode.Conflict, "mỗi dòng đơn một đánh giá");
        edit.StatusCode.Should().Be(HttpStatusCode.OK);
        editAgain.StatusCode.Should().Be(HttpStatusCode.Conflict, "chỉ sửa một lần");
        reply.StatusCode.Should().Be(HttpStatusCode.OK);
        replyAgain.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await factory.WithDbAsync(db => db.CoinLedger.Where(c => c.UserId == b.Buyer.Id).SumAsync(c => c.Delta))).Should().Be(100, "đủ chữ + có ảnh");
        var product = await factory.WithDbAsync(db => db.Products.AsNoTracking().SingleAsync(p => p.Id == b.ProductId));
        product.RatingCount.Should().Be(1);
        product.RatingAvg.Should().Be(2);
        (await factory.WithDbAsync(db => db.Shops.Where(s => s.Id == b.Store.ShopId).Select(s => s.RatingAvg).SingleAsync())).Should().Be(2);
        var shown = page.GetProperty("reviews").GetProperty("items")[0];
        shown.Str("reviewerName").Should().Be("Người mua ẩn danh");
        shown.Str("sellerReply").Should().Be("Cảm ơn bạn đã ủng hộ shop!");
        shown.GetProperty("media").GetArrayLength().Should().Be(1);
        page.GetProperty("summary").GetProperty("byStar").GetProperty("2").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Only_completed_orders_can_be_reviewed_and_reported_reviews_can_be_hidden()
    {
        var delivered = await BuyAsync(quantity: 1, complete: false);
        var early = await delivered.Buyer.Client.PostAsJsonAsync($"/api/orders/{delivered.Code}/items/{delivered.ItemId}/review", new { rating = 5, anonymous = false });
        early.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var b = await BuyAsync(quantity: 1);
        var write = await b.Buyer.Client.PostAsJsonAsync($"/api/orders/{b.Code}/items/{b.ItemId}/review", new { rating = 1, content = "Nội dung vi phạm", anonymous = false });
        var reviewId = Guid.Parse((await write.ReadEnvelopeAsync()).Data.GetString()!);
        var reporter = await factory.CreateUserAsync();
        (await reporter.Client.PostAsJsonAsync($"/api/reviews/{reviewId}/report", new { reason = "Ngôn từ không phù hợp" })).EnsureSuccessStatusCode();
        var moderator = await factory.ClientWithPermissionsAsync(Permissions.ReviewModerate);
        var reports = (await (await moderator.GetAsync("/api/admin/review-reports")).ReadEnvelopeAsync()).Data;
        var reportId = reports.GetProperty("items").EnumerateArray().First(r => r.Str("reviewId") == reviewId.ToString()).Str("id");
        (await moderator.PostAsJsonAsync($"/api/admin/review-reports/{reportId}/resolve", new { hide = true, reason = "Vi phạm tiêu chuẩn cộng đồng" })).EnsureSuccessStatusCode();

        (await factory.WithDbAsync(db => db.Products.Where(p => p.Id == b.ProductId).Select(p => p.RatingCount).SingleAsync())).Should().Be(0);
        var page = (await (await factory.CreateClient().GetAsync($"/api/products/{b.ProductId}/reviews")).ReadEnvelopeAsync()).Data;
        page.GetProperty("summary").GetProperty("total").GetInt32().Should().Be(0);
    }

    // ---------- returns ----------

    [Fact]
    public async Task Partial_returns_refund_exactly_what_was_paid_after_every_discount()
    {
        var store = await factory.CreateStoreAsync("79", products: [new("Ly Sứ Cao Cấp", "Bình Giữ Nhiệt", 99_999, 20, "Việt Nam")]);
        var shopVoucher = await factory.CreateVoucherAsync(VoucherOwner.Shop, store.ShopId, VoucherType.Amount, value: 10_001);
        var platform = await factory.CreateVoucherAsync(VoucherOwner.Platform, null, VoucherType.Percent, percentBp: 1000, max: 1_000_000);
        var b = await BuyAsync(3, "Simulated", shopVoucher: shopVoucher.Code, platformVoucher: platform.Code, coins: 7_777, store: store);
        var line = await factory.WithDbAsync(db => db.OrderItems.AsNoTracking().Include(i => i.Discounts).SingleAsync(i => i.Id == b.ItemId));
        var paidMoney = line.LineTotal - line.Discounts.Sum(d => d.Amount);
        var paidCoins = line.Discounts.Where(d => d.Source == DiscountSource.Coin).Sum(d => d.Amount);
        var evidence = await UploadAsync(b.Buyer.Client, "evidence");

        var one = await ReturnAsync(b, 1, evidence);
        var oneId = (await one.ReadEnvelopeAsync()).Data.Str("id");
        (await b.Seller.PostAsJsonAsync($"/api/seller/shops/{b.Store.ShopId}/returns/{oneId}/actions", new { action = "Approve" })).EnsureSuccessStatusCode();
        var evidence2 = await UploadAsync(b.Buyer.Client, "evidence");
        var two = await ReturnAsync(b, 2, evidence2);
        var twoId = (await two.ReadEnvelopeAsync()).Data.Str("id");
        (await b.Seller.PostAsJsonAsync($"/api/seller/shops/{b.Store.ShopId}/returns/{twoId}/actions", new { action = "Approve" })).EnsureSuccessStatusCode();

        var returns = await factory.WithDbAsync(db => db.ReturnRequests.AsNoTracking().Where(r => r.OrderId == b.OrderId).ToListAsync());
        returns.Should().OnlyContain(r => r.Status == ReturnStatus.Refunded);
        returns.Sum(r => r.RefundAmount!.Value).Should().Be(paidMoney, "hai lần trả cộng lại đúng bằng số tiền đã trả cho dòng");
        returns.Sum(r => r.RefundCoins!.Value).Should().Be(paidCoins);
        (await factory.WithDbAsync(db => db.Refunds.Where(r => r.OrderId == b.OrderId && r.Status == RefundStatus.Succeeded).SumAsync(r => r.Amount)))
            .Should().Be(paidMoney);
        var paymentId = await factory.WithDbAsync(db => db.Payments.Where(p => p.Status == PaymentStatus.Succeeded && db.Orders.Any(o => o.Id == b.OrderId && o.CheckoutId == p.CheckoutId))
            .Select(p => p.Id).SingleAsync());
        (await factory.WithDbAsync(db => db.SimulatedPayments.Where(s => s.PaymentId == paymentId).Select(s => s.RefundedAmount).SingleAsync())).Should().Be(paidMoney);
        (await factory.WithDbAsync(db => db.Products.Where(p => p.Id == b.ProductId).Select(p => p.SoldCount).SingleAsync())).Should().Be(0, "đã hoàn hết");
    }

    [Fact]
    public async Task Rejected_return_goes_to_a_dispute_and_the_admin_decides_for_the_buyer()
    {
        var b = await BuyAsync(2);
        var created = (await (await ReturnAsync(b, 1, await UploadAsync(b.Buyer.Client, "evidence"))).ReadEnvelopeAsync()).Data;
        var code = created.Str("code");
        var id = created.Str("id");

        var noReason = await b.Seller.PostAsJsonAsync($"/api/seller/shops/{b.Store.ShopId}/returns/{id}/actions", new { action = "Reject" });
        (await b.Seller.PostAsJsonAsync($"/api/seller/shops/{b.Store.ShopId}/returns/{id}/actions",
            new { action = "Reject", note = "Ảnh không cho thấy hư hỏng" })).EnsureSuccessStatusCode();
        (await b.Buyer.Client.PostAsJsonAsync($"/api/returns/{code}/dispute", new { reason = "Ly bị nứt rõ ở đáy" })).EnsureSuccessStatusCode();
        var admin = await factory.ClientWithPermissionsAsync(Permissions.DisputeResolve);
        var open = (await (await admin.GetAsync("/api/admin/disputes")).ReadEnvelopeAsync()).Data;
        var decide = await admin.PostAsJsonAsync($"/api/admin/disputes/{id}/decide",
            new { decision = "FavorBuyer", reason = "Ảnh của người mua cho thấy vết nứt", refundAmount = (long?)null, requireReturn = false });
        var after = (await (await b.Buyer.Client.GetAsync($"/api/returns/{code}")).ReadEnvelopeAsync()).Data;

        noReason.StatusCode.Should().Be(HttpStatusCode.Conflict);
        open.GetProperty("items").EnumerateArray().Should().Contain(r => r.Str("id") == id);
        decide.StatusCode.Should().Be(HttpStatusCode.OK, await decide.Content.ReadAsStringAsync());
        after.Str("status").Should().Be("Refunded");
        after.GetProperty("refundAmount").GetInt64().Should().Be(created.GetProperty("requestedAmount").GetInt64());
        after.Str("refundDestination").Should().Be("Ví ShopHub", "đơn COD hoàn vào ví");
        (await factory.WithDbAsync(db => db.Refunds.Where(r => r.OrderId == b.OrderId).Select(r => new { r.Destination, r.Status }).SingleAsync()))
            .Should().Be(new { Destination = RefundDestination.Wallet, Status = RefundStatus.Pending });
    }

    [Fact]
    public async Task Return_and_refund_ships_the_goods_back_then_the_shop_checks_and_restocks()
    {
        var b = await BuyAsync(2);
        var stockBefore = await factory.WithDbAsync(db => db.Skus.Where(s => s.Id == b.SkuId).Select(s => s.Stock).SingleAsync());
        var created = (await (await ReturnAsync(b, 1, await UploadAsync(b.Buyer.Client, "evidence"), "ReturnAndRefund")).ReadEnvelopeAsync()).Data;
        var id = created.Str("id");
        (await b.Seller.PostAsJsonAsync($"/api/seller/shops/{b.Store.ShopId}/returns/{id}/actions", new { action = "Approve" })).EnsureSuccessStatusCode();
        var awaiting = (await (await b.Buyer.Client.GetAsync($"/api/returns/{created.Str("code")}")).ReadEnvelopeAsync()).Data;

        await CarrierStepsAsync(4);
        var check = await factory.WithDbAsync(db => db.ReturnRequests.Where(r => r.Id == Guid.Parse(id)).Select(r => r.Status).SingleAsync());
        (await b.Seller.PostAsJsonAsync($"/api/seller/shops/{b.Store.ShopId}/returns/{id}/actions", new { action = "ConfirmReceived", restock = true }))
            .EnsureSuccessStatusCode();

        awaiting.Str("status").Should().Be("AwaitingReturn");
        awaiting.Str("returnTrackingNo").Should().StartWith("SIM");
        check.Should().Be(ReturnStatus.AwaitingShopCheck);
        (await factory.WithDbAsync(db => db.ReturnRequests.Where(r => r.Id == Guid.Parse(id)).Select(r => r.Status).SingleAsync())).Should().Be(ReturnStatus.Refunded);
        (await factory.WithDbAsync(db => db.Skus.Where(s => s.Id == b.SkuId).Select(s => s.Stock).SingleAsync())).Should().Be(stockBefore + 1);
    }

    [Fact]
    public async Task Only_one_open_return_per_line_even_in_parallel()
    {
        var b = await BuyAsync(3);
        var evidence = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => UploadAsync(b.Buyer.Client, "evidence")));

        var results = await Task.WhenAll(evidence.Select(e => ReturnAsync(b, 1, e)));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
        (await factory.WithDbAsync(db => db.ReturnItems.CountAsync(i => i.OrderItemId == b.ItemId && i.IsOpen))).Should().Be(1);
    }

    [Fact]
    public async Task Deadlines_auto_approve_unanswered_returns_and_close_undisputed_rejections_and_late_requests_are_refused()
    {
        var unanswered = await BuyAsync(1);
        var refused = await BuyAsync(1);
        var late = await BuyAsync(1);
        var a = (await (await ReturnAsync(unanswered, 1, await UploadAsync(unanswered.Buyer.Client, "evidence"))).ReadEnvelopeAsync()).Data.Str("id");
        var r = (await (await ReturnAsync(refused, 1, await UploadAsync(refused.Buyer.Client, "evidence"))).ReadEnvelopeAsync()).Data.Str("id");
        (await refused.Seller.PostAsJsonAsync($"/api/seller/shops/{refused.Store.ShopId}/returns/{r}/actions", new { action = "Reject", note = "Không đủ căn cứ" }))
            .EnsureSuccessStatusCode();
        await factory.WithDbAsync(db => db.ReturnRequests.Where(x => x.Id == Guid.Parse(a) || x.Id == Guid.Parse(r))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.RespondBy, DateTimeOffset.UtcNow.AddMinutes(-1))));
        await factory.WithDbAsync(db => db.Orders.Where(o => o.Id == late.OrderId)
            .ExecuteUpdateAsync(u => u.SetProperty(o => o.DeliveredAt, DateTimeOffset.UtcNow.AddDays(-20))));

        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ReturnAutomationService>().RunAsync(CancellationToken.None);
        var lateResponse = await ReturnAsync(late, 1, await UploadAsync(late.Buyer.Client, "evidence"));

        (await factory.WithDbAsync(db => db.ReturnRequests.Where(x => x.Id == Guid.Parse(a)).Select(x => x.Status).SingleAsync())).Should().Be(ReturnStatus.Refunded);
        (await factory.WithDbAsync(db => db.ReturnRequests.Where(x => x.Id == Guid.Parse(r)).Select(x => x.Status).SingleAsync())).Should().Be(ReturnStatus.Closed);
        lateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await lateResponse.ReadEnvelopeAsync()).Message.Should().Contain("quá hạn");
    }

    [Fact]
    public async Task A_refunded_line_loses_its_review_reward_and_strangers_cannot_touch_returns()
    {
        var b = await BuyAsync(1);
        var photo = await UploadAsync(b.Buyer.Client, "review");
        (await b.Buyer.Client.PostAsJsonAsync($"/api/orders/{b.Code}/items/{b.ItemId}/review",
            new { rating = 5, content = new string('x', 80), anonymous = false, mediaAssetIds = new[] { photo } })).EnsureSuccessStatusCode();
        var created = (await (await ReturnAsync(b, 1, await UploadAsync(b.Buyer.Client, "evidence"))).ReadEnvelopeAsync()).Data;
        var stranger = await factory.CreateUserAsync();
        var otherStore = await factory.CreateStoreAsync(products: [new("Ly Khác", "Bình Giữ Nhiệt", 10_000, 5, "Việt Nam")]);
        var otherSeller = await StaffAsync(otherStore);

        (await stranger.Client.GetAsync($"/api/returns/{created.Str("code")}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.Client.PostAsJsonAsync($"/api/returns/{created.Str("code")}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otherSeller.PostAsJsonAsync($"/api/seller/shops/{otherStore.ShopId}/returns/{created.Str("id")}/actions", new { action = "Approve" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await b.Seller.PostAsJsonAsync($"/api/seller/shops/{b.Store.ShopId}/returns/{created.Str("id")}/actions", new { action = "Approve" })).EnsureSuccessStatusCode();
        (await factory.WithDbAsync(db => db.CoinLedger.Where(c => c.UserId == b.Buyer.Id).SumAsync(c => c.Delta))).Should().Be(0, "thưởng 100 xu bị thu hồi");

        // L125: editing the review afterwards must not hand the revoked reward out again
        var reviewId = await factory.WithDbAsync(db => db.Reviews.Where(r => r.BuyerId == b.Buyer.Id).Select(r => r.Id).SingleAsync());
        (await b.Buyer.Client.PutAsJsonAsync($"/api/reviews/{reviewId}",
            new { rating = 4, content = new string('y', 90), anonymous = false, mediaAssetIds = new[] { photo } })).EnsureSuccessStatusCode();
        (await factory.WithDbAsync(db => db.CoinLedger.Where(c => c.UserId == b.Buyer.Id).SumAsync(c => c.Delta))).Should().Be(0, "xu đã thu hồi không được thưởng lại khi sửa đánh giá");
    }
}
