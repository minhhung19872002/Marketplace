using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShopHub.Domain.Iam;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>Đăng nhập Google (I.1): verified ID tokens only, consent for a new account, linking by verified e-mail.</summary>
[Collection(ApiCollection.Name)]
public class GoogleLoginTests(ApiFactory factory)
{
    private Task<HttpResponseMessage> SignInAsync(string idToken, bool acceptTerms = false) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/google", new { idToken, device = "Chrome thử", acceptTerms });

    [Fact]
    public async Task A_new_google_account_needs_consent_then_signs_in_to_the_same_account_every_time()
    {
        var providers = (await (await factory.CreateClient().GetAsync("/api/auth/providers")).ReadEnvelopeAsync()).Data;
        providers.Str("googleClientId").Should().Be(FakeGoogle.ClientId);

        var sub = Guid.NewGuid().ToString("N");
        var email = $"g{sub[..10]}@gmail.test";
        var token = FakeGoogle.IdToken(sub, email);

        var noConsent = await SignInAsync(token);
        noConsent.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await noConsent.ReadEnvelopeAsync()).Message.Should().Contain("Điều khoản");

        var first = await SignInAsync(token, acceptTerms: true);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var userId = Guid.Parse((await first.ReadEnvelopeAsync()).Data.GetProperty("user").Str("id"));

