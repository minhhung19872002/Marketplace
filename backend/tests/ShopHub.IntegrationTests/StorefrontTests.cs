using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Shops;
using ShopHub.Infrastructure.Search;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class StorefrontTests(ApiFactory factory)
{
    private record Store(Guid ShopId, Guid OwnerId, string Marker, Dictionary<string, Guid> Products);

    private async Task<Store> StoreAsync(string province = "01", bool mall = false, params SeedProduct[] products)
    {
        var s = await factory.CreateStoreAsync(province, mall, products);
        return new Store(s.ShopId, s.OwnerId, s.Marker, s.Products);
    }

    private async Task<JsonElement> SearchAsync(string query)
    {
        var res = await factory.CreateClient().GetAsync($"/api/search/products?{query}");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.ReadEnvelopeAsync()).Data;
    }

    private static List<string> Names(JsonElement result) => result.GetProperty("items").EnumerateArray().Select(i => i.Str("name")).ToList();

    // ---------- search quality ----------

    [Fact]
    public async Task Accent_insensitive_typo_tolerant_and_synonyms()
    {
        var s = await StoreAsync(products: [new("Điện Thoại Thông Minh Zentrix", "Điện Thoại Thông Minh", 5_000_000, 10, "Hàn Quốc"),
            new("Ốp Lưng Trong Suốt", "Ốp Lưng", 50_000, 10, "Việt Nam")]);

        var noTones = await SearchAsync($"q=dien+thoai+zentrix+{s.Marker}");
        var withTypo = await SearchAsync($"q=dien+thaoi+{s.Marker}");
        var synonym = await SearchAsync($"q=dt+{s.Marker}");

        noTones.Str("engine").Should().Be("meilisearch");
        Names(noTones).Should().Contain($"Điện Thoại Thông Minh Zentrix {s.Marker}");
        Names(withTypo).Should().Contain($"Điện Thoại Thông Minh Zentrix {s.Marker}", "gõ sai một ký tự vẫn ra");
        Names(synonym).Should().Contain($"Điện Thoại Thông Minh Zentrix {s.Marker}", "\"dt\" = \"điện thoại\"");
        Names(noTones).Should().NotContain($"Ốp Lưng Trong Suốt {s.Marker}", "khớp từng từ chứ không phải bất kỳ từ nào");
    }

    [Fact]
    public async Task Facet_counts_match_an_independent_sql_count()
    {
        var s = await StoreAsync("79", mall: true, products:
        [
            new("Áo Thun A", "Áo Thun", 99_000, 5, "Việt Nam", "Cotton"),
            new("Áo Thun B", "Áo Thun", 149_000, 0, "Việt Nam", "Polyester"),
            new("Áo Sơ Mi C", "Áo Sơ Mi", 249_000, 5, "Trung Quốc", "Cotton"),
            new("Quần Jean D", "Quần Jean", 399_000, 5, "Việt Nam", "Jean"),
        ]);

        var result = await SearchAsync($"shopId={s.ShopId}");
        var facets = result.GetProperty("facets");
        int Count(string facet, string label) => facets.GetProperty(facet).EnumerateArray().Where(f => f.Str("label") == label).Select(f => f.GetProperty("count").GetInt32()).SingleOrDefault();

        var sql = await factory.WithDbAsync(async db =>
        {
            var shopProducts = db.Products.Where(p => p.ShopId == s.ShopId && p.Status == ProductStatus.Active);
            var shirtsLeaf = await db.Categories.Where(c => c.Name == "Áo Thun").Select(c => c.Id).SingleAsync();
            var topLevel = await db.Categories.Where(c => c.Name == "Thời Trang Nam").Select(c => c.Id).SingleAsync();
            var cotton = await (from p in shopProducts from a in p.Attributes join d in db.CategoryAttributes on a.AttributeId equals d.Id
                                where d.Name == "Chất liệu" && a.Values.Contains("Cotton") select p.Id).Distinct().CountAsync();
            return new
            {
                Total = await shopProducts.CountAsync(),
                Shirts = await shopProducts.CountAsync(p => p.CategoryId == shirtsLeaf),
                Fashion = await shopProducts.CountAsync(p => db.Categories.Any(l3 => l3.Id == p.CategoryId
                    && db.Categories.Any(l2 => l2.Id == l3.ParentId && l2.ParentId == topLevel))),
                Cotton = cotton,
            };
        });

        result.GetProperty("totalCount").GetInt32().Should().Be(sql.Total);
        Count("categories", "Áo Thun").Should().Be(sql.Shirts);
        Count("categories", "Thời Trang Nam").Should().Be(sql.Fashion);
        Count("provinces", "Hồ Chí Minh").Should().Be(sql.Total);
        Count("shopTypes", "ShopHub Mall").Should().Be(sql.Total);
        facets.GetProperty("attributes").GetProperty("Chất liệu").EnumerateArray()
            .Single(v => v.Str("label") == "Cotton").GetProperty("count").GetInt32().Should().Be(sql.Cotton);

        // Filters narrow results and the counts follow
        var cottonOnly = await SearchAsync($"shopId={s.ShopId}&attrs={Uri.EscapeDataString("Chất liệu=Cotton")}&inStock=true");
        Names(cottonOnly).Should().BeEquivalentTo([$"Áo Thun A {s.Marker}", $"Áo Sơ Mi C {s.Marker}"]);
    }

    [Fact]
    public async Task Postgres_fallback_returns_the_same_results_and_counts_as_meilisearch()
    {
        var s = await StoreAsync("48", products:
        [
            new("Nồi Chiên Không Dầu Lớn", "Nồi Chiên Không Dầu", 1_200_000, 10, "Hàn Quốc"),
            new("Nồi Chiên Không Dầu Nhỏ", "Nồi Chiên Không Dầu", 800_000, 3, "Trung Quốc"),
            new("Máy Xay Đa Năng", "Máy Xay", 600_000, 0, "Việt Nam"),
            new("Bình Giữ Nhiệt Inox", "Bình Giữ Nhiệt", 150_000, 20, "Việt Nam"),
        ]);

        // A word that only appears in the products' top-level category name, not in any product name
        var industry = await factory.WithDbAsync(db => (from leaf in db.Categories
                                                         join mid in db.Categories on leaf.ParentId equals mid.Id
                                                         join top in db.Categories on mid.ParentId equals top.Id
                                                         where leaf.Name == "Máy Xay"
                                                         select top.Name).SingleAsync());
        var categoryOnly = Slug.Fold(industry).Split(' ')[0];
        var requests = new[]
        {
            Request(s.ShopId, categoryOnly, ProductSort.PriceAsc),
            Request(s.ShopId, null, ProductSort.PriceAsc),
            Request(s.ShopId, "noi chien", ProductSort.PriceDesc),
            Request(s.ShopId, null, ProductSort.Newest, minPrice: 500_000, maxPrice: 1_000_000),
            Request(s.ShopId, null, ProductSort.PriceAsc, inStock: true, attrs: ["Xuất xứ=Việt Nam", "Xuất xứ=Hàn Quốc"]),
        };

        using var scope = factory.Services.CreateScope();
        var meili = scope.ServiceProvider.GetRequiredService<MeiliProductSearch>();
        var postgres = scope.ServiceProvider.GetRequiredService<PostgresProductSearch>();
        foreach (var r in requests)
        {
            var a = await meili.SearchAsync(r, CancellationToken.None);
            a.TotalCount.Should().BePositive($"phép thử phải có kết quả: {r}");
            var b = await postgres.SearchAsync(r, CancellationToken.None);
            b.Items.Select(i => i.Id).Should().Equal(a.Items.Select(i => i.Id), $"cùng thứ tự cho {r}");
            b.TotalCount.Should().Be(a.TotalCount);
            b.Facets.Categories.Should().BeEquivalentTo(a.Facets.Categories);
            b.Facets.Provinces.Should().BeEquivalentTo(a.Facets.Provinces);
            b.Facets.Attributes.Should().BeEquivalentTo(a.Facets.Attributes);
        }

        static ProductSearchRequest Request(Guid shopId, string? q, ProductSort sort, long? minPrice = null, long? maxPrice = null,
            bool inStock = false, string[]? attrs = null) =>
            new(q, null, shopId, [], [], minPrice, maxPrice, null, false, false, inStock, null, attrs ?? [], sort, 1, 60);
    }

    [Fact]
    public async Task Hidden_or_sold_out_products_leave_the_results()
    {
        var s = await StoreAsync(products: [new("Loa Bluetooth Mini", "Loa Bluetooth", 300_000, 1, "Việt Nam"), new("Loa Bluetooth Lớn", "Loa Bluetooth", 900_000, 1, "Việt Nam")]);
        await factory.WithDbAsync(async db =>
        {
            var hidden = await db.Products.SingleAsync(p => p.Id == s.Products["Loa Bluetooth Lớn"]);
            hidden.Hide();
            await db.SaveChangesAsync();
            await db.Skus.Where(x => x.ProductId == s.Products["Loa Bluetooth Mini"]).ExecuteUpdateAsync(u => u.SetProperty(x => x.Stock, 0));
        });
        // The SQL stock change bypassed EF; resync like the stock-adjust handler does
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ISearchIndexer>().SyncProductsAsync([s.Products["Loa Bluetooth Mini"]], CancellationToken.None);
        await factory.DispatchOutboxAsync();

        Names(await SearchAsync($"shopId={s.ShopId}")).Should().Equal($"Loa Bluetooth Mini {s.Marker}");
        Names(await SearchAsync($"shopId={s.ShopId}&inStock=true")).Should().BeEmpty();
        (await factory.CreateClient().GetAsync($"/api/products/{s.Products["Loa Bluetooth Lớn"]}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invalid_price_range_is_a_clear_400()
    {
        var res = await factory.CreateClient().GetAsync("/api/search/products?minPrice=500000&maxPrice=100000");

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.ReadEnvelopeAsync()).Errors.Should().Contain(e => e.Message.Contains("Khoảng giá không hợp lệ"));
    }

    // ---------- product page ----------

    [Fact]
    public async Task Product_page_marks_sold_out_options_and_vacation_shops_cannot_sell()
    {
        var s = await StoreAsync(products: [new("Áo Khoác Gió", "Áo Khoác", 300_000, 5, "Việt Nam", "Polyester")]);
        var id = s.Products["Áo Khoác Gió"];
        await factory.WithDbAsync(async db =>
        {
            var p = await db.Products.Include(x => x.Tiers).ThenInclude(t => t.Options).Include(x => x.Skus).SingleAsync(x => x.Id == id);
            p.SetVariants([new TierSpec("Size", [new OptionSpec("M", null), new OptionSpec("L", null)])],
                [new SkuSpec("M", null, null, 300_000, 350_000, 0, null, true), new SkuSpec("L", null, null, 320_000, 350_000, 4, null, true)]);
            await db.SaveChangesAsync();
        });

        var page = (await (await factory.CreateClient().GetAsync($"/api/products/{id}")).ReadEnvelopeAsync()).Data;
        await factory.WithDbAsync(async db =>
        {
            var shop = await db.Shops.SingleAsync(x => x.Id == s.ShopId);
            shop.StartVacation(DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        });
        var onVacation = (await (await factory.CreateClient().GetAsync($"/api/products/{id}")).ReadEnvelopeAsync()).Data;

        var options = page.GetProperty("tiers")[0].GetProperty("options").EnumerateArray().ToList();
        options.Single(o => o.Str("value") == "M").GetProperty("available").GetBoolean().Should().BeFalse("hết hàng thì mờ đi");
        options.Single(o => o.Str("value") == "L").GetProperty("available").GetBoolean().Should().BeTrue();
        page.GetProperty("breadcrumb").EnumerateArray().Select(b => b.Str("name")).Should().Equal("Thời Trang Nam", "Áo", "Áo Khoác");
        page.GetProperty("purchasable").GetBoolean().Should().BeTrue();
        onVacation.GetProperty("purchasable").GetBoolean().Should().BeFalse();
        onVacation.GetProperty("shop").GetProperty("onVacation").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_vacation_that_has_ended_reopens_the_shop_without_its_owner()
    {
        // L141: VacationUntil was stored but nothing compared it with the clock, so the shop stayed closed
        var s = await StoreAsync(products: [new("Mũ Hết Nghỉ", "Áo Khoác", 150_000, 5, "Việt Nam", "Cotton")]);
        await factory.WithDbAsync(async db =>
        {
            var shop = await db.Shops.SingleAsync(x => x.Id == s.ShopId);
            shop.StartVacation(DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        });
        await factory.WithDbAsync(db => db.Shops.Where(x => x.Id == s.ShopId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.VacationUntil, DateTimeOffset.UtcNow.AddMinutes(-1))));

        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ShopHub.Infrastructure.Jobs.OrderAutomationJob>().RunJobAsync();

        var shop = await factory.WithDbAsync(db => db.Shops.AsNoTracking().SingleAsync(x => x.Id == s.ShopId));
        shop.Status.Should().Be(ShopStatus.Active, "hết ngày tạm nghỉ thì shop tự mở lại");
        shop.VacationUntil.Should().BeNull();
        var buyer = await factory.CreateUserAsync();
        (await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = await factory.WithDbAsync(db => db.Skus.Where(k => k.ProductId == s.Products["Mũ Hết Nghỉ"]).Select(k => k.Id).FirstAsync()), quantity = 1 })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Views_are_counted_once_per_viewer_within_the_window()
    {
        var s = await StoreAsync(products: [new("Gối Ngủ Êm", "Gối", 200_000, 5, "Việt Nam")]);
        var id = s.Products["Gối Ngủ Êm"];
        var guest = factory.CreateClient(new() { HandleCookies = true });
        var buyer = await factory.CreateUserAsync();

        for (var i = 0; i < 3; i++) (await guest.PostAsync($"/api/products/{id}/views", null)).EnsureSuccessStatusCode();
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => factory.Authorized(buyer.AccessToken).PostAsync($"/api/products/{id}/views", null)));
        var viewed = (await (await guest.GetAsync("/api/viewed")).ReadEnvelopeAsync()).Data;

        (await factory.WithDbAsync(db => db.ProductViews.CountAsync(v => v.ProductId == id))).Should().Be(2,
            "khách và người mua mỗi người một lượt, kể cả khi gọi song song");
        viewed.EnumerateArray().Select(v => v.Str("id")).Should().Contain(id.ToString());
    }

    // ---------- engagement ----------

    [Fact]
    public async Task Parallel_likes_and_follows_are_counted_once_and_recomputed()
    {
        var s = await StoreAsync(products: [new("Bình Sữa Trẻ Em", "Bình Sữa", 150_000, 5, "Nhật Bản")]);
        var id = s.Products["Bình Sữa Trẻ Em"];
        var buyer = await factory.CreateUserAsync();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => factory.Authorized(buyer.AccessToken).PostAsync($"/api/account/wishlist/{id}", null)));
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => factory.Authorized(buyer.AccessToken).PostAsync($"/api/shops/{s.ShopId}/follow", null)));
        var liked = await factory.WithDbAsync(db => db.Products.Where(p => p.Id == id).Select(p => p.LikeCount).SingleAsync());
        var followers = await factory.WithDbAsync(db => db.Shops.Where(x => x.Id == s.ShopId).Select(x => x.FollowerCount).SingleAsync());
        var ids = (await (await buyer.Client.GetAsync("/api/account/wishlist/ids")).ReadEnvelopeAsync()).Data;
        var unlike = (await (await buyer.Client.DeleteAsync($"/api/account/wishlist/{id}")).ReadEnvelopeAsync()).Data.GetInt32();

        liked.Should().Be(1);
        followers.Should().Be(1);
        ids.EnumerateArray().Select(x => x.GetString()).Should().Contain(id.ToString());
        unlike.Should().Be(0);
    }

    [Fact]
    public async Task A_seller_cannot_follow_their_own_shop()
    {
        var s = await StoreAsync(products: []);
        // Any staff member of the shop counts as the seller
        var staff = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new ShopStaff(s.ShopId, staff.Id, ShopStaffRole.Manager, []));
            await db.SaveChangesAsync();
        });

        var res = await staff.Client.PostAsync($"/api/shops/{s.ShopId}/follow", null);

        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---------- home ----------

    [Fact]
    public async Task Recommendations_put_the_viewers_interests_first()
    {
        var s = await StoreAsync(products: [new("Vợt Cầu Lông Siêu Nhẹ", "Cầu Lông", 700_000, 5, "Trung Quốc")]);
        var buyer = await factory.CreateUserAsync();
        (await buyer.Client.PostAsync($"/api/products/{s.Products["Vợt Cầu Lông Siêu Nhẹ"]}/views", null)).EnsureSuccessStatusCode();

        var recs = (await (await buyer.Client.GetAsync("/api/home/recommendations?pageSize=5")).ReadEnvelopeAsync()).Data;

        var first = recs.GetProperty("items")[0];
        var leaf = await factory.WithDbAsync(db => db.Products.Where(p => p.Id == Guid.Parse(first.Str("id"))).Select(p => p.CategoryId).SingleAsync());
        var badminton = await factory.WithDbAsync(db => db.Categories.Where(c => c.Name == "Cầu Lông").Select(c => c.Id).SingleAsync());
        leaf.Should().Be(badminton);
    }

    [Fact]
    public async Task Hot_keywords_and_suggestions_come_from_real_searches()
    {
        var s = await StoreAsync(products: [new("Tai Nghe Chống Ồn Volt", "Tai Nghe Bluetooth", 900_000, 5, "Nhật Bản")]);
        var keyword = $"tai nghe volt {s.Marker}".ToLowerInvariant();
        for (var i = 0; i < 3; i++)
        {
            var user = await factory.CreateUserAsync();
            (await user.Client.GetAsync($"/api/search/products?q={Uri.EscapeDataString(keyword)}")).EnsureSuccessStatusCode();
        }

        var hot = (await (await factory.CreateClient().GetAsync("/api/search/hot-keywords")).ReadEnvelopeAsync()).Data;
        var suggest = (await (await factory.CreateClient().GetAsync($"/api/search/suggest?q={Uri.EscapeDataString("tai nghe volt")}")).ReadEnvelopeAsync()).Data;

        hot.EnumerateArray().Select(k => k.GetString()).Should().Contain(Slug.Fold(keyword));
        suggest.GetProperty("keywords").EnumerateArray().Select(k => k.GetString()).Should().Contain(Slug.Fold(keyword));
        suggest.GetProperty("products").EnumerateArray().Select(p => p.Str("name")).Should().Contain($"Tai Nghe Chống Ồn Volt {s.Marker}");
    }
}
