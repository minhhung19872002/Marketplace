using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Promo;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>
/// Buyer-site pieces of II.2–II.5 and 3.11: shipping estimate on the product page, the shop matching a keyword, the
/// category's banners and featured brands, the shop's running programmes, its "online" time, reviews by variant.
/// </summary>
[Collection(ApiCollection.Name)]
public class StorefrontExtrasTests(ApiFactory factory)
{
    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        var res = await client.GetAsync(url);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.ReadEnvelopeAsync()).Data;
    }

    [Fact]
    public async Task The_product_page_quotes_shipping_to_my_default_address_or_a_picked_province_with_the_shops_carriers()
    {
        var store = await factory.CreateStoreAsync("01", products: [new("Đèn Ước Tính", "Đèn Bàn", 150_000, 10, "Việt Nam", WeightG: 900)]);
        var product = store.Products["Đèn Ước Tính"];

        // A guest: to the capital by default, then to another province — farther costs more
        var guest = factory.CreateClient();
        var near = await GetAsync(guest, $"/api/products/{product}/shipping");
        near.GetProperty("destination").Str("provinceCode").Should().Be("01");
        near.Str("fromProvinceName").Should().NotBeNullOrEmpty();
        var options = near.GetProperty("options").EnumerateArray().ToList();
        options.Should().NotBeEmpty();
        var far = await GetAsync(guest, $"/api/products/{product}/shipping?province=79");
        far.GetProperty("destination").Str("provinceCode").Should().Be("79");
        var code = options[0].Str("code");
        far.GetProperty("options").EnumerateArray().Single(o => o.Str("code") == code).GetProperty("fee").GetInt64()
            .Should().BeGreaterThan(options[0].GetProperty("fee").GetInt64(), "khác miền đắt hơn cùng tỉnh");

        // Signed in: my default address, and my addresses to pick from
        var buyer = await factory.CreateUserAsync();
        var addressId = await factory.AddAddressAsync(buyer.Id, "79");
        var mine = await GetAsync(buyer.Client, $"/api/products/{product}/shipping");
        mine.GetProperty("destination").Str("addressId").Should().Be(addressId.ToString());
        mine.GetProperty("myAddresses").GetArrayLength().Should().Be(1);

        // The shop switched a carrier off → not quoted; the product limited to one carrier → only that one
        await factory.WithDbAsync(async db =>
        {
            db.ShopShippingChannels.Add(new Domain.Shops.ShopShippingChannel(store.ShopId, code, false, true));
            await db.SaveChangesAsync();
        });
        (await GetAsync(guest, $"/api/products/{product}/shipping?province=48")).GetProperty("options").EnumerateArray()
            .Select(o => o.Str("code")).Should().NotContain(code);
        var kept = options.Select(o => o.Str("code")).First(c => c != code);
        await factory.WithDbAsync(async db =>
        {
            var p = await db.Products.SingleAsync(x => x.Id == product);
            p.LimitCarriers([kept]);
            await db.SaveChangesAsync();
        });
        (await GetAsync(guest, $"/api/products/{product}/shipping?province=31")).GetProperty("options").EnumerateArray()
            .Select(o => o.Str("code")).Should().Equal(kept);
    }

    [Fact]
    public async Task A_keyword_shows_the_matching_shop_and_the_category_page_its_banners_and_best_selling_brands()
    {
        var store = await factory.CreateStoreAsync("79", products:
        [
            new("Đèn Hiệu A", "Đèn Bàn", 100_000, 10, "Việt Nam"), new("Đèn Hiệu B", "Đèn Bàn", 120_000, 10, "Việt Nam"),
        ]);
        var shops = await GetAsync(factory.CreateClient(), $"/api/search/shops?q={store.Marker}");
        shops.EnumerateArray().Single().Str("id").Should().Be(store.ShopId.ToString());

        var (slug, brandId) = await factory.WithDbAsync(async db =>
        {
            var leaf = await db.Categories.SingleAsync(c => c.Name == "Đèn Bàn" && c.Level == 3);
            var brand = new Brand($"Hiệu {store.Marker}", $"hieu-{store.Marker.ToLowerInvariant()}", null, true);
            db.Brands.Add(brand);
            foreach (var p in await db.Products.Where(p => p.ShopId == store.ShopId).ToListAsync())
            {
                p.SetInfo(p.CategoryId, brand.Id, p.Name, p.Slug, p.Description, p.Condition, p.WeightG, p.LengthMm, p.WidthMm, p.HeightMm, false, 0);
            }
            var now = DateTimeOffset.UtcNow;
            db.Banners.Add(new Banner(BannerPosition.Category, $"Tuần lễ đèn {store.Marker}", "https://example.invalid/b.webp", "/su-kien/den",
                now.AddMinutes(-5), now.AddDays(1), 0, leaf.ParentId, now));
            db.Banners.Add(new Banner(BannerPosition.Category, $"Hết hạn {store.Marker}", "https://example.invalid/c.webp", "/", now.AddDays(-3),
                now.AddDays(-1), 0, leaf.Id, now.AddDays(-3)));
            await db.SaveChangesAsync();
            // Best sellers of the industry (sold_count is a recomputed column; set here as the job would)
            await db.Products.Where(p => p.ShopId == store.ShopId).ExecuteUpdateAsync(u => u.SetProperty(p => p.SoldCount, 100_000));
            return (leaf.Slug, brand.Id);
        });

        var page = await GetAsync(factory.CreateClient(), $"/api/categories/by-slug/{slug}");
        page.GetProperty("banners").EnumerateArray().Select(b => b.Str("title")).Should().Contain($"Tuần lễ đèn {store.Marker}")
            .And.NotContain($"Hết hạn {store.Marker}", "banner ngành của cấp trên hiện ở cấp dưới; banner hết hạn thì không");
        var brands = page.GetProperty("brands").EnumerateArray().ToList();
        brands[0].Str("id").Should().Be(brandId.ToString(), "thương hiệu bán chạy nhất ngành đứng đầu");
        brands[0].GetProperty("productCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task The_shop_page_lists_running_programmes_and_the_product_page_the_shops_last_activity()
    {
        var store = await factory.CreateStoreAsync("01", products: [new("Bút Chương Trình", "Đèn Bàn", 90_000, 10, "Việt Nam")]);
        var now = DateTimeOffset.UtcNow;
        await factory.WithDbAsync(async db =>
        {
            var combo = new Promotion(store.ShopId, PromotionType.Combo, "Combo bút", now.AddMinutes(-5), now.AddDays(3), store.OwnerId, now);
            combo.ConfigureCombo(3, 1_000, 0);
            db.Promotions.Add(combo);
            var ended = new Promotion(store.ShopId, PromotionType.Combo, "Combo cũ", now.AddMinutes(-5), now.AddMinutes(10), store.OwnerId, now);
            ended.ConfigureCombo(2, 500, 0);
            ended.Stop(now);
            db.Promotions.Add(ended);
            await db.SaveChangesAsync();
        });
        var offers = await GetAsync(factory.CreateClient(), $"/api/shops/{store.ShopId}/offers");
        offers.EnumerateArray().Select(o => o.Str("name")).Should().Equal("Combo bút");
        offers[0].Str("text").Should().Be("Mua 3 sản phẩm giảm 10%");

        var before = await GetAsync(factory.CreateClient(), $"/api/products/{store.Products["Bút Chương Trình"]}");
        before.GetProperty("shop").GetProperty("lastActiveAt").ValueKind.Should().Be(JsonValueKind.Null, "chủ shop thử chưa đăng nhập lần nào");
        var staff = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(store.ShopId, staff.Id, Domain.Shops.ShopStaffRole.Manager, Application.Security.ShopPermissions.All));
            await db.SaveChangesAsync();
        });
        var other = await factory.CreateStoreAsync("01", products: [new("Bút Khác", "Đèn Bàn", 90_000, 10, "Việt Nam")]);
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(other.ShopId, staff.Id, Domain.Shops.ShopStaffRole.Manager, Application.Security.ShopPermissions.All));
            await db.SaveChangesAsync();
        });
        var after = await GetAsync(factory.CreateClient(), $"/api/products/{other.Products["Bút Khác"]}");
        after.GetProperty("shop").GetProperty("lastActiveAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Reviews_can_be_filtered_by_the_variant_bought_and_the_summary_counts_each_variant()
    {
        var store = await factory.CreateStoreAsync("01", products: [new("Áo Đánh Giá", "Đèn Bàn", 90_000, 20, "Việt Nam")]);
        var product = store.Products["Áo Đánh Giá"];
        var buyer = await factory.CreateUserAsync();
        var addressId = await factory.AddAddressAsync(buyer.Id, "01");
        (await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = store.Skus["Áo Đánh Giá"], quantity = 3 })).EnsureSuccessStatusCode();
        var request = new
        {
            addressId, shops = new[] { new { shopId = store.ShopId, voucherCode = (string?)null, carrierCode = (string?)null, note = (string?)null } },
            platformVoucherCode = (string?)null, freeshipVoucherCode = (string?)null, useCoins = false, paymentMethod = "Cod",
        };
        var quote = (await (await buyer.Client.PostAsJsonAsync("/api/checkout/quote", request)).ReadEnvelopeAsync()).Data;
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { checkout = request, expectedGrandTotal = quote.GetProperty("grandTotal").GetInt64() }),
        };
        msg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var orderId = Guid.Parse((await (await buyer.Client.SendAsync(msg)).ReadEnvelopeAsync()).Data.GetProperty("orders")[0].Str("id"));

        // Three review rows on lines of that order (the query is what is under test, not the writing rules)
        await factory.WithDbAsync(async db =>
        {
            var item = await db.OrderItems.SingleAsync(i => i.OrderId == orderId);
            var now = DateTimeOffset.UtcNow;
            foreach (var (variant, rating) in new[] { ("Đỏ, M", 5), ("Xanh, L", 3) })
            {
                var extra = new Domain.Sales.OrderItem(orderId, item.SkuId, item.ProductId, item.NameSnapshot, variant, null, item.UnitPrice, item.OriginalPrice, 1);
                db.OrderItems.Add(extra);
                var r = new Review(extra.Id, orderId, product, item.SkuId, store.ShopId, buyer.Id, variant, now);
                r.Write(rating, "Áo mặc vừa, vải mát", [], false);
                db.Reviews.Add(r);
            }
            var red = new Review(item.Id, orderId, product, item.SkuId, store.ShopId, buyer.Id, "Đỏ, M", now);
            red.Write(4, "", [], false);
            db.Reviews.Add(red);
            await db.SaveChangesAsync();
        });

        var all = await GetAsync(factory.CreateClient(), $"/api/products/{product}/reviews");
        all.GetProperty("summary").GetProperty("variants").EnumerateArray().Select(v => (v.Str("variant"), v.GetProperty("count").GetInt32()))
            .Should().Equal(("Đỏ, M", 2), ("Xanh, L", 1));
        var red = await GetAsync(factory.CreateClient(), $"/api/products/{product}/reviews?variant={Uri.EscapeDataString("Đỏ, M")}");
        red.GetProperty("reviews").GetProperty("totalCount").GetInt32().Should().Be(2);
        red.GetProperty("reviews").GetProperty("items").EnumerateArray().Should().OnlyContain(r => r.Str("variant") == "Đỏ, M");
        var redWithText = await GetAsync(factory.CreateClient(), $"/api/products/{product}/reviews?variant={Uri.EscapeDataString("Đỏ, M")}&withComment=true");
        redWithText.GetProperty("reviews").GetProperty("totalCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task The_shop_block_of_a_product_and_the_shop_page_show_the_shops_rating()
    {
        var store = await factory.CreateStoreAsync("79", products: [new("Bút Điểm Shop", "Đèn Bàn", 90_000, 10, "Việt Nam")]);
        // The figure is the recomputed copy on the shop (from its reviews); set it directly for the test
        await factory.WithDbAsync(db => db.Shops.Where(s => s.Id == store.ShopId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RatingAvg, 4.7).SetProperty(s => s.RatingCount, 128)));
        var product = await GetAsync(factory.CreateClient(), $"/api/products/{store.Products["Bút Điểm Shop"]}");
        var shopBlock = product.GetProperty("shop");
        shopBlock.GetProperty("ratingAvg").GetDouble().Should().Be(4.7);
        shopBlock.GetProperty("ratingCount").GetInt32().Should().Be(128);

        var page = await GetAsync(factory.CreateClient(), $"/api/shops/{shopBlock.Str("slug")}");
        page.GetProperty("shop").GetProperty("ratingAvg").GetDouble().Should().Be(4.7);
        page.GetProperty("shop").GetProperty("ratingCount").GetInt32().Should().Be(128);
    }

    [Fact]
    public async Task Running_programmes_on_the_shop_page_include_the_shops_own_flash_sale()
    {
        var store = await factory.CreateStoreAsync("01", products: [new("Bút Flash Shop", "Đèn Bàn", 90_000, 10, "Việt Nam")]);
        var now = DateTimeOffset.UtcNow;
        await factory.WithDbAsync(async db =>
        {
            var running = new FlashSaleSlot(FlashSaleOwner.Shop, store.ShopId, now.AddMinutes(-10), now.AddHours(2), 0, 0, [], now);
            var later = new FlashSaleSlot(FlashSaleOwner.Shop, store.ShopId, now.AddHours(5), now.AddHours(6), 0, 0, [], now);
            db.FlashSaleSlots.AddRange(running, later);
            var item = new FlashSaleItem(running.Id, store.Skus["Bút Flash Shop"], store.Products["Bút Flash Shop"], store.ShopId, 60_000, 5, 1, now);
            item.Approve(now);
            db.FlashSaleItems.Add(item);
            await db.SaveChangesAsync();
            return 0;
        });
        var offers = (await GetAsync(factory.CreateClient(), $"/api/shops/{store.ShopId}/offers")).EnumerateArray().ToList();
        var flash = offers.Should().ContainSingle(o => o.Str("type") == "FlashSale", "chỉ khung đang chạy").Which;
        flash.Str("text").Should().StartWith("Flash Sale của shop đến ");
        flash.GetProperty("productCount").GetInt32().Should().Be(1);
    }
}
