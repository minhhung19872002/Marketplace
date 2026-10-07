using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>Phase 14 B6: sign-in and OTP are limited per account / phone too, not only per IP.</summary>
[Collection(ApiCollection.Name)]
public class IdentifierRateLimitTests(ApiFactory factory)
{
    [Fact]
    public async Task Sign_in_attempts_for_one_phone_are_limited_whatever_way_the_number_is_written()
    {
        var user = await factory.CreateUserAsync();
        var local = user.Phone;                      // 09xxxxxxxx
        var international = "+84" + local[1..];      // +849xxxxxxxx — the same number
        var client = factory.CreateClient();
        var answers = new List<HttpStatusCode>();
        // CreateUserAsync already signed in once: 29 more fit in the minute, the 30th of ours is refused
        for (var i = 0; i < 30; i++)
            answers.Add((await client.PostAsJsonAsync("/api/auth/login", new { identifier = i % 2 == 0 ? local : international, password = "sai-mat-khau-1" })).StatusCode);
        answers.Take(29).Should().NotContain(HttpStatusCode.TooManyRequests);
        answers[29].Should().Be(HttpStatusCode.TooManyRequests, "quá 30 lần/phút cho cùng một số, dù viết 09… hay +84…");

        // Another account from the same IP is not affected
        var other = await factory.CreateUserAsync();
        (await client.PostAsJsonAsync("/api/auth/login", new { identifier = other.Phone, password = ApiFactory.DefaultPassword })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Parallel_otp_requests_for_one_number_send_a_single_code()
    {
        var phone = ApiFactory.NewPhone();
        var client = factory.CreateClient();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
            client.PostAsJsonAsync("/api/auth/otp/send", new { target = phone, purpose = "Register" }))));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        var normalised = Application.Identity.Identifiers.NormalisePhone(phone)!;
        (await factory.WithDbAsync(db => db.OtpCodes.CountAsync(o => o.Target == normalised))).Should().Be(1, "chỉ một mã được phát cho một số trong thời gian chờ");
    }
}
