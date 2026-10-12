using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Shops;
using ShopHub.Infrastructure.Search;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>
/// II.3 facets "đơn vị vận chuyển" and "dịch vụ" (Freeship Xtra, có voucher, COD) with real counts, and "nơi bán" from the
/// ship-from warehouse — the same on Meilisearch and on the PostgreSQL fallback, and following the shop's changes.
/// </summary>
[Collection(ApiCollection.Name)]
public class SearchServiceFacetsTests(ApiFactory factory)
{
    private static ProductSearchRequest Request(string q, IReadOnlyList<string>? carriers = null, bool freeship = false, bool voucher = false,
        bool cod = false, IReadOnlyList<string>? provinces = null) =>
        new(q, null, null, provinces ?? [], [], null, null, null, false, false, false, null, [], ProductSort.Newest, 1, 60, carriers ?? [], freeship, voucher, cod);

    private async Task<List<ProductSearchResult>> BothAsync(ProductSearchRequest r)
    {
        using var scope = factory.Services.CreateScope();
        return
        [
            await scope.ServiceProvider.GetRequiredService<MeiliProductSearch>().SearchAsync(r, CancellationToken.None),
            await scope.ServiceProvider.GetRequiredService<PostgresProductSearch>().SearchAsync(r, CancellationToken.None),
        ];
    }

    private async Task DispatchAllAsync()
    {
        for (var i = 0; i < 50 && (await factory.DispatchOutboxAsync()).Processed > 0; i++) { }
    }

    [Fact]
    public async Task Carrier_and_service_facets_count_and_filter_the_same_on_both_engines_and_follow_the_shop()
    {
        var word = $"Z{Guid.NewGuid():N}"[..9];
        var plain = await factory.CreateStoreAsync("01", products: [new($"Hộp {word} Thường", "Đèn Bàn", 100_000, 10, "Việt Nam")]);
        var rich = await factory.CreateStoreAsync("79", products:
        [
            new($"Hộp {word} Xtra", "Đèn Bàn", 100_000, 10, "Việt Nam"), new($"Hộp {word} Riêng", "Đèn Bàn", 100_000, 10, "Việt Nam"),
        ]);
        var carriers = await factory.WithDbAsync(db => db.Carriers.Where(c => c.IsActive).OrderBy(c => c.SortOrder).Select(c => new { c.Code, c.SupportsCod })
            .ToListAsync());
        var off = carriers[0].Code;
        var only = carriers.First(c => c.Code != off && c.SupportsCod).Code;
        var now = DateTimeOffset.UtcNow;
        await factory.WithDbAsync(async db =>
        {
            var shop = await db.Shops.SingleAsync(s => s.Id == rich.ShopId);
            shop.SetXtra(XtraProgram.FreeshipXtra, true, now);
            // One carrier off for the shop; COD off on every carrier → the shop's products are not COD
            foreach (var c in carriers)
                db.ShopShippingChannels.Add(new ShopShippingChannel(rich.ShopId, c.Code, c.Code != off, false));
            var v = new Voucher(VoucherOwner.Shop, rich.ShopId, $"V{word}".ToUpperInvariant(), "Giảm của shop");
            v.Configure(VoucherType.Amount, 10_000, 0, null, 0, VoucherAudience.Everyone, [], [], now.AddMinutes(-5), now.AddDays(3), null, 1, true, VoucherChannel.All);
            db.Vouchers.Add(v);
            var own = await db.Products.SingleAsync(p => p.Id == rich.Products[$"Hộp {word} Riêng"]);
            own.LimitCarriers([only]);
            await db.SaveChangesAsync();
        });
        await DispatchAllAsync();

        foreach (var res in await BothAsync(Request(word)))
        {
            res.TotalCount.Should().Be(3, res.Engine);
            var services = res.Facets.Services!.ToDictionary(f => f.Value, f => f.Count);
            services.GetValueOrDefault("freeship").Should().Be(2, res.Engine);
            services.GetValueOrDefault("voucher").Should().Be(2, res.Engine);
            services.GetValueOrDefault("cod").Should().Be(1, $"{res.Engine}: chỉ shop thường còn COD");
            var byCarrier = res.Facets.Carriers!.ToDictionary(f => f.Value, f => f.Count);
            byCarrier.GetValueOrDefault(off).Should().Be(1, $"{res.Engine}: shop giàu tắt {off}");
            byCarrier.GetValueOrDefault(only).Should().Be(3, res.Engine);
        }
        foreach (var res in await BothAsync(Request(word, carriers: [off])))
            res.Items.Select(i => i.Name).Should().Equal([$"Hộp {word} Thường {plain.Marker}"], res.Engine);
        foreach (var res in await BothAsync(Request(word, freeship: true, voucher: true)))
            res.Items.Select(i => i.Name).Should().BeEquivalentTo([$"Hộp {word} Xtra {rich.Marker}", $"Hộp {word} Riêng {rich.Marker}"], res.Engine);
        foreach (var res in await BothAsync(Request(word, cod: true)))
            res.Items.Select(i => i.ShopId).Should().Equal([plain.ShopId], res.Engine);

        // The shop turns COD back on for one carrier: its products are COD again (the index follows the shop)
        await factory.WithDbAsync(async db =>
        {
            var row = await db.ShopShippingChannels.SingleAsync(c => c.ShopId == rich.ShopId && c.CarrierCode == only);
            row.Set(true, true);
            await db.SaveChangesAsync();
        });
        await DispatchAllAsync();
        foreach (var res in await BothAsync(Request(word, cod: true)))
            res.TotalCount.Should().Be(3, res.Engine);
    }

