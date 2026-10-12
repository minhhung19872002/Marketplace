using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
            var leaf = await db.Categories.SingleAsync(c => c.Name == "Đèn bàn" && c.Level == 3);
            var brand = new Brand($"Hiệu {store.Marker}", $"hieu-{store.Marker.ToLowerInvariant()}", null, true);
            db.Brands.Add(brand);
            foreach (var p in await db.Products.Where(p => p.ShopId == store.ShopId).ToListAsync())
            {
                p.SetInfo(p.CategoryId, brand.Id, p.Name, p.Slug, p.Description, p.Condition, p.WeightG, p.LengthMm, p.WidthMm, p.HeightMm, false, 0);
            }
            var now = DateTimeOffset.UtcNow;
            // A search link: a link to a campaign page shows only while that campaign runs (L206), and "/su-kien/den" has none
            db.Banners.Add(new Banner(BannerPosition.Category, $"Tuần lễ đèn {store.Marker}", "https://example.invalid/b.webp", "/tim-kiem?q=den",
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
    public async Task The_category_page_lists_official_shops_selling_there_apart_from_product_brands()
    {
        // G-VIS round 3 (L197): "Thương hiệu nổi bật" mixed Mall shops (BeautyPro, BabyCare) with product brands, and showed
        // a shop's house brand where the shop hardly sells
        var here = await factory.CreateStoreAsync("79", true, [new("Đèn Chính Hãng A", "Đèn Bàn", 300_000, 10, "Việt Nam"), new("Đèn Chính Hãng B", "Đèn Bàn", 320_000, 10, "Việt Nam")]);
        var elsewhere = await factory.CreateStoreAsync("79", true, [new("Áo Shop Khác", "Áo Thun", 100_000, 10, "Việt Nam")]);
        var parentSlug = await factory.WithDbAsync(async db =>
        {
            // The Mall shop's house brand on its products: a shop, not a product brand
            var house = new Brand($"Shop {here.Marker}", $"house-{here.Marker.ToLowerInvariant()}", null, true);
            db.Brands.Add(house);
            foreach (var p in await db.Products.Where(p => p.ShopId == here.ShopId).ToListAsync())
                p.SetInfo(p.CategoryId, house.Id, p.Name, p.Slug, p.Description, p.Condition, p.WeightG, p.LengthMm, p.WidthMm, p.HeightMm, false, 0);
            await db.SaveChangesAsync();
            await db.Products.Where(p => p.ShopId == here.ShopId).ExecuteUpdateAsync(u => u.SetProperty(p => p.SoldCount, 500_000));
            var leaf = await db.Categories.SingleAsync(c => c.Name == "Đèn bàn" && c.Level == 3);
            return await db.Categories.Where(c => c.Id == leaf.ParentId).Select(c => c.Slug).SingleAsync();
        });

        var page = await GetAsync(factory.CreateClient(), $"/api/categories/by-slug/{parentSlug}");
        var shops = page.GetProperty("shops").EnumerateArray().ToList();
        shops[0].Str("id").Should().Be(here.ShopId.ToString(), "shop chính hãng bán chạy nhất trong ngành đứng đầu");
        shops[0].GetProperty("productCount").GetInt32().Should().Be(2);
        shops.Select(s => s.Str("id")).Should().NotContain(elsewhere.ShopId.ToString(), "shop không có hàng trong ngành thì không hiện");
        page.GetProperty("brands").EnumerateArray().Select(b => b.Str("name")).Should().NotContain($"Shop {here.Marker}",
            "thương hiệu riêng của shop Mall nằm ở hàng shop, không lẫn vào hàng thương hiệu");
    }

    [Fact]
    public async Task A_card_with_an_ad_cover_says_its_picture_has_text_and_carries_the_plain_photo()
    {
        // G-VIS round 5 (L203): covers got the campaign strip over their own text, and a grid could not swap one for a photo
        var store = await factory.CreateStoreAsync("79", products: [new("Đèn Bìa Chữ", "Đèn Bàn", 100_000, 10, "Việt Nam"), new("Đèn Ảnh Thường", "Đèn Bàn", 90_000, 10, "Việt Nam")]);
        var (covered, plain) = (store.Products["Đèn Bìa Chữ"], store.Products["Đèn Ảnh Thường"]);
        var marker = store.Marker.ToLowerInvariant();
        await factory.WithDbAsync(async db =>
        {
            db.ProductMedia.RemoveRange(await db.ProductMedia.Where(m => m.ProductId == covered).ToListAsync());
            db.ProductMedia.Add(new ProductMedia(covered, MediaType.Image, null, $"https://example.invalid/{marker}-v2-ad_1200.webp", 0, null));
            db.ProductMedia.Add(new ProductMedia(covered, MediaType.Image, null, $"https://example.invalid/{marker}-0_1200.webp", 1, null));
            await db.SaveChangesAsync();
        });

        using var scope = factory.Services.CreateScope();
        var pricing = scope.ServiceProvider.GetRequiredService<ShopHub.Application.Features.Storefront.CardPricing>();
        ShopHub.Application.Abstractions.ProductCardDto Card(Guid id, string url) => new(id, "x", "x", url, 1, 1, 1, 0, 0, 0, 0, true, store.ShopId, "s", false, false, null);
        var cards = await pricing.ApplyAsync([Card(covered, $"https://example.invalid/{marker}-v2-ad_1200.webp"), Card(plain, "https://example.invalid/a.webp")], CancellationToken.None);
        cards[0].ImageHasText.Should().BeTrue();
        cards[0].PhotoUrl.Should().Be($"https://example.invalid/{marker}-0_1200.webp");
        cards[1].ImageHasText.Should().BeFalse();
        cards[1].PhotoUrl.Should().BeNull();
    }

    [Fact]
    public async Task Every_sub_category_chip_shows_a_photo_and_one_with_nothing_on_sale_is_left_out()
    {
        // G-VIS round 5 (L208): "Chăm sóc da" had no product on sale — its chip drew a letter "C" and led to an empty page
        var store = await factory.CreateStoreAsync("79", products: [new("Đèn Chip Có Hàng", "Đèn Bàn", 100_000, 10, "Việt Nam")]);
        var (parentSlug, empty) = await factory.WithDbAsync(async db =>
        {
            var leaf = await db.Categories.SingleAsync(c => c.Name == "Đèn bàn" && c.Level == 3);
            var parent = await db.Categories.SingleAsync(c => c.Id == leaf.ParentId);
            var empty = new Category(parent.Id, 3, $"Rỗng {store.Marker}", $"rong-{store.Marker.ToLowerInvariant()}", null, 99, 0);
            db.Categories.Add(empty);
            await db.SaveChangesAsync();
            return (parent.Slug, empty.Name);
        });

        var page = await GetAsync(factory.CreateClient(), $"/api/categories/by-slug/{parentSlug}");
        var chips = page.GetProperty("children").EnumerateArray().ToList();
        chips.Should().Contain(c => c.Str("name") == "Đèn bàn");
        chips.Should().NotContain(c => c.Str("name") == empty, "danh mục con chưa có hàng thì không có ảnh và không có kết quả");
        chips.Should().OnlyContain(c => c.GetProperty("imageUrl").ValueKind == System.Text.Json.JsonValueKind.String);
    }

    [Fact]
    public async Task A_sub_category_chip_shows_a_photo_of_the_best_seller_below_it_never_an_ad_cover()
    {
        var store = await factory.CreateStoreAsync("79", products: [new("Đèn Chip Ảnh", "Đèn Bàn", 100_000, 10, "Việt Nam")]);
        var product = store.Products["Đèn Chip Ảnh"];
        var marker = store.Marker.ToLowerInvariant();
        var parentSlug = await factory.WithDbAsync(async db =>
        {
            // The seed's ad cover (text drawn on it) comes first; the photo second
            db.ProductMedia.RemoveRange(await db.ProductMedia.Where(m => m.ProductId == product).ToListAsync());
            db.ProductMedia.Add(new ProductMedia(product, MediaType.Image, null, $"https://example.invalid/{marker}-ad_1200.webp", 0, null));
            db.ProductMedia.Add(new ProductMedia(product, MediaType.Image, null, $"https://example.invalid/{marker}-0_1200.webp", 1, null));
            await db.SaveChangesAsync();
            await db.Products.Where(p => p.Id == product).ExecuteUpdateAsync(u => u.SetProperty(p => p.SoldCount, int.MaxValue / 2));
            var leaf = await db.Categories.SingleAsync(c => c.Name == "Đèn bàn" && c.Level == 3);
            return await db.Categories.Where(c => c.Id == leaf.ParentId).Select(c => c.Slug).SingleAsync();
        });

        var page = await GetAsync(factory.CreateClient(), $"/api/categories/by-slug/{parentSlug}");
        var chip = page.GetProperty("children").EnumerateArray().Single(c => c.Str("name") == "Đèn bàn");
        chip.Str("imageUrl").Should().Be($"https://example.invalid/{marker}-0_1200.webp", "ảnh tròn là ảnh chụp của món bán chạy nhất, không phải ảnh bìa có chữ");
    }

    [Fact]
    public async Task A_mall_tile_shows_the_plain_photo_of_a_best_seller_that_has_an_ad_cover()
    {
        // G-VIS round 3 (L195): the tile's photo was the plain photo of the very best seller — a grater, an ice tray, a
        // plain phone case. A best seller with a designed cover reads as a brand tile; the plain best seller is the fallback
        var store = await factory.CreateStoreAsync("79", true, [new("Bào Rau Trơn", "Đèn Bàn", 50_000, 10, "Việt Nam"), new("Đèn Có Bìa", "Đèn Bàn", 150_000, 10, "Việt Nam")]);
        var plain = store.Products["Bào Rau Trơn"];
        var covered = store.Products["Đèn Có Bìa"];
        var marker = store.Marker.ToLowerInvariant();
        await factory.WithDbAsync(async db =>
        {
            db.ProductMedia.RemoveRange(await db.ProductMedia.Where(m => m.ProductId == plain || m.ProductId == covered).ToListAsync());
            db.ProductMedia.Add(new ProductMedia(plain, MediaType.Image, null, $"https://example.invalid/{marker}-plain-0_1200.webp", 0, null));
            db.ProductMedia.Add(new ProductMedia(covered, MediaType.Image, null, $"https://example.invalid/{marker}-lamp-ad_1200.webp", 0, null));
            db.ProductMedia.Add(new ProductMedia(covered, MediaType.Image, null, $"https://example.invalid/{marker}-lamp-0_1200.webp", 1, null));
            await db.SaveChangesAsync();
            await db.Products.Where(p => p.Id == plain).ExecuteUpdateAsync(u => u.SetProperty(p => p.SoldCount, 900));
            await db.Products.Where(p => p.Id == covered).ExecuteUpdateAsync(u => u.SetProperty(p => p.SoldCount, 50));
            // Among the first Mall tiles whatever else the database holds
            await db.Shops.Where(x => x.Id == store.ShopId).ExecuteUpdateAsync(u => u.SetProperty(x => x.ProductCount, 1_000_000));
        });

        using var scope = factory.Services.CreateScope();
        var tiles = await scope.ServiceProvider.GetRequiredService<MediatR.ISender>().Send(new ShopHub.Application.Features.Storefront.MallShopsQuery(30));
        // G-VIS round 5 (L204): that product's plain photo — the cover's text did not read at tile size
        tiles.Single(t => t.Id == store.ShopId).CoverImageUrl.Should().Be($"https://example.invalid/{marker}-lamp-0_1200.webp");
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
