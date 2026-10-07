using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using ShopHub.Domain.Engage;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Notifications;
using ShopHub.Infrastructure.Persistence;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>
/// G2 / G3: the real push (FCM HTTP v1) and SMS (eSMS) senders against fake servers in this process. Not checked against
/// Firebase / eSMS themselves — that needs the platform's own accounts (docs/07 "Chờ tài khoản").
/// </summary>
[Collection(ApiCollection.Name)]
public class PushSmsProviderTests(ApiFactory factory)
{
    private sealed class OneClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Google's token endpoint + FCM messages:send, checking the signed assertion and the bearer token.</summary>
    private sealed class FakeFcm(RSA publicKey, string clientEmail, string tokenUri) : HttpMessageHandler
    {
        public int TokenCalls;
        public readonly List<JsonElement> Messages = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            if (request.RequestUri!.ToString() == tokenUri)
            {
                TokenCalls++;
                var form = body.Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
                form["grant_type"].Should().Be("urn:ietf:params:oauth:grant-type:jwt-bearer");
                new JwtSecurityTokenHandler().ValidateToken(form["assertion"], new TokenValidationParameters
                {
                    IssuerSigningKey = new RsaSecurityKey(publicKey), ValidIssuer = clientEmail, ValidAudience = tokenUri,
                }, out var validated);
                ((JwtSecurityToken)validated).Claims.Single(c => c.Type == "scope").Value.Should().Be("https://www.googleapis.com/auth/firebase.messaging");
                return Json(HttpStatusCode.OK, """{"access_token":"fake-access","expires_in":3599,"token_type":"Bearer"}""");
            }
            request.RequestUri.AbsolutePath.Should().Be("/v1/projects/shophub-demo/messages:send");
            request.Headers.Authorization!.ToString().Should().Be("Bearer fake-access");
            var message = JsonDocument.Parse(body).RootElement.GetProperty("message").Clone();
            if (message.GetProperty("token").GetString() == "gone-token")
                return Json(HttpStatusCode.NotFound, """{"error":{"code":404,"status":"NOT_FOUND","details":[{"@type":"type.googleapis.com/google.firebase.fcm.v1.FcmError","errorCode":"UNREGISTERED"}]}}""");
            Messages.Add(message);
            return Json(HttpStatusCode.OK, """{"name":"projects/shophub-demo/messages/1"}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task Fcm_push_signs_in_with_the_service_account_sends_each_device_and_forgets_unregistered_tokens()
    {
        using var rsa = RSA.Create(2048);
        const string tokenUri = "https://oauth.fake.test/token";
        var account = JsonSerializer.Serialize(new
        {
            type = "service_account", project_id = "shophub-demo", client_email = "push@shophub-demo.iam.gserviceaccount.com",
            private_key = rsa.ExportPkcs8PrivateKeyPem(), token_uri = tokenUri,
        });
        // The key as operators set it: base64 of the JSON Google gives
        var options = FcmOptions.Parse(Convert.ToBase64String(Encoding.UTF8.GetBytes(account)), "https://fcm.fake.test");
        using var publicKey = RSA.Create();
        publicKey.ImportParameters(rsa.ExportParameters(false));
        var fake = new FakeFcm(publicKey, options.ClientEmail, tokenUri);
        var http = new OneClientFactory(fake);
        var tokens = new FcmAccessTokens(http, options);

        var user = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.DeviceTokens.Add(new DeviceToken(user.Id, "android", "live-token", DateTimeOffset.UtcNow));
            db.DeviceTokens.Add(new DeviceToken(user.Id, "ios", "gone-token", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return 0;
        });

        using (var scope = factory.Services.CreateScope())
        {
            var sender = new FcmPushSender(http, options, tokens, scope.ServiceProvider.GetRequiredService<ShopHubDbContext>(), NullLogger<FcmPushSender>.Instance);
            await sender.SendAsync(["live-token", "gone-token"], new PushMessage("Đơn hàng đã giao", "Đơn SH26 đã giao thành công.", "/tai-khoan/don-mua/SH26"),
                CancellationToken.None);
            await sender.SendAsync(["live-token"], new PushMessage("Lần hai", "Dùng lại access token", null), CancellationToken.None);
        }

        fake.TokenCalls.Should().Be(1, "access token được giữ lại tới gần hết hạn");
        fake.Messages.Should().HaveCount(2);
        fake.Messages[0].GetProperty("notification").GetProperty("title").GetString().Should().Be("Đơn hàng đã giao");
        fake.Messages[0].GetProperty("data").GetProperty("link").GetString().Should().Be("/tai-khoan/don-mua/SH26");
        (await factory.WithDbAsync(db => db.DeviceTokens.Where(d => d.UserId == user.Id).Select(d => d.Token).ToListAsync()))
            .Should().Equal(["live-token"], "token FCM báo UNREGISTERED bị xoá");
    }

    private sealed class FakeEsms : HttpMessageHandler
    {
        public readonly List<JsonElement> Requests = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
            Requests.Add(body);
            var json = body.GetProperty("Phone").GetString() == "0900000000"
                ? """{"CodeResult":"99","ErrorMessage":"Phone invalid"}"""
                : """{"CodeResult":"100","CountRegenerate":0,"SMSID":"5f0c1d2e"}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task Esms_sends_a_brandname_sms_and_a_refused_one_throws_so_the_outbox_retries()
    {
        var fake = new FakeEsms();
        var sender = new EsmsSmsSender(new OneClientFactory(fake), new EsmsOptions("api-key", "secret-key", "SHOPHUB", "https://esms.fake.test/send", "2"),
            NullLogger<EsmsSmsSender>.Instance);

        await sender.SendAsync("0912345678", "ShopHub: Ma 123456 de dang ky.", CancellationToken.None);
        var sent = fake.Requests.Single();
        sent.GetProperty("Phone").GetString().Should().Be("0912345678");
        sent.GetProperty("Brandname").GetString().Should().Be("SHOPHUB");
        sent.GetProperty("SmsType").GetString().Should().Be("2");
        sent.GetProperty("ApiKey").GetString().Should().Be("api-key");

        var refused = () => sender.SendAsync("0900000000", "x", CancellationToken.None);
        (await refused.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("CodeResult 99");
    }
}
