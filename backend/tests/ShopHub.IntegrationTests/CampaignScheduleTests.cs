using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Features.Marketing;
using ShopHub.Domain.Promo;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>
/// L206: everything tied to a campaign ends with it. The 10.10 popup had its own schedule of a year and would have announced
/// the sale in November; a banner leading to /su-kien/{slug} now shows only while that campaign runs — not before it starts,
/// not after it ends, never for a campaign that does not exist.
/// </summary>
[Collection(ApiCollection.Name)]
public class CampaignScheduleTests(ApiFactory factory)
{
    [Fact]
    public async Task A_banner_leading_to_a_campaign_shows_only_while_the_campaign_runs()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (ended, running, ahead) = ($"da-qua-{tag}", $"dang-chay-{tag}", $"sap-toi-{tag}");
        await factory.WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            List<CampaignBlock> blocks = [new(CampaignBlockType.Registered, "Sản phẩm", null, null, null, null, null, null, 12)];
            db.Campaigns.Add(new Campaign("Đã qua", ended, now.AddDays(-10), now.AddMinutes(-1), blocks, now));
            db.Campaigns.Add(new Campaign("Đang chạy", running, now.AddHours(-1), now.AddDays(1), blocks, now));
            db.Campaigns.Add(new Campaign("Sắp tới", ahead, now.AddDays(5), now.AddDays(10), blocks, now));
            // Each banner's own schedule runs for a year, like the seeded 10.10 popup did
            foreach (var slug in new[] { ended, running, ahead, $"khong-co-{tag}" })
                db.Banners.Add(new Banner(BannerPosition.Popup, $"Popup {slug}", "https://example.invalid/p.webp", $"/su-kien/{slug}", now.AddMinutes(-5),
                    now.AddYears(1), -1_000, null, now, hasTextInImage: true));
            db.Banners.Add(new Banner(BannerPosition.HomeMain, $"Hero {ended}", "https://example.invalid/h.webp", $"/su-kien/{ended}?tab=1", now.AddMinutes(-5),
                now.AddYears(1), -1_000, null, now, hasTextInImage: true));
            await db.SaveChangesAsync();
        });

        using var scope = factory.Services.CreateScope();
        var home = await scope.ServiceProvider.GetRequiredService<MediatR.ISender>().Send(new HomeBannersQuery());
        home.Popup!.Link.Should().Be($"/su-kien/{running}", "popup của chiến dịch đã qua / chưa tới / không tồn tại không được hiện");
        home.Main.Should().NotContain(b => b.Link.Contains(ended));
    }

    [Theory]
    [InlineData("/su-kien/sieu-sale-10-10?tab=1", "sieu-sale-10-10")]
    [InlineData("/su-kien/sieu-sale-11-11", "sieu-sale-11-11")]
    [InlineData("/flash-sale", null)]
    public void The_campaign_of_a_link_is_read_from_its_path(string link, string? slug) => CampaignBanners.SlugOf(link).Should().Be(slug);
}
