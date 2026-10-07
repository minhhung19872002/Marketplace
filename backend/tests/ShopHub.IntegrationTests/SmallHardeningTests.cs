using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Security;
using ShopHub.Domain.Promo;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>Phase 14 B7: the small permission and input gaps.</summary>
[Collection(ApiCollection.Name)]
public class SmallHardeningTests(ApiFactory factory)
{
    [Fact]
    public async Task Only_staff_who_may_withdraw_can_ask_for_the_finance_otp()
    {
        var store = await factory.CreateStoreAsync("79", products: [new("Ly Thuỷ Tinh", "Đèn Bàn", 50_000, 5, "Việt Nam")]);
        async Task<HttpClient> StaffAsync(params string[] permissions)
        {
            var staff = await factory.CreateUserAsync();
            await factory.WithDbAsync(async db =>
            {
                db.ShopStaff.Add(new Domain.Shops.ShopStaff(store.ShopId, staff.Id, Domain.Shops.ShopStaffRole.CustomerService, permissions));
                await db.SaveChangesAsync();
                return 0;
            });
            return staff.Client;
        }
        var viewer = await StaffAsync(ShopPermissions.FinanceView);
        (await viewer.PostAsync($"/api/seller/shops/{store.ShopId}/finance/otp", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "xem tài chính không đủ để thêm tài khoản ngân hàng");
        var withdrawer = await StaffAsync(ShopPermissions.FinanceView, ShopPermissions.FinanceWithdraw);
        (await withdrawer.PostAsync($"/api/seller/shops/{store.ShopId}/finance/otp", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void Descriptions_keep_only_the_platforms_own_images()
    {
        using var scope = factory.Services.CreateScope();
        var sanitizer = scope.ServiceProvider.GetRequiredService<IHtmlSanitizer>();
        var own = $"{factory.MediaBaseUrl}/sh-products/x_1200.webp";
        var html = sanitizer.Sanitize($"""<p>Ảnh <img src="http://tracker.test/pixel.png"><img src="https://cdn.ngoai.test/a.jpg"><img src="{own}"></p>""");
        html.Should().NotContain("tracker.test").And.NotContain("cdn.ngoai.test");
        html.Should().Contain(own);
    }

    [Fact]
    public async Task Voucher_codes_are_unique_whatever_their_case_in_the_database_itself()
    {
        var voucher = await factory.CreateVoucherAsync(VoucherOwner.Platform, null, VoucherType.Amount, value: 1_000);
        var lower = voucher.Code.ToLowerInvariant();
        var insert = async () => await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO promo.vouchers SELECT (jsonb_populate_record(NULL::promo.vouchers, to_jsonb(v) || jsonb_build_object('id', gen_random_uuid(), 'code', {lower}))).*
            FROM promo.vouchers v WHERE v.id = {voucher.Id}
            """));
        (await insert.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("ux_voucher_code");
    }

    [Fact]
    public async Task A_null_character_in_a_form_field_or_file_name_is_refused_at_the_door()
    {
        var admin = await factory.ClientWithPermissionsAsync(Permissions.FinanceReconcile);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(DateTimeOffset.UtcNow.AddDays(-1).ToString("O")), "from" },
            { new StringContent(DateTimeOffset.UtcNow.ToString("O")), "to" },
            { new StringContent("VnPay\0"), "source" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes("a,b\n")), "file", "sao-ke.csv" },
        };
        var res = await admin.PostAsync("/api/admin/finance/reconcile/gateway", form);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.ReadEnvelopeAsync()).Message.Should().Contain("U+0000");
    }
}
