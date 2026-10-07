using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;
using ShopHub.Infrastructure.Commerce;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class MarketingTests(ApiFactory factory)
{
    private const string Kettle = "Ấm Đun Siêu Tốc";
    private const string Cup = "Cốc Sứ Trắng";
    private const string Gift = "Khăn Lau Bếp";

    private sealed record Store(TestStore Shop, TestUser Staff);

    private async Task<Store> StoreAsync(int stock = 100)
    {
        var shop = await factory.CreateStoreAsync("79", products:
        [
            new(Kettle, "Bình Giữ Nhiệt", 200_000, stock, "Việt Nam"),
            new(Cup, "Bình Giữ Nhiệt", 50_000, stock, "Việt Nam"),
            new(Gift, "Bình Giữ Nhiệt", 20_000, stock, "Việt Nam"),
        ]);
        var staff = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(shop.ShopId, staff.Id, Domain.Shops.ShopStaffRole.Manager, ShopPermissions.All));
            await db.SaveChangesAsync();
        });
        return new Store(shop, staff);
    }

    private string Url(Store s, string path) => $"/api/seller/shops/{s.Shop.ShopId}/marketing/{path}";

    private sealed record Buyer(TestUser User, Guid AddressId);

    private async Task<Buyer> BuyerAsync()
    {
        var user = await factory.CreateUserAsync();
        return new Buyer(user, await factory.AddAddressAsync(user.Id, "01"));
    }

    private static object Request(Buyer b, Guid shopId, string? platformVoucher = null) => new
    {
        addressId = b.AddressId,
        shops = new[] { new { shopId, voucherCode = (string?)null, carrierCode = (string?)null, note = (string?)null } },
        platformVoucherCode = platformVoucher, freeshipVoucherCode = (string?)null, useCoins = false, paymentMethod = "Cod",
    };

    private static async Task<JsonElement> QuoteAsync(Buyer b, Guid shopId, string? platformVoucher = null) =>
        (await (await b.User.Client.PostAsJsonAsync("/api/checkout/quote", Request(b, shopId, platformVoucher))).ReadEnvelopeAsync()).Data;

    private static async Task<HttpResponseMessage> PlaceAsync(Buyer b, Guid shopId, long total, string? platformVoucher = null)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { checkout = Request(b, shopId, platformVoucher), expectedGrandTotal = total }),
        };
        msg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await b.User.Client.SendAsync(msg);
    }

    private static async Task AddAsync(Buyer b, Guid sku, int quantity) =>
        (await b.User.Client.PostAsJsonAsync("/api/cart/items", new { skuId = sku, quantity })).EnsureSuccessStatusCode();

    /// <summary>A platform slot running right now (admins can only open aligned hours, so tests insert it directly).</summary>
    private async Task<Guid> RunningSlotAsync(int minDiscountBp = 0) =>
        await factory.WithDbAsync(async db =>
        {
            var slot = new FlashSaleSlot(FlashSaleOwner.Platform, null, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(2), minDiscountBp, 0,
                [], DateTimeOffset.UtcNow);
            db.FlashSaleSlots.Add(slot);
            await db.SaveChangesAsync();
            return slot.Id;
        });

    /// <summary>Registration is only open before a slot starts: register first, then move the slot to now.</summary>
    private async Task<Guid> FlashItemAsync(Store store, string product, long flashPrice, int quota, int perUserLimit)
    {
        var slotId = await factory.WithDbAsync(async db =>
        {
            var slot = new FlashSaleSlot(FlashSaleOwner.Platform, null, DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddHours(3), 0, 0, [],
                DateTimeOffset.UtcNow);
            db.FlashSaleSlots.Add(slot);
            await db.SaveChangesAsync();
            return slot.Id;
        });
        var reg = await store.Staff.Client.PostAsJsonAsync(Url(store, $"platform-slots/{slotId}/items"),
            new { items = new[] { new { skuId = store.Shop.Skus[product], flashPrice, quota, perUserLimit } } });
        reg.StatusCode.Should().Be(HttpStatusCode.OK, await reg.Content.ReadAsStringAsync());
        await factory.WithDbAsync(db => db.FlashSaleSlots.Where(s => s.Id == slotId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.StartAt, DateTimeOffset.UtcNow.AddSeconds(-5))));
        var itemId = await factory.WithDbAsync(db => db.FlashSaleItems.Where(i => i.SlotId == slotId).Select(i => i.Id).SingleAsync());
        var admin = await factory.ClientWithPermissionsAsync(Permissions.MarketingManage);
        (await admin.PostAsync($"/api/admin/marketing/flash-items/{itemId}/approve", null)).EnsureSuccessStatusCode();
        return itemId;
    }

    // ---------- price programmes ----------

    [Fact]
    public async Task A_discount_quota_and_per_buyer_limit_hold_when_six_buyers_order_at_once_and_a_cancel_gives_the_unit_back()
    {
        // L139: promotion_skus.per_user_limit was always null and there was no quota (spec 4.6)
        var store = await StoreAsync();
        var sku = store.Shop.Skus[Kettle];
        var create = await store.Staff.Client.PostAsJsonAsync(Url(store, "promotions"), new
        {
            type = "Discount", name = "Ấm giá sốc 3 suất", startAt = DateTimeOffset.UtcNow.AddMinutes(-1), endAt = DateTimeOffset.UtcNow.AddDays(3),
            skus = new[] { new { skuId = sku, price = 150_000, perUserLimit = 1, quota = 3 } },
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());

        var buyers = new List<Buyer>();
        for (var i = 0; i < 6; i++)
        {
            var b = await BuyerAsync();
            await AddAsync(b, sku, 1);
            buyers.Add(b);
        }
        var quotes = await Task.WhenAll(buyers.Select(b => QuoteAsync(b, store.Shop.ShopId)));
        quotes.Should().OnlyContain(q => q.GetProperty("subtotal").GetInt64() == 150_000, "trước khi đặt, cả 6 đều thấy giá ưu đãi");
        var placed = await Task.WhenAll(buyers.Select((b, i) => Task.Run(() => PlaceAsync(b, store.Shop.ShopId, quotes[i].GetProperty("grandTotal").GetInt64()))));

        placed.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(3, "chỉ có 3 suất giá ưu đãi");
        placed.Where(r => r.StatusCode != HttpStatusCode.OK).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        async Task<int> SoldAsync() => await factory.WithDbAsync(db => db.Database.SqlQuery<int>($"SELECT sold AS \"Value\" FROM promo.promotion_skus WHERE sku_id = {sku}").SingleAsync());
        (await SoldAsync()).Should().Be(3);

        // A buyer who got one cannot get a second at the discount price; someone new now sees the normal price
        var winner = buyers[placed.Select((r, i) => (r, i)).First(x => x.r.StatusCode == HttpStatusCode.OK).i];
        await AddAsync(winner, sku, 1);
        var again = await QuoteAsync(winner, store.Shop.ShopId);
        again.GetProperty("subtotal").GetInt64().Should().Be(200_000, "hết suất giá ưu đãi thì về giá thường");
        var late = await BuyerAsync();
        await AddAsync(late, sku, 1);
        (await QuoteAsync(late, store.Shop.ShopId)).GetProperty("subtotal").GetInt64().Should().Be(200_000);

        // Cancelling one of the three orders gives its unit back: the discount price is available again
        var code = (await placed.First(r => r.StatusCode == HttpStatusCode.OK).ReadEnvelopeAsync()).Data.GetProperty("orders")[0].Str("code");
        var owner = buyers[placed.Select((r, i) => (r, i)).First(x => x.r.StatusCode == HttpStatusCode.OK).i];
        (await owner.User.Client.PostAsJsonAsync($"/api/orders/{code}/cancel", new { reason = "Đổi ý" })).EnsureSuccessStatusCode();
        (await SoldAsync()).Should().Be(2);
        (await QuoteAsync(late, store.Shop.ShopId)).GetProperty("subtotal").GetInt64().Should().Be(150_000);

        // The per-buyer limit on its own: the winner still holding a unit is told before placing
        var other = buyers.Where((b, i) => placed[i].StatusCode == HttpStatusCode.OK && b != owner).First();
        await AddAsync(other, sku, 1);
        var limited = await QuoteAsync(other, store.Shop.ShopId);
        limited.GetProperty("problems").EnumerateArray().Select(x => x.GetString()).Should()
            .Contain(m => m!.Contains("Mỗi người chỉ mua tối đa 1 sản phẩm giá ưu đãi"));
    }

    [Fact]
    public async Task A_discount_price_reaches_cart_checkout_and_order_and_a_sku_cannot_be_in_two_price_programmes_at_once()
    {
        var store = await StoreAsync();
        var sku = store.Shop.Skus[Kettle];
        var create = await store.Staff.Client.PostAsJsonAsync(Url(store, "promotions"), new
        {
            type = "Discount", name = "Giảm giá ấm đun", startAt = DateTimeOffset.UtcNow.AddMinutes(-1), endAt = DateTimeOffset.UtcNow.AddDays(3),
            skus = new[] { new { skuId = sku, price = 150_000 } },
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());

        var b = await BuyerAsync();
        await AddAsync(b, sku, 2);
        var cart = (await (await b.User.Client.GetAsync("/api/cart")).ReadEnvelopeAsync()).Data;
        var line = cart.GetProperty("shops")[0].GetProperty("lines")[0];
        line.GetProperty("price").GetInt64().Should().Be(150_000);
        line.Str("priceLabel").Should().Be("Giảm giá");
        var quote = await QuoteAsync(b, store.Shop.ShopId);
        quote.GetProperty("subtotal").GetInt64().Should().Be(300_000);
        var placed = await PlaceAsync(b, store.Shop.ShopId, quote.GetProperty("grandTotal").GetInt64());
        placed.StatusCode.Should().Be(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());
        var orderId = Guid.Parse((await placed.ReadEnvelopeAsync()).Data.GetProperty("orders")[0].Str("id"));
        var item = await factory.WithDbAsync(db => db.OrderItems.AsNoTracking().SingleAsync(i => i.OrderId == orderId));
        item.UnitPrice.Should().Be(150_000);
        item.PriceSource.Should().Be(PriceProgramKind.Discount);

        // The same SKU in another price programme overlapping in time → refused by the database
        var overlap = await store.Staff.Client.PostAsJsonAsync(Url(store, "flash-sales"), new
        {
            startAt = DateTimeOffset.UtcNow.AddDays(1), endAt = DateTimeOffset.UtcNow.AddDays(1).AddHours(2),
            items = new[] { new { skuId = sku, flashPrice = 120_000, quota = 5, perUserLimit = 1 } },
        });
        overlap.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await overlap.Content.ReadAsStringAsync()).Should().Contain("trùng thời gian");

        // After the discount is stopped, the SKU is free again
        var promoId = (await (await store.Staff.Client.GetAsync(Url(store, "promotions"))).ReadEnvelopeAsync()).Data[0].Str("id");
        (await store.Staff.Client.PostAsync(Url(store, $"promotions/{promoId}/stop"), null)).EnsureSuccessStatusCode();
        (await store.Staff.Client.PostAsJsonAsync(Url(store, "flash-sales"), new
        {
            startAt = DateTimeOffset.UtcNow.AddDays(1), endAt = DateTimeOffset.UtcNow.AddDays(1).AddHours(2),
            items = new[] { new { skuId = sku, flashPrice = 120_000, quota = 5, perUserLimit = 1 } },
        })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Platform_flash_registration_checks_the_criteria_and_an_approved_item_shows_on_the_board_with_server_time()
    {
        var store = await StoreAsync();
        var admin = await factory.ClientWithPermissionsAsync(Permissions.MarketingManage);
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7).AddDays(1));
        (await admin.PostAsJsonAsync("/api/admin/marketing/flash-slots", new { date = tomorrow, hour = 10, minDiscountBp = 0, minRating = 0.0 }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "10:00 is not one of FLASH.SLOT_HOURS");
        var created = await admin.PostAsJsonAsync("/api/admin/marketing/flash-slots", new { date = tomorrow, hour = 12, minDiscountBp = 3_000, minRating = 0.0 });
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var slotId = (await created.ReadEnvelopeAsync()).Data.GetString();
        var start = await factory.WithDbAsync(db => db.FlashSaleSlots.Where(s => s.Id == Guid.Parse(slotId!)).Select(s => s.StartAt).SingleAsync());
        start.ToOffset(TimeSpan.FromHours(7)).Hour.Should().Be(12);

        // 20% off is below the slot's 30% minimum
        var weak = await store.Staff.Client.PostAsJsonAsync(Url(store, $"platform-slots/{slotId}/items"),
            new { items = new[] { new { skuId = store.Shop.Skus[Kettle], flashPrice = 160_000, quota = 10, perUserLimit = 1 } } });
        weak.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await weak.Content.ReadAsStringAsync()).Should().Contain("giảm ít nhất 30%");

        var itemId = await FlashItemAsync(store, Kettle, 99_000, 10, 2);
        var board = (await (await factory.CreateClient().GetAsync("/api/flash-sale")).ReadEnvelopeAsync()).Data;
        DateTimeOffset.Parse(board.Str("serverTime")).Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        var shown = board.GetProperty("items").EnumerateArray().Single(i => i.Str("itemId") == itemId.ToString());
        shown.GetProperty("flashPrice").GetInt64().Should().Be(99_000);
        shown.GetProperty("sold").GetInt32().Should().Be(0);

        var deals = (await (await factory.CreateClient().GetAsync($"/api/products/{store.Shop.Products[Kettle]}/deals")).ReadEnvelopeAsync()).Data;
        deals.GetProperty("flash").GetProperty("quota").GetInt32().Should().Be(10);
        deals.GetProperty("skus").EnumerateArray().Single(s => s.Str("skuId") == store.Shop.Skus[Kettle].ToString())
            .GetProperty("price").GetInt64().Should().Be(99_000);
    }

    [Fact]
    public async Task Thirty_buyers_at_once_get_exactly_the_quota_and_a_cancelled_order_gives_its_unit_back()
    {
        var store = await StoreAsync();
        var itemId = await FlashItemAsync(store, Kettle, 99_000, 10, 1);
        var buyers = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => BuyerAsync()));
        var totals = new long[buyers.Length];
        for (var i = 0; i < buyers.Length; i++)
        {
            await AddAsync(buyers[i], store.Shop.Skus[Kettle], 1);
            totals[i] = (await QuoteAsync(buyers[i], store.Shop.ShopId)).GetProperty("grandTotal").GetInt64();
        }

        var results = await Task.WhenAll(buyers.Select((b, i) => Task.Run(() => PlaceAsync(b, store.Shop.ShopId, totals[i]))));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(10);
        results.Where(r => r.StatusCode != HttpStatusCode.OK).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        (await factory.WithDbAsync(db => db.FlashSaleItems.Where(i => i.Id == itemId).Select(i => i.Sold).SingleAsync())).Should().Be(10);
        (await factory.WithDbAsync(db => db.OrderItems.CountAsync(i => i.PriceRefId == itemId))).Should().Be(10);
        var counter = factory.Services.CreateScope().ServiceProvider.GetRequiredService<IFlashSaleCounter>();
        (await counter.LeftAsync(itemId, CancellationToken.None)).Should().Be(0);

        // One winner cancels: the unit is back for someone else
        var winner = buyers[results.Select((r, i) => (r, i)).First(x => x.r.StatusCode == HttpStatusCode.OK).i];
        var code = (await results.First(r => r.StatusCode == HttpStatusCode.OK).ReadEnvelopeAsync()).Data.GetProperty("orders")[0].Str("code");
        (await winner.User.Client.PostAsJsonAsync($"/api/orders/{code}/cancel", new { reason = "Đổi ý" })).EnsureSuccessStatusCode();
        (await factory.WithDbAsync(db => db.FlashSaleItems.Where(i => i.Id == itemId).Select(i => i.Sold).SingleAsync())).Should().Be(9);
        (await counter.LeftAsync(itemId, CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task One_buyer_cannot_take_more_flash_units_than_the_limit_even_over_several_orders()
    {
        var store = await StoreAsync();
        await FlashItemAsync(store, Cup, 25_000, 50, 2);
        var b = await BuyerAsync();
        await AddAsync(b, store.Shop.Skus[Cup], 2);
        var first = await PlaceAsync(b, store.Shop.ShopId, (await QuoteAsync(b, store.Shop.ShopId)).GetProperty("grandTotal").GetInt64());
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());

        await AddAsync(b, store.Shop.Skus[Cup], 1);
        var quote = await QuoteAsync(b, store.Shop.ShopId);
        quote.GetProperty("canPlace").GetBoolean().Should().BeFalse();
        quote.GetProperty("problems").ToString().Should().Contain("tối đa 2 suất");
    }

    // ---------- combo, add-on, gift ----------

    [Fact]
    public async Task Combo_add_on_and_gift_change_the_checkout_and_the_combo_is_the_shops_cost()
    {
        var store = await StoreAsync();
        var start = DateTimeOffset.UtcNow.AddMinutes(-1);
        var end = DateTimeOffset.UtcNow.AddDays(2);
        async Task Create(object body)
        {
            var res = await store.Staff.Client.PostAsJsonAsync(Url(store, "promotions"), body);
            res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        }
        await Create(new { type = "Combo", name = "Mua 3 cốc giảm 10%", startAt = start, endAt = end, productIds = new[] { store.Shop.Products[Cup] }, minQuantity = 3, discountBp = 1_000 });
        await Create(new
        {
            type = "AddOn", name = "Mua ấm kèm cốc", startAt = start, endAt = end, productIds = new[] { store.Shop.Products[Kettle] },
            skus = new[] { new { skuId = store.Shop.Skus[Gift], price = 5_000 } }, maxAddOnQuantity = 1,
        });
        await Create(new
        {
            type = "Gift", name = "Đơn ấm từ 200k tặng khăn", startAt = start, endAt = end, productIds = new[] { store.Shop.Products[Kettle] },
            minSpend = 200_000, giftSkuId = store.Shop.Skus[Gift], giftQuantity = 1,
        });

        // L142: the product page lists what each offer is about — the add-on with its deal price (addable as is), the gift
        var deals = (await (await factory.CreateClient().GetAsync($"/api/products/{store.Shop.Products[Kettle]}/deals")).ReadEnvelopeAsync()).Data;
        var addOn = deals.GetProperty("offers").EnumerateArray().Single(o => o.Str("type") == "AddOn");
        var item = addOn.GetProperty("items").EnumerateArray().Single();
        item.Str("skuId").Should().Be(store.Shop.Skus[Gift].ToString());
        item.GetProperty("price").GetInt64().Should().Be(5_000);
        item.GetProperty("basePrice").GetInt64().Should().BeGreaterThan(5_000);
        deals.GetProperty("offers").EnumerateArray().Single(o => o.Str("type") == "Gift").GetProperty("items")[0].Str("skuId")
            .Should().Be(store.Shop.Skus[Gift].ToString());
        var combo = (await (await factory.CreateClient().GetAsync($"/api/products/{store.Shop.Products[Cup]}/deals")).ReadEnvelopeAsync()).Data
            .GetProperty("offers").EnumerateArray().Single(o => o.Str("type") == "Combo");
        combo.GetProperty("items").EnumerateArray().Select(i => i.Str("productId")).Should().Contain(store.Shop.Products[Cup].ToString());

        var b = await BuyerAsync();
        await AddAsync(b, store.Shop.Skus[Cup], 3);
        await AddAsync(b, store.Shop.Skus[Kettle], 1);
        await AddAsync(b, store.Shop.Skus[Gift], 1);
        var quote = await QuoteAsync(b, store.Shop.ShopId);
        quote.GetProperty("canPlace").GetBoolean().Should().BeTrue(quote.GetProperty("problems").ToString());
        var shop = quote.GetProperty("shops")[0];
        shop.GetProperty("comboDiscount").GetInt64().Should().Be(15_000);
        shop.GetProperty("lines").EnumerateArray().Single(l => l.Str("skuId") == store.Shop.Skus[Gift].ToString())
            .GetProperty("unitPrice").GetInt64().Should().Be(5_000, "add-on price next to the kettle");
        shop.GetProperty("gifts").EnumerateArray().Should().ContainSingle(g => g.Str("skuId") == store.Shop.Skus[Gift].ToString());
        quote.GetProperty("subtotal").GetInt64().Should().Be(150_000 + 200_000 + 5_000);

        var placed = await PlaceAsync(b, store.Shop.ShopId, quote.GetProperty("grandTotal").GetInt64());
        placed.StatusCode.Should().Be(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());
        var orderId = Guid.Parse((await placed.ReadEnvelopeAsync()).Data.GetProperty("orders")[0].Str("id"));
        var order = await factory.WithDbAsync(db => db.Orders.AsNoTracking().Include(o => o.Items).ThenInclude(i => i.Discounts).SingleAsync(o => o.Id == orderId));
        order.ShopDiscount.Should().Be(15_000, "the combo is a discount the shop bears");
        order.Items.SelectMany(i => i.Discounts).Where(d => d.Source == DiscountSource.Combo).Sum(d => d.Amount).Should().Be(15_000);
        var giftLine = order.Items.Single(i => i.GiftPromotionId != null);
        giftLine.UnitPrice.Should().Be(0);
        (await factory.WithDbAsync(db => db.Skus.Where(s => s.Id == store.Shop.Skus[Gift]).Select(s => s.Reserved).SingleAsync()))
            .Should().Be(2, "one bought as add-on, one given as a gift");

        // Two add-on units are more than the deal allows
        var other = await BuyerAsync();
        await AddAsync(other, store.Shop.Skus[Kettle], 1);
        await AddAsync(other, store.Shop.Skus[Gift], 2);
        (await QuoteAsync(other, store.Shop.ShopId)).GetProperty("problems").ToString().Should().Contain("mua kèm tối đa 1");
    }

    // ---------- loyalty ----------

    [Fact]
    public async Task Check_in_pays_the_streak_reward_once_per_day_even_when_pressed_twice_at_once()
    {
        var user = await factory.CreateUserAsync();
        var presses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => user.Client.PostAsync("/api/account/check-in", null))));
        presses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        presses.Where(r => r.StatusCode != HttpStatusCode.OK).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        var status = (await (await user.Client.GetAsync("/api/account/check-in")).ReadEnvelopeAsync()).Data;
        status.GetProperty("doneToday").GetBoolean().Should().BeTrue();
        status.GetProperty("streak").GetInt32().Should().Be(1);
        (await factory.WithDbAsync(db => db.CoinLedger.Where(c => c.UserId == user.Id).SumAsync(c => c.Delta))).Should().Be(100);

        // Checked in yesterday as day 3 → today is day 4 (200 xu)
        var other = await factory.CreateUserAsync();
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(-1);
        await factory.WithDbAsync(async db =>
        {
            db.CheckIns.Add(new Domain.Engage.CheckIn(other.Id, yesterday, 3, 100, DateTimeOffset.UtcNow.AddDays(-1)));
            await db.SaveChangesAsync();
        });
        var day4 = (await (await other.Client.PostAsync("/api/account/check-in", null)).ReadEnvelopeAsync()).Data;
        day4.GetProperty("streak").GetInt32().Should().Be(4);
        (await factory.WithDbAsync(db => db.CoinLedger.Where(c => c.UserId == other.Id).SumAsync(c => c.Delta))).Should().Be(200);
    }

    private async Task<string> CompleteOrderAsync(Store store, Buyer b, string? platformVoucher = null)
    {
        var quote = await QuoteAsync(b, store.Shop.ShopId, platformVoucher);
        quote.GetProperty("canPlace").GetBoolean().Should().BeTrue(quote.GetProperty("problems").ToString());
        var placed = await PlaceAsync(b, store.Shop.ShopId, quote.GetProperty("grandTotal").GetInt64(), platformVoucher);
        placed.StatusCode.Should().Be(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());
        var order = (await placed.ReadEnvelopeAsync()).Data.GetProperty("orders")[0];
        (await store.Staff.Client.PostAsJsonAsync($"/api/seller/shops/{store.Shop.ShopId}/orders/prepare",
            new { orderIds = new[] { Guid.Parse(order.Str("id")) }, pickupMethod = "DropOff", pickupSlot = (string?)null })).EnsureSuccessStatusCode();
        await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == ParameterKeys.LogisticsSimStepSeconds)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.Value, "0")));
        factory.Services.GetRequiredService<ISystemParameters>().Invalidate(ParameterKeys.LogisticsSimStepSeconds);
        for (var i = 0; i < 4; i++)
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<CarrierSimulator>().RunAsync(CancellationToken.None);
        }
        await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == ParameterKeys.LogisticsSimStepSeconds)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.Value, "120")));
        factory.Services.GetRequiredService<ISystemParameters>().Invalidate(ParameterKeys.LogisticsSimStepSeconds);
        (await b.User.Client.PostAsync($"/api/orders/{order.Str("code")}/received", null)).EnsureSuccessStatusCode();
        for (var i = 0; i < 50 && (await factory.DispatchOutboxAsync()).Processed > 0; i++) { }
        return order.Str("code");
    }

    [Fact]
    public async Task Spending_unlocks_the_gold_tier_voucher_and_voucher_cashback_is_paid_once_when_the_order_completes()
    {
        var store = await StoreAsync();
        var b = await BuyerAsync();
        var gold = await factory.CreateVoucherAsync(VoucherOwner.Platform, null, VoucherType.Amount, value: 10_000);
        await factory.WithDbAsync(db => db.Vouchers.Where(v => v.Id == gold.Id).ExecuteUpdateAsync(u => u.SetProperty(v => v.Audience, VoucherAudience.MemberGold)));
        var cashback = await factory.CreateVoucherAsync(VoucherOwner.Platform, null, VoucherType.CoinCashback, percentBp: 1_000, max: 50_000);

        await AddAsync(b, store.Shop.Skus[Kettle], 1);
        (await QuoteAsync(b, store.Shop.ShopId, gold.Code)).GetProperty("problems").ToString().Should().Contain("hạng Vàng");

        // ₫200.000 + ship with 10% cash-back → 20.000 xu after completion
        var code = await CompleteOrderAsync(store, b, cashback.Code);
        (await factory.WithDbAsync(db => db.CoinLedger.Where(c => c.UserId == b.User.Id && c.Reason == CoinReason.VoucherCashback).SumAsync(c => c.Delta)))
            .Should().Be(20_000);
        // The completion event delivered again changes nothing
        await factory.WithDbAsync(async db =>
        {
            var orderId = await db.Orders.Where(o => o.Code == code).Select(o => o.Id).SingleAsync();
            db.OutboxMessages.Add(new Domain.SystemConfig.OutboxMessage(OutboxTypes.OrderEvent,
                JsonSerializer.Serialize(new OrderEventPayload(orderId, OrderEvents.Completed, null), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return 0;
        });
        for (var i = 0; i < 20 && (await factory.DispatchOutboxAsync()).Processed > 0; i++) { }
        (await factory.WithDbAsync(db => db.CoinLedger.Where(c => c.UserId == b.User.Id && c.Reason == CoinReason.VoucherCashback).SumAsync(c => c.Delta)))
            .Should().Be(20_000);

        // Ten more completed kettles (≥ ₫2.000.000) → gold tier → the gold voucher works
        await AddAsync(b, store.Shop.Skus[Kettle], 10);
        await CompleteOrderAsync(store, b);
        var membership = (await (await b.User.Client.GetAsync("/api/account/membership")).ReadEnvelopeAsync()).Data;
        membership.Str("tier").Should().Be("Gold");
        await AddAsync(b, store.Shop.Skus[Cup], 1);
        (await QuoteAsync(b, store.Shop.ShopId, gold.Code)).GetProperty("platformDiscount").GetInt64().Should().Be(10_000);
    }

    [Fact]
    public async Task Member_spending_counts_the_goods_paid_without_shipping_and_less_what_was_refunded()
    {
        var store = await StoreAsync();
        var b = await BuyerAsync();
        await AddAsync(b, store.Shop.Skus[Kettle], 1);
        var code = await CompleteOrderAsync(store, b);
        var order = await factory.WithDbAsync(db => db.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Code == code));
        order.ShippingFee.Should().BeGreaterThan(order.ShippingDiscount, "đơn có trả phí vận chuyển");
        var goodsPaid = order.GrandTotal - (order.ShippingFee - order.ShippingDiscount);
        async Task<long> SpendAsync() => (await (await b.User.Client.GetAsync("/api/account/membership")).ReadEnvelopeAsync()).Data.GetProperty("spend").GetInt64();
        (await SpendAsync()).Should().Be(goodsPaid, "phí vận chuyển không phải chi tiêu mua hàng");

        var admin = await factory.ClientWithPermissionsAsync(Permissions.OrderIntervene);
        (await admin.PostAsJsonAsync($"/api/admin/orders/{code}/manual-refund", new
        {
            lines = new[] { new { orderItemId = order.Items[0].Id, quantity = 1 } }, amount = 50_000, platformBorne = true,
            reason = "Hàng giao thiếu phụ kiện, sàn bồi hoàn",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SpendAsync()).Should().Be(goodsPaid - 50_000, "phần đã hoàn không còn là chi tiêu");
    }

    // ---------- banners & campaigns ----------

    [Fact]
    public async Task Banners_and_campaign_pages_come_from_the_admin_and_only_marketing_admins_edit_them()
    {
        (await (await factory.CreateUserAsync()).Client.PostAsJsonAsync("/api/admin/marketing/banners", new { position = "HomeMain" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var admin = await factory.ClientWithPermissionsAsync(Permissions.MarketingManage);
        var title = $"Banner thử {Guid.NewGuid():N}"[..20];
        var saved = await admin.PostAsJsonAsync("/api/admin/marketing/banners", new
        {
            position = "HomeMain", title, imageUrl = "https://cdn.example/banner.webp", link = "/su-kien/sieu-sale",
            startAt = DateTimeOffset.UtcNow.AddMinutes(-1), endAt = DateTimeOffset.UtcNow.AddDays(1), sortOrder = 1, isActive = true,
        });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await admin.PostAsJsonAsync("/api/admin/marketing/banners", new
        {
            position = "HomeMain", title = "Xấu", imageUrl = "https://cdn.example/x.webp", link = "javascript:alert(1)",
            startAt = DateTimeOffset.UtcNow, endAt = DateTimeOffset.UtcNow.AddDays(1), sortOrder = 1, isActive = true,
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var home = (await (await factory.CreateClient().GetAsync("/api/home/banners")).ReadEnvelopeAsync()).Data;
        home.GetProperty("main").EnumerateArray().Should().Contain(x => x.Str("title") == title);

        var slug = $"sale-{Guid.NewGuid():N}"[..14];
        var campaign = await admin.PostAsJsonAsync("/api/admin/marketing/campaigns", new
        {
            name = "Siêu sale thử", slug, startAt = DateTimeOffset.UtcNow.AddMinutes(-1), endAt = DateTimeOffset.UtcNow.AddDays(1), isActive = true,
            blocks = new object[]
            {
                new { type = "Banner", title = "Đầu trang", imageUrl = "https://cdn.example/top.webp", link = "/" },
                new { type = "Vouchers", title = "Mã của sàn", voucherCodes = new[] { "SHOPHUB50" } },
                new { type = "Products", title = "Bình giữ nhiệt", keyword = "Bình", limit = 6 },
            },
        });
        campaign.StatusCode.Should().Be(HttpStatusCode.OK, await campaign.Content.ReadAsStringAsync());
        var page = (await (await factory.CreateClient().GetAsync($"/api/campaigns/{slug}")).ReadEnvelopeAsync()).Data;
        page.GetProperty("blocks").GetArrayLength().Should().Be(3);
        (await factory.CreateClient().GetAsync("/api/campaigns/khong-ton-tai")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Expired_xu_of_twelve_thousand_users_are_all_written_off_in_one_run_and_never_twice()
    {
        // 12 000 buyers (cloned from one test user, phones 08…) each with 100 xu that expired yesterday
        var template = await factory.CreateUserAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO iam.users
            SELECT (jsonb_populate_record(NULL::iam.users, to_jsonb(u) || jsonb_build_object(
                'id', gen_random_uuid(), 'phone', '08' || lpad((g + {Random.Shared.Next(0, 80_000_000)})::text, 8, '0'),
                'email', NULL, 'username', NULL, 'full_name', 'Xu hết hạn ' || {marker}))).*
            FROM iam.users u CROSS JOIN generate_series(1, 12000) g WHERE u.id = {template.Id}
            """));
        var at = DateTimeOffset.UtcNow.AddDays(-40);
        var expired = DateTimeOffset.UtcNow.AddDays(-1);
        var inserted = await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO promo.coin_ledger (id, user_id, delta, reason, ref_type, ref_id, expires_at, note, created_at)
            SELECT gen_random_uuid(), u.id, 100, 'CheckIn', NULL, NULL, {expired}, 'Điểm danh', {at}
            FROM iam.users u WHERE u.full_name = {"Xu hết hạn " + marker}
            """));
        inserted.Should().Be(12_000);

        using (var scope = factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<Application.Features.Marketing.CoinExpiryService>().RunAsync(CancellationToken.None))
                .Should().BeGreaterThanOrEqualTo(12_000, "mọi người có xu hết hạn đều được xử lý, không dừng ở 5.000");
        var written = await factory.WithDbAsync(db => (from c in db.CoinLedger
                                                       join u in db.Users on c.UserId equals u.Id
                                                       where u.FullName == "Xu hết hạn " + marker && c.Reason == CoinReason.Expired
                                                       select c.Delta).ToListAsync());
        written.Should().HaveCount(12_000).And.OnlyContain(d => d == -100);

        using (var scope = factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<Application.Features.Marketing.CoinExpiryService>().RunAsync(CancellationToken.None))
                .Should().Be(0, "lần chạy sau không xử lý lại người đã xong");
    }

    [Fact]
    public async Task Xu_given_back_after_their_credit_expired_are_not_spendable_and_the_next_run_writes_them_off()
    {
        // L124 / L126: 500 xu (expired yesterday) were spent on an order; the order is cancelled today
        var buyer = await factory.CreateUserAsync();
        var now = DateTimeOffset.UtcNow;
        await factory.WithDbAsync(async db =>
        {
            db.CoinLedger.Add(new CoinEntry(buyer.Id, 500, CoinReason.CheckIn, null, null, now.AddDays(-1), "Điểm danh", now.AddDays(-10)));
            db.CoinLedger.Add(new CoinEntry(buyer.Id, -500, CoinReason.CheckoutSpend, "checkout", Guid.NewGuid(), null, "Dùng xu", now.AddDays(-5)));
            await db.SaveChangesAsync();
            return 0;
        });
        async Task RunJobAsync()
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<Application.Features.Marketing.CoinExpiryService>().RunAsync(CancellationToken.None);
        }
        await RunJobAsync();   // nothing left to write off: the credit was fully spent

        await factory.WithDbAsync(async db =>
        {
            db.CoinLedger.Add(new CoinEntry(buyer.Id, 500, CoinReason.CheckoutRefund, "order", Guid.NewGuid(), null, "Hoàn xu đơn huỷ", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return 0;
        });
        async Task<long> BalanceAsync() =>
            (await (await buyer.Client.GetAsync("/api/account/coins")).ReadEnvelopeAsync()).Data.GetProperty("balance").GetInt64();

        (await BalanceAsync()).Should().Be(0, "xu trả lại vẫn mang hạn của khoản đã tiêu, khoản ấy hết hạn từ hôm qua");
        await RunJobAsync();
        var ledger = await factory.WithDbAsync(db => db.CoinLedger.Where(c => c.UserId == buyer.Id).SumAsync(c => c.Delta));
        ledger.Should().Be(0, "lượt chạy kế tiếp ghi xoá 500 xu ấy, sổ xu khớp số dư");
        (await BalanceAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_discount_not_started_yet_can_be_edited_and_its_price_programme_follows_but_a_running_one_cannot()
    {
        var store = await StoreAsync();
        var (kettle, cup) = (store.Shop.Skus[Kettle], store.Shop.Skus[Cup]);
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var created = await store.Staff.Client.PostAsJsonAsync(Url(store, "promotions"), new
        {
            type = "Discount", name = "Giảm giá tuần sau", startAt = start, endAt = start.AddDays(2), skus = new[] { new { skuId = kettle, price = 150_000 } },
        });
        var id = Guid.Parse((await created.ReadEnvelopeAsync()).Data.GetString()!);

        (await store.Staff.Client.PutAsJsonAsync(Url(store, $"promotions/{id}"), new
        {
            type = "Discount", name = "", startAt = start, endAt = start.AddDays(2), skus = new[] { new { skuId = cup, price = 40_000 } },
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "sửa cũng qua đủ các luật kiểm như khi tạo");
        var edit = await store.Staff.Client.PutAsJsonAsync(Url(store, $"promotions/{id}"), new
        {
            type = "Discount", name = "Giảm giá cốc", startAt = start.AddHours(2), endAt = start.AddDays(3), skus = new[] { new { skuId = cup, price = 40_000 } },
        });
        edit.StatusCode.Should().Be(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync());
        var promo = (await (await store.Staff.Client.GetAsync(Url(store, "promotions"))).ReadEnvelopeAsync()).Data.EnumerateArray().Single(p => p.Str("id") == id.ToString());
        promo.Str("name").Should().Be("Giảm giá cốc");
        promo.GetProperty("skus").EnumerateArray().Select(x => (x.Str("skuId"), x.GetProperty("price").GetInt64())).Should().Equal((cup.ToString(), 40_000L));
        var programs = await factory.WithDbAsync(db => db.PricePrograms.AsNoTracking().Where(p => p.RefId == id && p.IsActive).ToListAsync());
        var program = programs.Should().ContainSingle().Which;
        (program.SkuId, program.Price).Should().Be((cup, 40_000L));
        program.StartAt.Should().BeCloseTo(start.AddHours(2), TimeSpan.FromMilliseconds(1), "PostgreSQL giữ tới micro giây");

        // The kettle is free again: a shop flash sale on it in the old window is accepted
        (await store.Staff.Client.PostAsJsonAsync(Url(store, "flash-sales"), new
        {
            startAt = start.AddMinutes(10), endAt = start.AddHours(1), items = new[] { new { skuId = kettle, flashPrice = 120_000, quota = 5, perUserLimit = 1 } },
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Running already → no edit
        await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE promo.promotions SET start_at = now() - interval '1 hour' WHERE id = {id}"));
        var late = await store.Staff.Client.PutAsJsonAsync(Url(store, $"promotions/{id}"), new
        {
            type = "Discount", name = "Muộn", startAt = start, endAt = start.AddDays(3), skus = new[] { new { skuId = cup, price = 30_000 } },
        });
        late.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await late.Content.ReadAsStringAsync()).Should().Contain("đã bắt đầu");
    }

    [Fact]
    public async Task A_shop_flash_sale_not_started_yet_can_be_edited_but_a_running_one_cannot()
    {
        var store = await StoreAsync();
        var (kettle, cup) = (store.Shop.Skus[Kettle], store.Shop.Skus[Cup]);
        var start = DateTimeOffset.UtcNow.AddDays(2);
        var created = await store.Staff.Client.PostAsJsonAsync(Url(store, "flash-sales"), new
        {
            startAt = start, endAt = start.AddHours(2), items = new[] { new { skuId = kettle, flashPrice = 120_000, quota = 5, perUserLimit = 1 } },
        });
        var slotId = Guid.Parse((await created.ReadEnvelopeAsync()).Data.GetString()!);

        var edit = await store.Staff.Client.PutAsJsonAsync(Url(store, $"flash-sales/{slotId}"), new
        {
            startAt = start.AddHours(1), endAt = start.AddHours(4),
            items = new[] { new { skuId = kettle, flashPrice = 110_000, quota = 8, perUserLimit = 2 }, new { skuId = cup, flashPrice = 30_000, quota = 3, perUserLimit = 1 } },
        });
        edit.StatusCode.Should().Be(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync());
        var slot = (await (await store.Staff.Client.GetAsync(Url(store, "flash-sales"))).ReadEnvelopeAsync()).Data.EnumerateArray().Single(x => x.Str("id") == slotId.ToString());
        slot.GetProperty("items").EnumerateArray().Select(i => (i.Str("skuId"), i.GetProperty("flashPrice").GetInt64(), i.GetProperty("quota").GetInt32()))
            .Should().BeEquivalentTo([(kettle.ToString(), 110_000L, 8), (cup.ToString(), 30_000L, 3)]);
        var live = await factory.WithDbAsync(db => db.PricePrograms.AsNoTracking().Where(p => p.Kind == PriceProgramKind.ShopFlash && p.IsActive && p.ShopId == store.Shop.ShopId).ToListAsync());
        live.Select(p => (p.SkuId, p.Price)).Should().BeEquivalentTo([(kettle, 110_000L), (cup, 30_000L)], "giá cũ 120.000 không còn hiệu lực");
        live.Should().OnlyContain(p => Math.Abs((p.StartAt - start.AddHours(1)).TotalMilliseconds) < 1 && Math.Abs((p.EndAt - start.AddHours(4)).TotalMilliseconds) < 1);

        await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE promo.flash_sale_slots SET start_at = now() - interval '1 minute' WHERE id = {slotId}"));
        var late = await store.Staff.Client.PutAsJsonAsync(Url(store, $"flash-sales/{slotId}"), new
        {
            startAt = start, endAt = start.AddHours(2), items = new[] { new { skuId = kettle, flashPrice = 100_000, quota = 5, perUserLimit = 1 } },
        });
        late.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await late.Content.ReadAsStringAsync()).Should().Contain("đã bắt đầu");
    }

    [Fact]
    public async Task The_marketing_role_edits_the_curated_hot_keywords_on_its_own_screen_with_its_own_permission()
    {
        RoleCatalog.All.Single(r => r.Code == "MARKETING").Permissions.Should().Contain(Permissions.HotKeywordManage);
        var old = await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == ParameterKeys.SearchHotKeywords).Select(p => p.Value).SingleAsync());
        var marketing = await factory.ClientWithPermissionsAsync(Permissions.HotKeywordManage);
        var other = await factory.ClientWithPermissionsAsync(Permissions.VoucherManage);
        try
        {
            (await other.GetAsync("/api/admin/marketing/hot-keywords")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await marketing.GetAsync("/api/admin/marketing/hot-keywords")).StatusCode.Should().Be(HttpStatusCode.OK);

            var saved = await marketing.PutAsJsonAsync("/api/admin/marketing/hot-keywords", new { keywords = new[] { " Nồi cơm điện ", "Ốp lưng", "nồi cơm điện" } });
            saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            (await (await marketing.GetAsync("/api/admin/marketing/hot-keywords")).ReadEnvelopeAsync()).Data.EnumerateArray().Select(k => k.GetString())
                .Should().Equal(["Nồi cơm điện", "Ốp lưng"], "bỏ khoảng trắng, bỏ trùng (không phân biệt hoa thường)");
            var parameterId = await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == ParameterKeys.SearchHotKeywords).Select(p => p.Id).SingleAsync());
            (await factory.WithDbAsync(db => db.AuditLogs.CountAsync(a => a.Entity == "SystemParameter" && a.EntityId == parameterId.ToString())))
                .Should().BeGreaterThan(0, "sửa qua lệnh tham số nên có lịch sử thay đổi");

            (await marketing.PutAsJsonAsync("/api/admin/marketing/hot-keywords", new { keywords = Enumerable.Range(1, 11).Select(i => $"Từ {i}") }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest, "tối đa 10 từ khoá");
        }
        finally
        {
            await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == ParameterKeys.SearchHotKeywords).ExecuteUpdateAsync(u => u.SetProperty(p => p.Value, old)));
            factory.Services.GetRequiredService<ISystemParameters>().Invalidate(ParameterKeys.SearchHotKeywords);
        }
    }
}
