using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Domain.Promo;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>
/// III.5 / VI.6 — shops put products forward for a platform campaign (not only Flash Sale), the platform approves, the
/// landing page shows the approved ones; penalty-banned shops, other shops' products and ended campaigns are refused.
/// </summary>
[Collection(ApiCollection.Name)]
public class CampaignRegistrationTests(ApiFactory factory)
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
    public async Task Shops_register_products_the_platform_approves_and_the_campaign_page_shows_only_approved_ones()
    {
        var store = await factory.CreateStoreAsync("01", products:
        [
            new("Nón Chiến Dịch A", "Đèn Bàn", 90_000, 10, "Việt Nam"), new("Nón Chiến Dịch B", "Đèn Bàn", 80_000, 10, "Việt Nam"),
        ]);
        var other = await factory.CreateStoreAsync("01", products: [new("Nón Shop Khác", "Đèn Bàn", 70_000, 10, "Việt Nam")]);
        var seller = await SellerAsync(store);
        var slug = $"ngay-hoi-{Guid.NewGuid():N}"[..20];
        var campaignId = await factory.WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var c = new Campaign("Ngày hội thử", slug, now.AddMinutes(-5), now.AddDays(7),
                [new(CampaignBlockType.Registered, "Sản phẩm tham gia", null, null, null, null, null, null, 24)], now);
            db.Campaigns.Add(c);
            await db.SaveChangesAsync();
            return c.Id;
        });
        var url = $"/api/seller/shops/{store.ShopId}/marketing";

        var open = (await (await seller.GetAsync($"{url}/campaigns")).ReadEnvelopeAsync()).Data;
        open.EnumerateArray().Should().Contain(c => c.Str("id") == campaignId.ToString());

        var a = store.Products["Nón Chiến Dịch A"];
        var b = store.Products["Nón Chiến Dịch B"];
        (await seller.PostAsJsonAsync($"{url}/campaigns/{campaignId}/registrations", new { productIds = new[] { a, b } })).EnsureSuccessStatusCode();
        (await seller.PostAsJsonAsync($"{url}/campaigns/{campaignId}/registrations", new { productIds = new[] { other.Products["Nón Shop Khác"] } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "sản phẩm của shop khác");
        // Registering the same product twice keeps one row
        (await (await seller.PostAsJsonAsync($"{url}/campaigns/{campaignId}/registrations", new { productIds = new[] { a } })).ReadEnvelopeAsync()).Data
            .GetInt32().Should().Be(0);

        var admin = await factory.ClientWithPermissionsAsync(Application.Security.Permissions.MarketingManage);
        var pending = (await (await admin.GetAsync($"/api/admin/marketing/campaigns/{campaignId}/registrations?status=Pending")).ReadEnvelopeAsync()).Data;
        var regA = pending.GetProperty("items").EnumerateArray().Single(r => r.Str("productId") == a.ToString()).Str("id");
        var regB = pending.GetProperty("items").EnumerateArray().Single(r => r.Str("productId") == b.ToString()).Str("id");
        (await admin.PostAsJsonAsync($"/api/admin/marketing/campaigns/{campaignId}/registrations/decisions",
            new { registrationIds = new[] { regA }, approve = true, reason = (string?)null })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/admin/marketing/campaigns/{campaignId}/registrations/decisions",
            new { registrationIds = new[] { regB }, approve = false, reason = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "từ chối cần lý do");
        (await admin.PostAsJsonAsync($"/api/admin/marketing/campaigns/{campaignId}/registrations/decisions",
            new { registrationIds = new[] { regB }, approve = false, reason = "Giá chưa đủ hấp dẫn" })).EnsureSuccessStatusCode();

        var page = (await (await factory.CreateClient().GetAsync($"/api/campaigns/{slug}")).ReadEnvelopeAsync()).Data;
        page.GetProperty("blocks")[0].GetProperty("products").EnumerateArray().Select(p => p.Str("id")).Should().Equal(a.ToString());

        // The shop sees why B was refused, puts it forward again → pending once more
        var mine = (await (await seller.GetAsync($"{url}/campaigns/{campaignId}/registrations")).ReadEnvelopeAsync()).Data;
        mine.EnumerateArray().Single(r => r.Str("productId") == b.ToString()).Str("rejectReason").Should().Be("Giá chưa đủ hấp dẫn");
        (await (await seller.PostAsJsonAsync($"{url}/campaigns/{campaignId}/registrations", new { productIds = new[] { b } })).ReadEnvelopeAsync()).Data
            .GetInt32().Should().Be(1);
        (await factory.WithDbAsync(db => db.CampaignRegistrations.CountAsync(r => r.CampaignId == campaignId))).Should().Be(2);

        // Another shop's staff cannot withdraw this shop's registration (404)
        var stranger = await SellerAsync(other);
        (await stranger.DeleteAsync($"/api/seller/shops/{other.ShopId}/marketing/campaign-registrations/{regA}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Penalty_banned_shops_and_campaigns_without_a_registration_block_are_refused()
    {
        var store = await factory.CreateStoreAsync("01", products: [new("Nón Bị Cấm", "Đèn Bàn", 90_000, 10, "Việt Nam")]);
        var seller = await SellerAsync(store);
        var (open, closed) = await factory.WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var withBlock = new Campaign("Có nhận", $"nhan-{Guid.NewGuid():N}"[..18], now.AddMinutes(-5), now.AddDays(7),
                [new(CampaignBlockType.Registered, null, null, null, null, null, null, null, null)], now);
            var noBlock = new Campaign("Không nhận", $"khong-{Guid.NewGuid():N}"[..18], now.AddMinutes(-5), now.AddDays(7),
                [new(CampaignBlockType.FlashSale, null, null, null, null, null, null, null, null)], now);
            db.Campaigns.AddRange(withBlock, noBlock);
            await db.SaveChangesAsync();
            return (withBlock.Id, noBlock.Id);
        });
        var url = $"/api/seller/shops/{store.ShopId}/marketing/campaigns";
        var product = store.Products["Nón Bị Cấm"];
        (await seller.PostAsJsonAsync($"{url}/{closed}/registrations", new { productIds = new[] { product } })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var banAt = await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == Application.SystemConfig.ParameterKeys.ShopPenaltyCampaignBanPoints)
            .Select(p => p.Value).SingleAsync());
        await factory.WithDbAsync(async db =>
        {
            db.ShopPenalties.Add(new Domain.Shops.ShopPenalty(store.ShopId, int.Parse(banAt), "Thử", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30)));
            await db.SaveChangesAsync();
        });
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Application.Features.Seller.ShopPenaltyService>()
                .RecomputeAsync(store.ShopId, CancellationToken.None);
        var refused = await seller.PostAsJsonAsync($"{url}/{open}/registrations", new { productIds = new[] { product } });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("điểm phạt");
    }
}
