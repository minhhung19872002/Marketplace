using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Security;
using ShopHub.Domain.Iam;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Identity;

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    // Spec 6.1: work factor ≥ 12
    public const int WorkFactor = 12;

    private static readonly Lazy<string> DummyHash = new(() => BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), WorkFactor));

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    public void SimulateVerify(string password) => BCrypt.Net.BCrypt.Verify(password, DummyHash.Value);
}

/// <summary>
/// Secrets come from the OS CSPRNG. Stored form is HMAC-SHA256 keyed by a key derived from SH_JWT_SECRET,
/// so a leaked table of OTP/refresh hashes cannot be brute-forced offline without the server key.
/// </summary>
public sealed class SecretGenerator(ShopHubSettings settings) : ISecretGenerator
{
    private readonly byte[] _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(settings.JwtSecret), 32,
        info: Encoding.UTF8.GetBytes("shophub-secret-hash"));

    public string NewOtpCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public string NewOpaqueToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));

    public string Hash(string secret) => Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();
}

public static class ShopHubClaims
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string SessionId = "sid";

    // Set while the account must change its password (seeded admin); every other endpoint answers 403
    public const string PasswordChangeRequired = "pcr";
}

public sealed class JwtAccessTokenIssuer(ShopHubSettings settings, IClock clock) : IAccessTokenIssuer
{
    public const string Issuer = "ShopHub";
    public const string Audience = "ShopHub";

    private readonly SigningCredentials _credentials =
        new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.JwtSecret)), SecurityAlgorithms.HmacSha256);

    public AccessToken Issue(AccessTokenRequest request)
    {
        var now = clock.UtcNow;
        var expires = now.Add(request.Lifetime);
        var claims = new List<Claim>
        {
            new(ShopHubClaims.Subject, request.UserId.ToString()),
            new(ShopHubClaims.Name, request.FullName),
            new(ShopHubClaims.SessionId, request.SessionId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(request.Permissions.Select(p => new Claim(Permissions.ClaimType, p)));
        if (request.MustChangePassword) claims.Add(new Claim(ShopHubClaims.PasswordChangeRequired, "true"));

        var token = new JwtSecurityToken(Issuer, Audience, claims, now.UtcDateTime, expires.UtcDateTime, _credentials);
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

/// <summary>
/// Per-request check behind every access token: user still Active and the session (refresh family) still open.
/// Results are cached for a few seconds; lock/logout/password change invalidate them here and, through the
/// "iam.sessions.changed" outbox message, on every other instance.
/// </summary>
public sealed class CachedSessionValidator(IServiceScopeFactory scopeFactory, IClock clock) : ISessionValidator
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<Guid, (bool Active, DateTimeOffset At)> _users = new();
    private readonly ConcurrentDictionary<Guid, (Guid UserId, bool Open, DateTimeOffset At)> _sessions = new();

    public async Task<bool> IsValidAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        bool? active = _users.TryGetValue(userId, out var u) && now - u.At < Ttl ? u.Active : null;
        bool? open = _sessions.TryGetValue(sessionId, out var s) && s.UserId == userId && now - s.At < Ttl ? s.Open : null;
        if (active is false || open is false) return false;
        if (active is true && open is true) return true;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopHubDbContext>();
        active ??= await db.Users.AnyAsync(x => x.Id == userId && x.Status == UserStatus.Active, ct);
        open ??= await db.RefreshTokens.AnyAsync(t => t.FamilyId == sessionId && t.UserId == userId && t.RevokedAt == null, ct);
        _users[userId] = (active.Value, now);
        _sessions[sessionId] = (userId, open.Value, now);
        return active.Value && open.Value;
    }

    // User-wide change (lock, password change, role change): forget the user and every cached session of theirs
    public void InvalidateUser(Guid userId)
    {
        _users.TryRemove(userId, out _);
        foreach (var (sid, entry) in _sessions)
            if (entry.UserId == userId) _sessions.TryRemove(sid, out _);
    }

    public void InvalidateSession(Guid sessionId) => _sessions.TryRemove(sessionId, out _);
}
