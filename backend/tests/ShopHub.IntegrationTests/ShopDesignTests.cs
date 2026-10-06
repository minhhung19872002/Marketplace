using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Security;
using ShopHub.Domain.Shops;
using ShopHub.IntegrationTests.Infrastructure;
using SkiaSharp;

namespace ShopHub.IntegrationTests;

/// <summary>Danh mục của shop + trang trí shop (III.9) and how buyers see them on the shop page (II.5).</summary>
[Collection(ApiCollection.Name)]
public class ShopDesignTests(ApiFactory factory)
{
    [Fact]
    public async Task Shop_categories_become_tabs_with_the_shops_order_and_only_products_a_buyer_can_see()
    {
        var store = await factory.CreateStoreAsync("79", products:
        [
            new("Đèn Bàn A", "Đèn Bàn", 120_000, 10, "Việt Nam"), new("Đèn Bàn B", "Đèn Bàn", 130_000, 10, "Việt Nam"),
            new("Đèn Bàn C", "Đèn Bàn", 140_000, 10, "Việt Nam"),
        ]);
        var seller = await OwnerAsync(store);
        var basePath = $"/api/seller/shops/{store.ShopId}/shop-categories";
        var catId = (await (await seller.PostAsJsonAsync(basePath, new { name = "Đèn học", sortOrder = 1 })).ReadEnvelopeAsync<Guid>()).Data;
        (await seller.PostAsJsonAsync(basePath, new { name = "đèn HỌC", sortOrder = 2 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var emptyId = (await (await seller.PostAsJsonAsync(basePath, new { name = "Sắp có", sortOrder = 2 })).ReadEnvelopeAsync<Guid>()).Data;

        Guid[] order = [store.Products["Đèn Bàn C"], store.Products["Đèn Bàn A"], store.Products["Đèn Bàn B"]];
        (await seller.PutAsJsonAsync($"{basePath}/{catId}/products", new { productIds = order })).StatusCode.Should().Be(HttpStatusCode.OK);
        // Another shop's product cannot be placed in my category
        var other = await factory.CreateStoreAsync("79", products: [new("Đèn Khác", "Đèn Bàn", 99_000, 5, "Việt Nam")]);
        (await seller.PutAsJsonAsync($"{basePath}/{catId}/products", new { productIds = new[] { other.Products["Đèn Khác"] } }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // B is hidden by the seller: it leaves the buyer listing and the count, the order of the rest is kept
        await factory.WithDbAsync(async db =>
        {
            var b = await db.Products.SingleAsync(p => p.Id == store.Products["Đèn Bàn B"]);
            b.Hide();
            await db.SaveChangesAsync();
        });
        var slug = await factory.WithDbAsync(db => db.Shops.Where(s => s.Id == store.ShopId).Select(s => s.Slug).SingleAsync());
        var page = (await (await factory.CreateClient().GetAsync($"/api/shops/{slug}")).ReadEnvelopeAsync()).Data;
        var tabs = page.GetProperty("categories").EnumerateArray().ToList();
        tabs.Should().ContainSingle("an empty category is not a tab");
        tabs[0].Str("name").Should().Be("Đèn học");
        tabs[0].GetProperty("productCount").GetInt32().Should().Be(2);

        var listing = (await (await factory.CreateClient().GetAsync($"/api/shops/{store.ShopId}/categories/{catId}/products")).ReadEnvelopeAsync()).Data;
        listing.GetProperty("totalCount").GetInt32().Should().Be(2);
        listing.GetProperty("items").EnumerateArray().Select(i => i.Str("id"))
            .Should().Equal(store.Products["Đèn Bàn C"].ToString(), store.Products["Đèn Bàn A"].ToString());

        // Hidden categories and other shops' categories are not reachable
        (await seller.PutAsJsonAsync($"{basePath}/{catId}", new { name = "Đèn học", sortOrder = 1, isVisible = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await factory.CreateClient().GetAsync($"/api/shops/{store.ShopId}/categories/{catId}/products")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await factory.CreateClient().GetAsync($"/api/shops/{other.ShopId}/categories/{emptyId}/products")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var stranger = await factory.CreateUserAsync();
        (await stranger.Client.GetAsync(basePath)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Deleting a category removes its memberships
        (await seller.DeleteAsync($"{basePath}/{catId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await factory.WithDbAsync(db => db.ShopCategoryProducts.CountAsync(x => x.ShopCategoryId == catId))).Should().Be(0);
    }

    [Fact]
    public async Task The_decoration_is_published_from_own_uploads_and_buyers_see_it_resolved()
    {
        var store = await factory.CreateStoreAsync("79", products:
        [
            new("Bình Hoa A", "Đèn Bàn", 150_000, 10, "Việt Nam"), new("Bình Hoa B", "Đèn Bàn", 160_000, 10, "Việt Nam"),
        ]);
        var seller = await OwnerAsync(store);
        var path = $"/api/seller/shops/{store.ShopId}/decoration";
        var catId = (await (await seller.PostAsJsonAsync($"/api/seller/shops/{store.ShopId}/shop-categories", new { name = "Bình hoa", sortOrder = 1 }))
            .ReadEnvelopeAsync<Guid>()).Data;
        await seller.PutAsJsonAsync($"/api/seller/shops/{store.ShopId}/shop-categories/{catId}/products",
            new { productIds = new[] { store.Products["Bình Hoa B"], store.Products["Bình Hoa A"] } });
        var image = await UploadImageAsync(seller);

        object[] blocks =
        [
            new { type = "Banner", title = "Khai trương", images = new[] { new { assetId = image, link = "/shop/khuyen-mai" } } },
            new { type = "Products", title = "Nổi bật", productIds = new[] { store.Products["Bình Hoa A"] } },
            new { type = "Category", shopCategoryId = catId },
            new { type = "Text", text = "Giao nhanh trong ngày tại TP.HCM." },
        ];
        var save = await seller.PutAsJsonAsync(path, new { blocks });
        save.StatusCode.Should().Be(HttpStatusCode.OK, await save.Content.ReadAsStringAsync());

        var home = (await (await factory.CreateClient().GetAsync($"/api/shops/{store.ShopId}/home")).ReadEnvelopeAsync()).Data.EnumerateArray().ToList();
        home.Select(b => b.Str("type")).Should().Equal("Banner", "Products", "Category", "Text");
        var bannerUrl = home[0].GetProperty("images")[0].Str("url");
        bannerUrl.Should().Contain(".webp");
        home[1].GetProperty("products").EnumerateArray().Select(p => p.Str("id")).Should().Equal(store.Products["Bình Hoa A"].ToString());
        home[2].Str("title").Should().Be("Bình hoa", "a category block without its own title shows the category name");
        home[2].GetProperty("products").EnumerateArray().Select(p => p.Str("id"))
            .Should().Equal(store.Products["Bình Hoa B"].ToString(), store.Products["Bình Hoa A"].ToString());

        // Re-saving may keep an image already on the layout by URL…
        var keep = await seller.PutAsJsonAsync(path, new { blocks = new object[] { new { type = "Banner", images = new[] { new { url = bannerUrl } } } } });
        keep.StatusCode.Should().Be(HttpStatusCode.OK, await keep.Content.ReadAsStringAsync());
        // …but never an arbitrary external URL, an external link, someone else's upload or another shop's product
        (await seller.PutAsJsonAsync(path, new { blocks = new object[] { new { type = "Banner", images = new[] { new { url = "https://evil.example/x.png" } } } } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await seller.PutAsJsonAsync(path, new { blocks = new object[] { new { type = "Banner", images = new[] { new { url = bannerUrl, link = "https://evil.example" } } } } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        var strangerImage = await UploadImageAsync((await factory.CreateUserAsync()).Client);
        (await seller.PutAsJsonAsync(path, new { blocks = new object[] { new { type = "Banner", images = new[] { new { assetId = strangerImage } } } } }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        var other = await factory.CreateStoreAsync("79", products: [new("Bình Khác", "Đèn Bàn", 99_000, 5, "Việt Nam")]);
        (await seller.PutAsJsonAsync(path, new { blocks = new object[] { new { type = "Products", productIds = new[] { other.Products["Bình Khác"] } } } }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await seller.PutAsJsonAsync(path, new { blocks = Enumerable.Range(0, ShopDecoration.MaxBlocks + 1).Select(_ => new { type = "Text", text = "x" }) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The shop page tells the buyer app there is a "Dạo" tab
        var slug = await factory.WithDbAsync(db => db.Shops.Where(s => s.Id == store.ShopId).Select(s => s.Slug).SingleAsync());
        (await (await factory.CreateClient().GetAsync($"/api/shops/{slug}")).ReadEnvelopeAsync()).Data.GetProperty("hasDecoration").GetBoolean().Should().BeTrue();
    }

    private static async Task<Guid> UploadImageAsync(HttpClient client)
    {
        using var bmp = new SKBitmap(600, 200);
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(new SKColor(200, 80, 40));
        using var img = SKImage.FromBitmap(bmp);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(img.Encode(SKEncodedImageFormat.Png, 90).ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "banner.png");
        var res = await client.PostAsync("/api/media/shop", form);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return Guid.Parse((await res.ReadEnvelopeAsync()).Data.Str("id"));
    }

    private async Task<HttpClient> OwnerAsync(TestStore store)
    {
        var user = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new ShopStaff(store.ShopId, user.Id, ShopStaffRole.Manager, ShopPermissions.All));
            await db.SaveChangesAsync();
        });
        return user.Client;
    }
}
