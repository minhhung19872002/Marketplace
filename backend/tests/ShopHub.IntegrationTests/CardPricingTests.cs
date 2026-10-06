using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ShopHub.Domain.Promo;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>Product cards in every grid carry the price in force (the same the product page and checkout use).</summary>
[Collection(ApiCollection.Name)]
public class CardPricingTests(ApiFactory factory)
{
    [Fact]
    public async Task A_running_discount_shows_on_search_shop_and_related_cards_like_on_the_product_page_and_ends_with_it()
    {
        var store = await factory.CreateStoreAsync("79", products:
        [
            new("Quạt Giảm Giá", "Đèn Bàn", 200_000, 20, "Việt Nam"), new("Quạt Thường", "Đèn Bàn", 180_000, 20, "Việt Nam"),
        ]);
        var promoted = store.Products["Quạt Giảm Giá"];
        var plain = store.Products["Quạt Thường"];
        var now = DateTimeOffset.UtcNow;
        var programId = Guid.Empty;
        await factory.WithDbAsync(async db =>
        {
            var program = new PriceProgram(store.Skus["Quạt Giảm Giá"], store.ShopId, PriceProgramKind.Discount, Guid.NewGuid(), 150_000,
                now.AddMinutes(-5), now.AddHours(2));
            db.PricePrograms.Add(program);
            await db.SaveChangesAsync();
            programId = program.Id;
        });
        var client = factory.CreateClient();

        // The product page (deals) says 150.000 — so must every card
        var deals = (await (await client.GetAsync($"/api/products/{promoted}/deals")).ReadEnvelopeAsync()).Data;
        deals.GetProperty("skus")[0].GetProperty("price").GetInt64().Should().Be(150_000);

        var search = (await (await client.GetAsync($"/api/search/products?shopId={store.ShopId}")).ReadEnvelopeAsync()).Data.GetProperty("items");
        Card(search, promoted).GetProperty("minPrice").GetInt64().Should().Be(150_000);
        Card(search, promoted).GetProperty("discountPercent").GetInt32().Should().Be(29, "(210.000 − 150.000) / 210.000");
        Card(search, promoted).GetProperty("isFlashSale").GetBoolean().Should().BeFalse();
        Card(search, plain).GetProperty("minPrice").GetInt64().Should().Be(180_000);

        var others = (await (await client.GetAsync($"/api/products/{plain}/shop-products")).ReadEnvelopeAsync()).Data;
        Card(others, promoted).GetProperty("minPrice").GetInt64().Should().Be(150_000);
        var related = (await (await client.GetAsync($"/api/products/{plain}/related")).ReadEnvelopeAsync()).Data;
        if (related.EnumerateArray().Any(c => c.Str("id") == promoted.ToString()))
            Card(related, promoted).GetProperty("minPrice").GetInt64().Should().Be(150_000);

        // The programme is switched off: cards go back to the list price at once (no index rebuild needed)
        await factory.WithDbAsync(async db =>
        {
            var program = await db.PricePrograms.FindAsync(programId);
            program!.Deactivate();
            await db.SaveChangesAsync();
        });
        search = (await (await client.GetAsync($"/api/search/products?shopId={store.ShopId}&page=1&pageSize=20")).ReadEnvelopeAsync()).Data.GetProperty("items");
        Card(search, promoted).GetProperty("minPrice").GetInt64().Should().Be(200_000);
    }

    private static JsonElement Card(JsonElement list, Guid id) => list.EnumerateArray().Single(c => c.Str("id") == id.ToString());
}
