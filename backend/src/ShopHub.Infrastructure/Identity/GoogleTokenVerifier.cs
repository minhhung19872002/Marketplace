using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Infrastructure.Configuration;

namespace ShopHub.Infrastructure.Identity;

/// <summary>Validates Google ID tokens against Google's published signing keys (kept for an hour, refreshed on an unknown key id).</summary>
public sealed class GoogleTokenVerifier(IHttpClientFactory factory, ShopHubSettings settings) : IGoogleTokenVerifier
{
    public const string HttpClientName = "google-certs";
    private static readonly string[] Issuers = ["accounts.google.com", "https://accounts.google.com"];
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static (DateTimeOffset At, IReadOnlyList<SecurityKey> Keys)? _cache;

    public string? ClientId => settings.GoogleClientId;

    public async Task<ExternalIdentity> VerifyAsync(string idToken, CancellationToken ct)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        if (!handler.CanReadToken(idToken)) throw Invalid();
        var kid = handler.ReadJwtToken(idToken).Header.Kid;
        var keys = await KeysAsync(kid, ct);
        try
        {
            var principal = handler.ValidateToken(idToken, new TokenValidationParameters
            {
                ValidIssuers = Issuers,
                ValidAudience = settings.GoogleClientId,
                IssuerSigningKeys = keys,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            }, out _);
            string? Claim(string type) => principal.FindFirst(type)?.Value;
            var sub = Claim("sub");
            var email = Claim("email");
            if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(email)) throw Invalid();
            return new ExternalIdentity(sub, email, string.Equals(Claim("email_verified"), "true", StringComparison.OrdinalIgnoreCase), Claim("name"));
        }
        catch (SecurityTokenException)
        {
            throw Invalid();
        }
        catch (ArgumentException)
        {
            throw Invalid();
        }
    }

    private async Task<IReadOnlyList<SecurityKey>> KeysAsync(string? kid, CancellationToken ct)
    {
        var cached = _cache;
        if (cached is { } c && DateTimeOffset.UtcNow - c.At < TimeSpan.FromHours(1) && (kid is null || c.Keys.Any(k => k.KeyId == kid)))
            return c.Keys;
        await Gate.WaitAsync(ct);
        try
        {
            var json = await factory.CreateClient(HttpClientName).GetStringAsync(settings.GoogleCertsUrl, ct);
            var keys = new JsonWebKeySet(json).GetSigningKeys().ToList();
            _cache = (DateTimeOffset.UtcNow, keys);
            return keys;
        }
        catch (HttpRequestException)
        {
            throw new AuthenticationFailedException("Không kiểm tra được đăng nhập Google lúc này, vui lòng thử lại.", "GOOGLE_UNAVAILABLE");
        }
        finally
        {
            Gate.Release();
        }
    }

    private static AuthenticationFailedException Invalid() => new("Đăng nhập Google không hợp lệ hoặc đã hết hạn.", "GOOGLE_INVALID");
}
