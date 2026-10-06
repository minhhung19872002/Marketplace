using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Promo;
using ShopHub.Infrastructure.Search;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>
/// Search sorts and filters on the price in force (L057): cards show the discounted / flash price, so the order and the
/// price range must use the same number — on Meilisearch and on the PostgreSQL fallback — and follow programmes that end.
/// </summary>
[Collection(ApiCollection.Name)]
public class SearchPriceIndexTests(ApiFactory factory)
{
    [Fact]
    public async Task Price_sort_and_range_use_the_discounted_price_and_the_index_follows_a_programme_that_ends()
    {
        var store = await factory.CreateStoreAsync("80", products:
        [
            new("Ấm Siêu Tốc Giảm", "Đèn Bàn", 300_000, 20, "Việt Nam"), new("Ấm Siêu Tốc Thường", "Đèn Bàn", 200_000, 20, "Việt Nam"),
        ]);
        var promoted = store.Products["Ấm Siêu Tốc Giảm"];
        var plain = store.Products["Ấm Siêu Tốc Thường"];
        var now = DateTimeOffset.UtcNow;
        await factory.WithDbAsync(async db =>
        {
            // 300.000 → 150.000 for a few seconds
            db.PricePrograms.Add(new PriceProgram(store.Skus["Ấm Siêu Tốc Giảm"], store.ShopId, PriceProgramKind.Discount, Guid.NewGuid(), 150_000,
                now.AddMinutes(-10), now.AddSeconds(8)));
            await db.SaveChangesAsync();
        });
        for (var i = 0; i < 50 && (await factory.DispatchOutboxAsync()).Processed > 0; i++) { }

        var asc = Request(store.ShopId, ProductSort.PriceAsc);
        var cheap = Request(store.ShopId, ProductSort.PriceAsc, maxPrice: 180_000);
        foreach (var engine in Engines())
        {
            (await engine(asc)).Items.Select(i => i.Id).Should().Equal([promoted, plain], "150.000 đang bán đứng trước 200.000");
            (await engine(cheap)).Items.Select(i => i.Id).Should().Equal([promoted], "khoảng giá lọc theo giá đang bán");
        }

        // The programme ends on the clock — no write happens; the minute job re-indexes it
        await Task.Delay(TimeSpan.FromSeconds(9));
        using (var scope = factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<PriceIndexJob>().RunAsync(CancellationToken.None)).Should().BeGreaterThan(0);
        for (var i = 0; i < 50 && (await factory.DispatchOutboxAsync()).Processed > 0; i++) { }
        foreach (var engine in Engines())
        {
            (await engine(asc)).Items.Select(i => i.Id).Should().Equal([plain, promoted], "hết chương trình: 200.000 trước 300.000");
            (await engine(cheap)).Items.Should().BeEmpty();
        }
    }

    private IEnumerable<Func<ProductSearchRequest, Task<ProductSearchResult>>> Engines()
    {
        yield return async r =>
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<MeiliProductSearch>().SearchAsync(r, CancellationToken.None);
        };
        yield return async r =>
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<PostgresProductSearch>().SearchAsync(r, CancellationToken.None);
        };
    }

    private static ProductSearchRequest Request(Guid shopId, ProductSort sort, long? maxPrice = null) =>
        new(null, null, shopId, [], [], null, maxPrice, null, false, false, false, null, [], sort, 1, 60);
}
