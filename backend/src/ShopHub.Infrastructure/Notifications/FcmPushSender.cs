using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Notifications;

/// <summary>
/// Google OAuth2 access tokens for FCM from the service-account key (JWT bearer grant, RS256), kept until a minute before
/// they expire. Singleton: one token serves every send.
/// </summary>
public sealed class FcmAccessTokens(IHttpClientFactory http, FcmOptions options)
{
    public const string HttpClientName = "fcm";
    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _expiresAt) return _token;
        await _gate.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _expiresAt) return _token;
            using var rsa = RSA.Create();
            rsa.ImportFromPem(options.PrivateKey);
            var now = DateTime.UtcNow;
            var assertion = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                issuer: options.ClientEmail, audience: options.TokenUri, claims: [new Claim("scope", Scope)], notBefore: now, expires: now.AddHours(1),
                signingCredentials: new SigningCredentials(new RsaSecurityKey(rsa.ExportParameters(true)), SecurityAlgorithms.RsaSha256)));
            using var response = await http.CreateClient(HttpClientName).PostAsync(options.TokenUri, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion,
            }), ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            _token = body.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("FCM token response has no access_token.");
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, body.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600) - 60);
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>
/// Push through Firebase Cloud Messaging HTTP v1 (spec VII, G2) — on only when SH_FCM_SERVICE_ACCOUNT is set. One request per
/// device token; a token FCM no longer knows (UNREGISTERED / 404) is deleted so it is not tried again. A failed push is
/// logged and never fails the other channels of the notification.
/// </summary>
public sealed class FcmPushSender(IHttpClientFactory http, FcmOptions options, FcmAccessTokens tokens, ShopHubDbContext db, ILogger<FcmPushSender> logger)
    : IPushSender
{
    public async Task SendAsync(IReadOnlyList<string> deviceTokens, PushMessage message, CancellationToken ct)
    {
        if (deviceTokens.Count == 0) return;
        var accessToken = await tokens.GetAsync(ct);
        var client = http.CreateClient(FcmAccessTokens.HttpClientName);
        var url = $"{options.ApiBase.TrimEnd('/')}/v1/projects/{Uri.EscapeDataString(options.ProjectId)}/messages:send";
        var gone = new List<string>();
        var sent = 0;
        foreach (var token in deviceTokens.Distinct())
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new
                {
                    message = new
                    {
                        token,
                        notification = new { title = message.Title, body = message.Body },
                        data = new Dictionary<string, string> { ["link"] = message.Link ?? "/" },
                    },
                }),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                sent++;
                continue;
            }
            var error = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == HttpStatusCode.NotFound || error.Contains("UNREGISTERED", StringComparison.Ordinal))
                gone.Add(token);
            else
                logger.LogWarning("FCM push failed with {Status}", (int)response.StatusCode);
        }
        if (gone.Count > 0)
        {
            await db.DeviceTokens.Where(d => gone.Contains(d.Token)).ExecuteDeleteAsync(ct);
            logger.LogInformation("FCM: {Count} unregistered device token(s) removed", gone.Count);
        }
        logger.LogDebug("FCM push sent to {Sent}/{Total} device(s)", sent, deviceTokens.Count);
    }
}
