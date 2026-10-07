using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>
/// Kênh Người Bán and admin pieces that were missing: product list filters, bulk actions and copy (III.3), voucher
/// results (III.5), the dashboard's to-dos / traffic / own low-stock threshold (III.2), guest cart cleanup (6.4) and the
/// administrative divisions screen (VI.8).
/// </summary>
[Collection(ApiCollection.Name)]
public class SellerToolsTests(ApiFactory factory)
{
    private async Task<HttpClient> SellerAsync(TestStore store)
    {
        var staff = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(store.ShopId, staff.Id, Domain.Shops.ShopStaffRole.Manager, Application.Security.ShopPermissions.All));
            await db.SaveChangesAsync();
        });
        return staff.Client;
    }

    [Fact]
    public async Task Products_filter_by_stock_and_price_act_in_bulk_and_copy_as_a_stockless_draft()
    {
        var store = await factory.CreateStoreAsync("01", products:
        [
            new("Bút Ít", "Đèn Bàn", 50_000, 2, "Việt Nam"), new("Bút Nhiều", "Đèn Bàn", 150_000, 200, "Việt Nam"), new("Bút Vừa", "Đèn Bàn", 90_000, 30, "Việt Nam"),
        ]);
        var seller = await SellerAsync(store);
        var url = $"/api/seller/shops/{store.ShopId}/products";
        async Task<List<string>> Names(string qs) =>
            (await (await seller.GetAsync($"{url}?{qs}")).ReadEnvelopeAsync()).Data.GetProperty("items").EnumerateArray()
            .Select(i => i.Str("name")).Select(n => n[..n.LastIndexOf(' ')]).OrderBy(n => n).ToList();
        (await Names("minStock=10&maxStock=100")).Should().Equal("Bút Vừa");
        (await Names("minPrice=80000&maxPrice=160000")).Should().Equal("Bút Nhiều", "Bút Vừa");
        (await seller.GetAsync($"{url}?minStock=10&maxStock=5")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Hide all three, one of them held by an order cannot be deleted later — each product answers for itself
        var ids = store.Products.Values.ToList();
        var hide = await seller.PostAsJsonAsync($"{url}/bulk-actions", new { productIds = ids, action = "Hide" });
        hide.StatusCode.Should().Be(HttpStatusCode.OK);
        (await hide.ReadEnvelopeAsync()).Data.EnumerateArray().Should().OnlyContain(r => r.GetProperty("ok").GetBoolean());
        (await factory.WithDbAsync(db => db.Products.Where(p => ids.Contains(p.Id)).Select(p => p.Status).ToListAsync())).Should().OnlyContain(s => s == ProductStatus.Hidden);
        await factory.WithDbAsync(db => db.Skus.Where(s => s.Id == store.Skus["Bút Vừa"]).ExecuteUpdateAsync(u => u.SetProperty(s => s.Reserved, 1)));
        var delete = (await (await seller.PostAsJsonAsync($"{url}/bulk-actions", new { productIds = ids.Append(Guid.NewGuid()), action = "Delete" })).ReadEnvelopeAsync()).Data;
        delete.EnumerateArray().Count(r => r.GetProperty("ok").GetBoolean()).Should().Be(2);
        delete.EnumerateArray().Where(r => !r.GetProperty("ok").GetBoolean()).Select(r => r.Str("error"))
            .Should().BeEquivalentTo(["Sản phẩm đang có đơn giữ hàng, chưa xoá được.", "Không tìm thấy sản phẩm."], "từng sản phẩm báo lỗi riêng; sản phẩm lạ là 404");
        (await seller.PostAsJsonAsync($"{url}/bulk-actions", new { productIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()), action = "Hide" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "có trần");

        // Copy: a draft with the same info and prices, no stock
        var copied = await seller.PostAsync($"{url}/{store.Products["Bút Vừa"]}/copy", null);
        copied.StatusCode.Should().Be(HttpStatusCode.OK);
        var copyId = Guid.Parse((await copied.ReadEnvelopeAsync()).Data.GetString()!);
        var copy = await factory.WithDbAsync(db => db.Products.AsNoTracking().Include(p => p.Skus).Include(p => p.Media).Include(p => p.Attributes).SingleAsync(p => p.Id == copyId));
        copy.Status.Should().Be(ProductStatus.Draft);
        copy.Name.Should().StartWith("Bản sao - Bút Vừa");
        copy.Skus.Should().ContainSingle().Which.Should().Match<Sku>(s => s.Stock == 0 && s.Price == 90_000);
        copy.Media.Should().NotBeEmpty();
        copy.Attributes.Should().NotBeEmpty();

        // Another shop's product cannot be copied (404)
        var other = await factory.CreateStoreAsync("01", products: [new("Bút Người Khác", "Đèn Bàn", 50_000, 2, "Việt Nam")]);
        (await seller.PostAsync($"{url}/{other.Products["Bút Người Khác"]}/copy", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Shop_vouchers_show_saves_uses_orders_and_sales_and_the_dashboard_its_to_dos_and_traffic()
    {
        var store = await factory.CreateStoreAsync("01", products: [new("Cốc Hiệu Quả", "Đèn Bàn", 100_000, 50, "Việt Nam")]);
        var seller = await SellerAsync(store);
        var voucher = await factory.CreateVoucherAsync(VoucherOwner.Shop, store.ShopId, VoucherType.Amount, value: 10_000);
        var buyer = await factory.CreateUserAsync();
        var addressId = await factory.AddAddressAsync(buyer.Id, "01");
        (await buyer.Client.PostAsync($"/api/account/vouchers/{voucher.Id}/claim", null)).EnsureSuccessStatusCode();
        (await buyer.Client.PostAsync($"/api/products/{store.Products["Cốc Hiệu Quả"]}/views", null)).EnsureSuccessStatusCode();
        (await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = store.Skus["Cốc Hiệu Quả"], quantity = 2 })).EnsureSuccessStatusCode();
        var request = new
        {
            addressId, shops = new[] { new { shopId = store.ShopId, voucherCode = voucher.Code, carrierCode = (string?)null, note = (string?)null } },
            platformVoucherCode = (string?)null, freeshipVoucherCode = (string?)null, useCoins = false, paymentMethod = "Cod",
        };
        var quote = (await (await buyer.Client.PostAsJsonAsync("/api/checkout/quote", request)).ReadEnvelopeAsync()).Data;
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { checkout = request, expectedGrandTotal = quote.GetProperty("grandTotal").GetInt64() }),
        };
        msg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        (await buyer.Client.SendAsync(msg)).StatusCode.Should().Be(HttpStatusCode.OK);

        var list = (await (await seller.GetAsync($"/api/seller/shops/{store.ShopId}/vouchers")).ReadEnvelopeAsync()).Data.GetProperty("items");
        var stats = list.EnumerateArray().Single(v => v.Str("id") == voucher.Id.ToString()).GetProperty("stats");
        stats.GetProperty("claims").GetInt32().Should().Be(1);
        stats.GetProperty("uses").GetInt32().Should().Be(1);
        stats.GetProperty("orders").GetInt32().Should().Be(1);
        stats.GetProperty("sales").GetInt64().Should().Be(200_000 - 10_000, "tiền hàng sau giảm giá của shop");

        // Dashboard: own low-stock threshold, traffic and conversion, returns waiting
        (await seller.PutAsJsonAsync($"/api/seller/shops/{store.ShopId}/low-stock-threshold", new { units = 60 })).EnsureSuccessStatusCode();
        var d = (await (await seller.GetAsync($"/api/seller/shops/{store.ShopId}/dashboard")).ReadEnvelopeAsync()).Data;
        d.GetProperty("lowStockThreshold").GetInt32().Should().Be(60);
        d.GetProperty("lowStockSkus").GetInt32().Should().Be(1, "48 khả dụng ≤ ngưỡng 60 của shop");
        var today = d.GetProperty("today");
        today.GetProperty("views").GetInt32().Should().Be(1);
        today.GetProperty("visitors").GetInt32().Should().Be(1);
        today.GetProperty("conversionBp").GetInt64().Should().Be(10_000);
        d.GetProperty("returnsPending").GetInt32().Should().Be(0);
        (await seller.PutAsJsonAsync($"/api/seller/shops/{store.ShopId}/low-stock-threshold", new { units = -1 })).StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Old_guest_carts_are_cleaned_and_signed_in_carts_kept()
    {
        var store = await factory.CreateStoreAsync("01", products: [new("Khăn Giỏ Cũ", "Đèn Bàn", 30_000, 50, "Việt Nam")]);
        var sku = store.Skus["Khăn Giỏ Cũ"];
        var buyer = await factory.CreateUserAsync();
        var (oldGuest, newGuest) = await factory.WithDbAsync(async db =>
        {
            var old = Cart.ForGuest($"g-{Guid.NewGuid():N}", DateTimeOffset.UtcNow.AddDays(-40));
            old.Add(sku, 1, 30_000, 100, DateTimeOffset.UtcNow.AddDays(-40));
            var fresh = Cart.ForGuest($"g-{Guid.NewGuid():N}", DateTimeOffset.UtcNow.AddDays(-2));
            fresh.Add(sku, 1, 30_000, 100, DateTimeOffset.UtcNow.AddDays(-2));
            var mine = Cart.ForUser(buyer.Id, DateTimeOffset.UtcNow.AddDays(-90));
            mine.Add(sku, 1, 30_000, 100, DateTimeOffset.UtcNow.AddDays(-90));
            db.Carts.AddRange(old, fresh, mine);
            await db.SaveChangesAsync();
            // Saving stamps "now": put the dates back as they were
            await db.Carts.Where(c => c.Id == old.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.UpdatedAt, DateTimeOffset.UtcNow.AddDays(-40)));
            await db.Carts.Where(c => c.Id == mine.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.UpdatedAt, DateTimeOffset.UtcNow.AddDays(-90)));
            return (old.Id, fresh.Id);
        });

        using (var scope = factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<ShopHub.Infrastructure.Jobs.CartCleanupJob>().RunAsync(CancellationToken.None)).Should().BeGreaterThan(0);
        var left = await factory.WithDbAsync(db => db.Carts.Where(c => c.Id == oldGuest || c.Id == newGuest || c.UserId == buyer.Id).Select(c => c.Id).ToListAsync());
        left.Should().NotContain(oldGuest).And.Contain(newGuest).And.HaveCount(2, "giỏ của người đã đăng nhập không bao giờ bị dọn");
        (await factory.WithDbAsync(db => db.CartItems.AnyAsync(i => i.CartId == oldGuest))).Should().BeFalse();
    }

    [Fact]
    public async Task Admins_add_and_rename_administrative_units_without_changing_codes()
    {
        var admin = await factory.ClientWithPermissionsAsync(Application.Security.Permissions.ContentManage);
        var provinces = (await (await admin.GetAsync("/api/admin/divisions")).ReadEnvelopeAsync()).Data;
        provinces.EnumerateArray().Should().Contain(p => p.Str("code") == "01" && p.GetProperty("childCount").GetInt32() > 0);
        var district = (await (await admin.GetAsync("/api/admin/divisions?parent=01")).ReadEnvelopeAsync()).Data[0].Str("code");

        var code = $"9{Random.Shared.Next(10_000_000, 99_999_999)}";
        (await admin.PostAsJsonAsync("/api/admin/divisions", new { code, name = "Phường Thử Mới", parentCode = district })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/admin/divisions", new { code, name = "Trùng mã", parentCode = district })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PutAsJsonAsync($"/api/admin/divisions/{code}", new { name = "Phường Thử Đổi Tên" })).EnsureSuccessStatusCode();
        var ward = await factory.WithDbAsync(db => db.AdminDivisions.AsNoTracking().SingleAsync(d => d.Code == code));
        ward.Should().Match<Domain.Iam.AdminDivision>(w => w.Name == "Phường Thử Đổi Tên" && w.Level == Domain.Iam.AdminDivisionLevel.Ward && w.ParentCode == district);
        (await admin.PostAsJsonAsync("/api/admin/divisions", new { code = $"{code}1"[..10], name = "Dưới phường", parentCode = code }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "phường / xã không có cấp dưới");
        (await factory.WithDbAsync(db => db.AuditLogs.AnyAsync(l => l.Entity == "AdminDivision" && l.EntityId == code))).Should().BeTrue("mọi thay đổi vào nhật ký");
        (await factory.CreateClient().PostAsJsonAsync("/api/admin/divisions", new { code = "999", name = "x", parentCode = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