    [Fact]
    public async Task Noi_ban_is_the_province_the_product_ships_from()
    {
        var word = $"Y{Guid.NewGuid():N}"[..9];
        var store = await factory.CreateStoreAsync("01", products: [new($"Kệ {word} Bắc", "Đèn Bàn", 100_000, 10, "Việt Nam"), new($"Kệ {word} Nam", "Đèn Bàn", 100_000, 10, "Việt Nam")]);
        await factory.WithDbAsync(async db =>
        {
            var ward = await CommerceFixtures.FirstWardAsync(db, "79");
            var south = new ShopWarehouse(store.ShopId);
            south.Update("Kho Nam", "Kho", "0912345678", "79", ward, "2 Đường Thử", false, false);
            db.ShopWarehouses.Add(south);
            (await db.Shops.SingleAsync(s => s.Id == store.ShopId)).SetMultiWarehouse(true);
            (await db.Products.SingleAsync(p => p.Id == store.Products[$"Kệ {word} Nam"])).ShipFrom(south.Id);
            await db.SaveChangesAsync();
        });
        await DispatchAllAsync();

        foreach (var res in await BothAsync(Request(word)))
            res.Facets.Provinces.ToDictionary(f => f.Value, f => f.Count).Should().BeEquivalentTo(new Dictionary<string, int> { ["01"] = 1, ["79"] = 1 }, res.Engine);
        foreach (var res in await BothAsync(Request(word, provinces: ["79"])))
        {
            res.Items.Select(i => i.Name).Should().Equal([$"Kệ {word} Nam {store.Marker}"], res.Engine);
            res.Items.Single().ProvinceName.Should().Be("Hồ Chí Minh", res.Engine);
        }
    }
    [Fact]
    public async Task A_mall_shops_house_brand_filters_under_shop_not_under_brand_on_both_engines()
    {
        // G-VIS round 5 (L208): "BeautyPro" — the house brand of the Mall shop — sat in the Thương hiệu group of the filters
        var word = $"Z{Guid.NewGuid():N}"[..9];
        var mall = await factory.CreateStoreAsync("79", true, [new($"Kem {word} Nhà", "Đèn Bàn", 100_000, 10, "Việt Nam"), new($"Kem {word} Hai", "Đèn Bàn", 90_000, 10, "Việt Nam")]);
        var other = await factory.CreateStoreAsync("01", products: [new($"Kem {word} Khác", "Đèn Bàn", 80_000, 10, "Việt Nam")]);
        var house = await factory.WithDbAsync(async db =>
        {
            var b = new Domain.Catalog.Brand(mall.Marker, $"house-{mall.Marker.ToLowerInvariant()}", null, true);
            db.Brands.Add(b);
            // The shop is "Shop <marker>": named like its Mall shop the way the seed names them ("Mall BeautyPro" → "BeautyPro")
            await db.Shops.Where(x => x.Id == mall.ShopId).ExecuteUpdateAsync(u => u.SetProperty(x => x.Name, $"Mall {mall.Marker}"));
            foreach (var p in await db.Products.Where(p => p.ShopId == mall.ShopId).ToListAsync())
                p.SetInfo(p.CategoryId, b.Id, p.Name, p.Slug, p.Description, p.Condition, p.WeightG, p.LengthMm, p.WidthMm, p.HeightMm, false, 0);
            db.OutboxMessages.Add(new Domain.SystemConfig.OutboxMessage(Application.SystemConfig.OutboxTypes.SearchSyncProducts,
                System.Text.Json.JsonSerializer.Serialize(new { productIds = mall.Products.Values }), DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return b.Id;
        });
        await DispatchAllAsync();

        foreach (var res in await BothAsync(Request(word)))
        {
            res.Facets.Brands.Select(b => b.Value).Should().NotContain(house.ToString(), $"{res.Engine}: thương hiệu riêng của shop Mall là shop");
            var shops = res.Facets.Shops!.ToDictionary(f => f.Value, f => f.Count);
            shops.GetValueOrDefault(mall.ShopId.ToString()).Should().Be(2, res.Engine);
            shops.GetValueOrDefault(other.ShopId.ToString()).Should().Be(1, res.Engine);
        }
        foreach (var res in await BothAsync(Request(word) with { ShopIds = [mall.ShopId] }))
            res.Items.Select(i => i.ShopId).Should().OnlyContain(id => id == mall.ShopId, res.Engine).And.HaveCount(2);
    }
}
