using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Promo;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>
/// #221 (G-VIS round 5): each shop block of the cart says the free-shipping discount a platform code gives it — the
/// largest one its lines can use (a Freeship+ code only for a Freeship+ shop) — and the total bar offers the buyer's
/// ShopHub Xu with how many the ticked lines could take. Both come from the server.
/// </summary>
[Collection(ApiCollection.Name)]
public class CartOffersTests(ApiFactory factory)
{
    [Fact]
    public async Task The_cart_shows_each_shops_free_shipping_discount_and_the_coins_the_ticked_lines_can_take()
    {
        var store = await factory.CreateStoreAsync("79", products: [new("Ấm Giỏ Ưu Đãi", "Đèn Bàn", 200_000, 10, "Việt Nam")]);
        var product = store.Products["Ấm Giỏ Ưu Đãi"];
        var tag = store.Marker.ToUpperInvariant();
        await factory.WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            // Scoped to this product so no other test sees them; the larger one is for Freeship+ shops only
            Voucher Code(string code, long max, long minOrder)
            {
                var v = new Voucher(VoucherOwner.Platform, null, code, "Miễn phí vận chuyển thử");
                v.Configure(VoucherType.FreeShipping, 0, 0, max, minOrder, VoucherAudience.Everyone, [], [product], now.AddMinutes(-5), now.AddDays(3), null, 5, true,
                    VoucherChannel.All);
                return v;
            }
            var xtra = Code($"FSX{tag}", 9_000_000, 0);
            xtra.SetXtraOnly(true);
            db.Vouchers.AddRange(xtra, Code($"FS{tag}", 8_000_000, 150_000));
            await db.SaveChangesAsync();
        });
        var buyer = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.CoinLedger.Add(new CoinEntry(buyer.Id, 5_000, CoinReason.CheckIn, null, null, now.AddDays(30), "Điểm danh", now));
            await db.SaveChangesAsync();
        });
        var skuId = store.Skus["Ấm Giỏ Ưu Đãi"];
        (await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId, quantity = 1 })).StatusCode.Should().Be(HttpStatusCode.OK);

        var cart = (await (await buyer.Client.GetAsync("/api/cart")).ReadEnvelopeAsync()).Data;
        var shop = cart.GetProperty("shops").EnumerateArray().Single(s => s.Str("shopId") == store.ShopId.ToString());
        var offer = shop.GetProperty("shippingOffer");
        offer.Str("code").Should().Be($"FS{tag}", "mã Freeship+ chỉ dành cho shop tham gia Freeship+");
        offer.GetProperty("discount").GetInt64().Should().Be(8_000_000);
        offer.GetProperty("minOrder").GetInt64().Should().Be(150_000);

        using var scope = factory.Services.CreateScope();
        var bp = await scope.ServiceProvider.GetRequiredService<ISystemParameters>().GetIntAsync(ParameterKeys.CoinMaxPercentBp, CancellationToken.None);
        cart.GetProperty("coinBalance").GetInt64().Should().Be(5_000);
        cart.GetProperty("coinUsable").GetInt64().Should().Be(Math.Min(5_000, 200_000 * bp / 10_000));
    }
}
