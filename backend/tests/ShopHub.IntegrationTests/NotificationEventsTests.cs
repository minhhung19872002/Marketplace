using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Features.Chat;
using ShopHub.Domain.Engage;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>
/// Spec VII events that were missing: payment failed, a product locked (in-app to the shop, channels by settings),
/// a wishlisted product back in stock — and promotion notifications capped at one a day per person.
/// </summary>
[Collection(ApiCollection.Name)]
public class NotificationEventsTests(ApiFactory factory)
{
    private async Task DispatchAllAsync()
    {
        for (var i = 0; i < 50 && (await factory.DispatchOutboxAsync()).Processed > 0; i++) { }
    }

    private async Task<int> RemindersAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ReminderService>().RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_failed_online_payment_tells_the_buyer_who_can_pay_again()
    {
        var store = await factory.CreateStoreAsync("01", products: [new("Ly Thanh Toán", "Đèn Bàn", 80_000, 10, "Việt Nam")]);
        var buyer = await factory.CreateUserAsync();
        var addressId = await factory.AddAddressAsync(buyer.Id, "01");
        (await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = store.Skus["Ly Thanh Toán"], quantity = 1 })).EnsureSuccessStatusCode();
        var request = new
        {
            addressId, shops = new[] { new { shopId = store.ShopId, voucherCode = (string?)null, carrierCode = (string?)null, note = (string?)null } },
            platformVoucherCode = (string?)null, freeshipVoucherCode = (string?)null, useCoins = false, paymentMethod = "Simulated",
        };
        var quote = (await (await buyer.Client.PostAsJsonAsync("/api/checkout/quote", request)).ReadEnvelopeAsync()).Data;
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { checkout = request, expectedGrandTotal = quote.GetProperty("grandTotal").GetInt64() }),
        };
        msg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var placed = (await (await buyer.Client.SendAsync(msg)).ReadEnvelopeAsync()).Data;
        var code = placed.GetProperty("orders")[0].Str("code");
        (await factory.CreateClient().PostAsync($"/api/payments/simulated/{placed.GetProperty("payment").Str("paymentId")}/fail", null)).EnsureSuccessStatusCode();
        await DispatchAllAsync();

        var n = await factory.WithDbAsync(db => db.Notifications.AsNoTracking().Where(x => x.UserId == buyer.Id && x.Title == "Thanh toán không thành công").ToListAsync());
        n.Should().ContainSingle();
        n[0].Body.Should().Contain(code).And.Contain("thanh toán lại");
        n[0].Link.Should().Be($"/tai-khoan/don-mua/{code}");
    }

    [Fact]
    public async Task A_locked_product_is_an_in_app_notice_to_the_owner_and_product_staff_and_sms_only_for_who_asked()
    {
        var store = await factory.CreateStoreAsync("01", products: [new("Đèn Vi Phạm", "Đèn Bàn", 80_000, 10, "Việt Nam")]);
        var product = store.Products["Đèn Vi Phạm"];
        var staff = await factory.CreateUserAsync();
        var cashier = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(store.ShopId, staff.Id, Domain.Shops.ShopStaffRole.Warehouse, [Application.Security.ShopPermissions.ProductView]));
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(store.ShopId, cashier.Id, Domain.Shops.ShopStaffRole.CustomerService, [Application.Security.ShopPermissions.ChatManage]));
            // The staff member wants SMS for "Hoạt động"
            db.NotificationPrefs.Add(new NotificationPref(staff.Id, NotificationCategory.Activity, NotificationChannel.Sms, true));
            await db.SaveChangesAsync();
        });
        var admin = await factory.ClientWithPermissionsAsync(Application.Security.Permissions.ProductBan);
        (await admin.PostAsJsonAsync($"/api/admin/products/{product}/ban", new { reason = "Hàng giả thương hiệu" })).StatusCode.Should().Be(HttpStatusCode.OK);
        await DispatchAllAsync();
        await DispatchAllAsync();

        var got = await factory.WithDbAsync(db => db.Notifications.AsNoTracking().Where(x => x.RefId == product && x.Title == "Sản phẩm bị khoá")
            .Select(x => new { x.UserId, x.Body, x.Category }).ToListAsync());
        got.Select(x => x.UserId).Should().BeEquivalentTo([store.OwnerId, staff.Id], "chủ shop và nhân viên xem sản phẩm, không phải CSKH");
        got.Should().OnlyContain(x => x.Category == NotificationCategory.Activity && x.Body.Contains("Hàng giả thương hiệu"));
        var phones = await factory.WithDbAsync(db => db.Users.Where(u => u.Id == staff.Id || u.Id == store.OwnerId).Select(u => new { u.Id, u.Phone }).ToListAsync());
        var sms = await factory.WithDbAsync(db => db.SimulatedSms.AsNoTracking().Where(s => s.Content.Contains("Sản phẩm bị khoá")).Select(s => s.To).ToListAsync());
        sms.Should().Contain(phones.Single(p => p.Id == staff.Id).Phone!).And.NotContain(phones.Single(p => p.Id == store.OwnerId).Phone ?? "-");
    }

    [Fact]
    public async Task A_voucher_about_to_expire_is_reminded_even_after_todays_promotion_and_promotions_can_be_turned_off_in_the_app()
    {
        var buyer = await factory.CreateUserAsync();
        var voucher = await factory.CreateVoucherAsync(Domain.Promo.VoucherOwner.Platform, null, Domain.Promo.VoucherType.Amount, value: 5_000);
        await factory.WithDbAsync(db => db.Vouchers.Where(v => v.Id == voucher.Id).ExecuteUpdateAsync(u => u.SetProperty(v => v.EndAt, DateTimeOffset.UtcNow.AddHours(5))));
        (await buyer.Client.PostAsync($"/api/account/vouchers/{voucher.Id}/claim", null)).EnsureSuccessStatusCode();
        await factory.WithDbAsync(async db =>
        {
            db.Notifications.Add(new Notification(buyer.Id, NotificationCategory.Promotion, "Ngày hội", "Giảm giá", "/", "broadcast", Guid.NewGuid(),
                DateTimeOffset.UtcNow, $"broadcast:{Guid.NewGuid()}", Application.Common.VietnamTime.Today(DateTimeOffset.UtcNow)));
            await db.SaveChangesAsync();
        });
        await RemindersAsync();
        var reminder = await factory.WithDbAsync(db => db.Notifications.AsNoTracking()
            .SingleOrDefaultAsync(n => n.UserId == buyer.Id && n.DedupeKey == $"voucher-expiring:{voucher.Id}"));
        reminder.Should().NotBeNull("nhắc việc không chung hạn mức với tin quảng cáo");
        reminder!.Category.Should().Be(NotificationCategory.Wallet);

        // Promotions in the app can be turned off; orders / wallet / account cannot
        var prefs = (await (await buyer.Client.GetAsync("/api/notifications/prefs")).ReadEnvelopeAsync()).Data.GetProperty("prefs").EnumerateArray()
            .Where(p => p.Str("channel") == "InApp").ToDictionary(p => p.Str("category"), p => p.GetProperty("locked").GetBoolean());
        prefs["Promotion"].Should().BeFalse();
        prefs["Order"].Should().BeTrue();
        (await buyer.Client.PutAsJsonAsync("/api/notifications/prefs", new
        {
            prefs = new[] { new { category = "Promotion", channel = "InApp", enabled = false }, new { category = "Order", channel = "InApp", enabled = false } },
        })).EnsureSuccessStatusCode();
        var after = (await (await buyer.Client.GetAsync("/api/notifications/prefs")).ReadEnvelopeAsync()).Data.GetProperty("prefs").EnumerateArray()
            .Where(p => p.Str("channel") == "InApp").ToDictionary(p => p.Str("category"), p => p.GetProperty("enabled").GetBoolean());
        after["Promotion"].Should().BeFalse();
        after["Order"].Should().BeTrue("đơn hàng luôn hiện trong ứng dụng");
    }

    [Fact]
    public async Task A_wishlisted_product_back_in_stock_is_told_once_and_promotions_are_capped_at_one_a_day()
    {
        var store = await factory.CreateStoreAsync("01", products: [new("Gối Hết Hàng", "Đèn Bàn", 80_000, 0, "Việt Nam")]);
        var product = store.Products["Gối Hết Hàng"];
        var fan = await factory.CreateUserAsync();
        var busy = await factory.CreateUserAsync();
        foreach (var u in new[] { fan, busy })
            (await u.Client.PostAsync($"/api/account/wishlist/{product}", null)).EnsureSuccessStatusCode();
        // "busy" already had a promotion today (e.g. a broadcast)
        await factory.WithDbAsync(async db =>
        {
            db.Notifications.Add(new Notification(busy.Id, NotificationCategory.Promotion, "Ngày hội", "Giảm giá", "/", "broadcast", Guid.NewGuid(), DateTimeOffset.UtcNow,
                $"broadcast:{Guid.NewGuid()}", Application.Common.VietnamTime.Today(DateTimeOffset.UtcNow)));
            await db.SaveChangesAsync();
        });

        await RemindersAsync();
        (await factory.WithDbAsync(db => db.Wishlists.Where(w => w.ProductId == product).Select(w => w.SoldOutSeenAt).ToListAsync()))
            .Should().OnlyContain(x => x != null, "hết hàng được ghi nhận");

        await factory.WithDbAsync(db => db.Skus.Where(s => s.Id == store.Skus["Gối Hết Hàng"]).ExecuteUpdateAsync(u => u.SetProperty(s => s.Stock, 5)));
        await RemindersAsync();
        await RemindersAsync();
        var told = await factory.WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.RefId == product && n.Title == "Sản phẩm yêu thích đã có hàng lại")
            .Select(n => n.UserId).ToListAsync());
        told.Should().Equal([fan.Id], "một lần cho người thích; người đã nhận khuyến mãi hôm nay chờ hôm sau");
        (await factory.WithDbAsync(db => db.Wishlists.Where(w => w.ProductId == product && w.UserId == busy.Id).Select(w => w.SoldOutSeenAt).SingleAsync()))
            .Should().NotBeNull("người chưa được báo vẫn chờ báo");
    }
}
