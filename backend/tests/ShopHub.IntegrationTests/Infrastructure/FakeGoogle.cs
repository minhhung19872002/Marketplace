using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace ShopHub.IntegrationTests.Infrastructure;

/// <summary>Plays Google's side of "Đăng nhập Google": a signing key, its published key set, and ID tokens signed with it.</summary>
public static class FakeGoogle
{
    public const string ClientId = "shophub-test.apps.googleusercontent.com";
    public const string CertsUrl = "https://google.test/oauth2/v3/certs";

    private static readonly RSA Rsa = RSA.Create(2048);
    private static readonly RsaSecurityKey Key = new(Rsa) { KeyId = "test-key-1" };

    public static string CertsJson()
    {
        var p = Rsa.ExportParameters(false);
        var jwk = new
        {
            keys = new[]
            {
                new { kty = "RSA", use = "sig", alg = "RS256", kid = Key.KeyId, n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) },
            },
        };
        return JsonSerializer.Serialize(jwk);
    }

    /// <summary>An ID token as Google would issue it; override pieces to forge bad ones.</summary>
    public static string IdToken(string sub, string email, bool emailVerified = true, string? audience = null, DateTime? expires = null,
        string issuer = "https://accounts.google.com", SecurityKey? signWith = null, string name = "Người Dùng Google")
    {
        var claims = new List<Claim>
        {
            new("sub", sub), new("email", email), new("email_verified", emailVerified ? "true" : "false", ClaimValueTypes.Boolean), new("name", name),
        };
        var now = DateTime.UtcNow;
        var exp = expires ?? now.AddMinutes(30);
        var token = new JwtSecurityToken(issuer, audience ?? ClientId, claims, exp.AddHours(-1) < now.AddMinutes(-1) ? exp.AddHours(-1) : now.AddMinutes(-1), exp,
            new SigningCredentials(signWith ?? Key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static SecurityKey OtherKey() => new RsaSecurityKey(RSA.Create(2048)) { KeyId = Key.KeyId };
}

public sealed class FakeGoogleCertsHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(request.RequestUri!.ToString() == FakeGoogle.CertsUrl
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(FakeGoogle.CertsJson()) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
}