        // Again (a later token of the same Google account, even if its e-mail changed): the same ShopHub account
        var again = await SignInAsync(FakeGoogle.IdToken(sub, $"doi-{email}"));
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        Guid.Parse((await again.ReadEnvelopeAsync()).Data.GetProperty("user").Str("id")).Should().Be(userId);
        (await factory.WithDbAsync(db => db.UserIdentities.CountAsync(i => i.UserId == userId))).Should().Be(1);
        (await factory.WithDbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.ConsentedAt).SingleAsync())).Should().NotBeNull();
    }

    [Fact]
    public async Task A_google_account_is_told_to_set_a_password_by_email_otp_and_can_then_delete_itself()
    {
        // L143: an account made by Google has a random password nobody knows, so "xoá tài khoản" (password) was a dead end
        var sub = Guid.NewGuid().ToString("N");
        var email = $"x{sub[..10]}@gmail.test";
        var signedIn = (await (await SignInAsync(FakeGoogle.IdToken(sub, email), acceptTerms: true)).ReadEnvelopeAsync()).Data;
        var client = factory.Authorized(signedIn.Str("accessToken"));
        var me = (await (await client.GetAsync("/api/account/me")).ReadEnvelopeAsync()).Data;
        me.GetProperty("hasGoogle").GetBoolean().Should().BeTrue("trang tài khoản dựa vào cờ này để chỉ lối đặt mật khẩu");

        // The way the page points to: "Quên mật khẩu" with the account's e-mail
        var anon = factory.CreateClient();
        (await anon.PostAsJsonAsync("/api/auth/forgot-password", new { target = email })).EnsureSuccessStatusCode();
        await factory.DispatchOutboxAsync();
        var mail = factory.Emails.Sent.Last(m => m.To == email);
        var code = System.Text.RegularExpressions.Regex.Match(mail.Html, @"\b\d{6}\b").Value;
        var ticket = (await (await anon.PostAsJsonAsync("/api/auth/otp/verify", new { target = email, purpose = "ResetPassword", code }))
            .ReadEnvelopeAsync()).Data.Str("ticket");
        (await anon.PostAsJsonAsync("/api/auth/reset-password", new { target = email, ticket, newPassword = "GoogleDat2026" })).EnsureSuccessStatusCode();

        var login = (await (await anon.PostAsJsonAsync("/api/auth/login", new { identifier = email, password = "GoogleDat2026" })).ReadEnvelopeAsync()).Data;
        var again = factory.Authorized(login.Str("accessToken"));
        (await again.PostAsJsonAsync("/api/account/delete", new { password = "GoogleDat2026" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_existing_account_with_the_verified_email_is_linked_not_duplicated_and_a_locked_one_stays_out()
    {
        var email = $"co-san-{Guid.NewGuid():N}"[..20] + "@gmail.test";
        var existing = await factory.WithDbAsync(async db =>
        {
            var u = User.Register(null, email, "x", "Đã Có Tài Khoản", DateTimeOffset.UtcNow);
            db.Users.Add(u);
            await db.SaveChangesAsync();
            return u.Id;
        });
        var sub = Guid.NewGuid().ToString("N");
        var res = await SignInAsync(FakeGoogle.IdToken(sub, email.ToUpperInvariant()));
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        Guid.Parse((await res.ReadEnvelopeAsync()).Data.GetProperty("user").Str("id")).Should().Be(existing);

        await factory.WithDbAsync(async db =>
        {
            var u = await db.Users.SingleAsync(x => x.Id == existing);
            u.Lock("Kiểm thử");
            await db.SaveChangesAsync();
        });
        (await SignInAsync(FakeGoogle.IdToken(sub, email))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Forged_expired_foreign_or_unverified_tokens_are_refused()
    {
        var sub = Guid.NewGuid().ToString("N");
        var email = $"x{sub[..8]}@gmail.test";
        (await SignInAsync(FakeGoogle.IdToken(sub, email, signWith: FakeGoogle.OtherKey()), true)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "signed by someone else");
        (await SignInAsync(FakeGoogle.IdToken(sub, email, audience: "another-app.apps.googleusercontent.com"), true)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SignInAsync(FakeGoogle.IdToken(sub, email, issuer: "https://evil.example"), true)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SignInAsync(FakeGoogle.IdToken(sub, email, expires: DateTime.UtcNow.AddMinutes(-10)), true)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SignInAsync(FakeGoogle.IdToken(sub, email, emailVerified: false), true)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SignInAsync("khong-phai-jwt", true)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await factory.WithDbAsync(db => db.Users.AnyAsync(u => u.Email == email))).Should().BeFalse();
    }

    [Fact]
    public async Task Deleting_an_account_removes_its_google_link_bank_accounts_and_devices_so_google_sign_in_works_again_and_again()
    {
        var user = await factory.CreateUserAsync();
        var sub = Guid.NewGuid().ToString("N");
        var email = $"x{sub[..10]}@gmail.test";
        await factory.WithDbAsync(db => db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Email, email)));
        (await SignInAsync(FakeGoogle.IdToken(sub, email))).StatusCode.Should().Be(HttpStatusCode.OK, "liên kết theo email đã xác minh");
        await factory.WithDbAsync(async db =>
        {
            db.BankAccounts.Add(new Domain.Finance.BankAccount(user.Id, "TCB", "ma-hoa-so-tai-khoan", "6789", "NGUYEN VAN XOA", DateTimeOffset.UtcNow));
            db.DeviceTokens.Add(new Domain.Engage.DeviceToken(user.Id, "android", $"fcm-{sub}", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return 0;
        });

        (await user.Client.PostAsJsonAsync("/api/account/delete", new { password = ApiFactory.DefaultPassword })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await factory.WithDbAsync(db => db.UserIdentities.CountAsync(i => i.UserId == user.Id))).Should().Be(0);
        (await factory.WithDbAsync(db => db.DeviceTokens.CountAsync(d => d.UserId == user.Id))).Should().Be(0);
        var bank = await factory.WithDbAsync(db => db.BankAccounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(b => b.UserId == user.Id));
        bank.AccountNoEncrypted.Should().BeEmpty();
        bank.AccountName.Should().NotContain("NGUYEN");
        bank.DeletedAt.Should().NotBeNull();

        // The same Google account later: a fresh ShopHub account, and the second sign-in finds it (no e-mail collision)
        var first = await SignInAsync(FakeGoogle.IdToken(sub, email), acceptTerms: true);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var fresh = Guid.Parse((await first.ReadEnvelopeAsync()).Data.GetProperty("user").Str("id"));
        fresh.Should().NotBe(user.Id);
        var second = await SignInAsync(FakeGoogle.IdToken(sub, email));
        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        Guid.Parse((await second.ReadEnvelopeAsync()).Data.GetProperty("user").Str("id")).Should().Be(fresh);
    }

    [Fact]
    public async Task A_google_link_left_on_a_deleted_account_is_dropped_at_the_next_sign_in()
    {
        // Data written before the fix: the account was anonymised but its Google link stayed
        var user = await factory.CreateUserAsync();
        var sub = Guid.NewGuid().ToString("N");
        var email = $"y{sub[..10]}@gmail.test";
        await factory.WithDbAsync(async db =>
        {
            db.UserIdentities.Add(new UserIdentity(user.Id, ExternalProvider.Google, sub, email, DateTimeOffset.UtcNow));
            var u = await db.Users.SingleAsync(x => x.Id == user.Id);
            u.Anonymise(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            return 0;
        });
        var first = await SignInAsync(FakeGoogle.IdToken(sub, email), acceptTerms: true);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var fresh = Guid.Parse((await first.ReadEnvelopeAsync()).Data.GetProperty("user").Str("id"));
        (await SignInAsync(FakeGoogle.IdToken(sub, email))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await factory.WithDbAsync(db => db.UserIdentities.Where(i => i.ProviderKey == sub).Select(i => i.UserId).SingleAsync())).Should().Be(fresh);
    }
}
