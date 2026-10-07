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
            var district = await db.AdminDivisions.Where(d => d.ParentCode == "79").OrderBy(d => d.Code).FirstAsync();
            var ward = await db.AdminDivisions.Where(d => d.ParentCode == district.Code).OrderBy(d => d.Code).FirstAsync();
            var south = new ShopWarehouse(store.ShopId);
            south.Update("Kho Nam", "Kho", "0912345678", "79", district.Code, ward.Code, "2 Đường Thử", false, false);
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
}
