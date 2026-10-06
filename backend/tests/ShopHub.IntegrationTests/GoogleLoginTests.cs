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
}
