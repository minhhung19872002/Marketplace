using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Iam;

namespace ShopHub.Application.Identity;

public record AuthUserDto(Guid Id, string FullName, string? Phone, string? Email, string? AvatarUrl, bool MustChangePassword);

public record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    AuthUserDto User);

/// <summary>Issues, rotates and revokes login sessions (refresh token families) and their access tokens.</summary>
public sealed class SessionService(
    IApplicationDbContext db,
    ISecretGenerator secrets,
    IAccessTokenIssuer tokenIssuer,
    ISessionValidator sessionValidator,
    ISystemParameters parameters,
    IOutbox outbox,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<AuthResult> StartAsync(User user, string? device, CancellationToken ct) =>
        (await IssueCoreAsync(user, Guid.NewGuid(), device, ct)).Result;

    /// <summary>
    /// Rotate a refresh token. The old token is retired with a conditional UPDATE: if it was already retired
    /// (stolen copy, or a parallel replay) the whole family is revoked and the caller must sign in again — except
    /// within the reuse grace (decision #223): a token rotated a few seconds ago whose successor was never used is a
    /// browser that lost the answer (reload mid-request, L212), so the unused successor is retired in its place.
    /// </summary>
    public async Task<AuthResult> RotateAsync(string refreshToken, string? device, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hash = secrets.Hash(refreshToken);
        var token = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw Unauthenticated();

        var retired = await db.RefreshTokens
            .Where(t => t.Id == token.Id && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokeReason, RevokeReasons.Rotated), ct);

        if (retired == 0)
        {
            if (token.RevokeReason is not (RevokeReasons.Rotated or RevokeReasons.Superseded)) throw Unauthenticated();
            var successor = token.RevokeReason == RevokeReasons.Rotated ? await RetireUnusedSuccessorAsync(token, device, now, ct) : null;
            if (successor is null)
            {
                // Not cancellable: a thief must not dodge the revocation by dropping the connection
                await RevokeFamilyAsync(token.UserId, token.FamilyId, RevokeReasons.ReuseDetected, CancellationToken.None);
                throw Unauthenticated();
            }
            token = successor;
        }

        // The old token is retired: finish the rotation even if the browser goes away now (reload mid-request). A
        // cancelled insert or link would leave a retired token with no successor, which the grace cannot recognise (L213)
        ct = CancellationToken.None;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (user is null || user.Status != UserStatus.Active)
        {
            await RevokeFamilyAsync(token.UserId, token.FamilyId, RevokeReasons.AccountLocked, ct);
            throw Unauthenticated();
        }

        var (result, newId) = await IssueCoreAsync(user, token.FamilyId, device ?? token.Device, ct);
        await db.RefreshTokens.Where(t => t.Id == token.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReplacedById, newId), ct);
        return result;
    }

    /// <summary>
    /// The reuse grace: <paramref name="token"/> was rotated at most AUTH.REFRESH_REUSE_GRACE_SECONDS ago, its successor
    /// is still unused (the live head of the family) and was issued to the same device as the caller — the rotation whose
    /// answer was lost (the sign-in may have named the device differently, so the old token's own device is not compared). Retires that successor with the same
    /// conditional UPDATE as a rotation (only one caller wins) and returns it, so the caller rotates from it. Null when
    /// any condition fails — a real reuse.
    /// </summary>
    private async Task<RefreshToken?> RetireUnusedSuccessorAsync(RefreshToken token, string? device, DateTimeOffset now, CancellationToken ct)
    {
        var graceSeconds = await parameters.GetIntAsync(ParameterKeys.AuthRefreshReuseGraceSeconds, ct);
        if (graceSeconds <= 0 || token.ReplacedById is not { } successorId || token.RevokedAt is not { } rotatedAt
            || now - rotatedAt > TimeSpan.FromSeconds(graceSeconds)) return null;
        var successor = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Id == successorId, ct);
        if (successor is null) return null;
        if (device is not null && successor.Device is not null && Truncate(device, 200) != successor.Device) return null;
        var retired = await db.RefreshTokens
            .Where(t => t.Id == successorId && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokeReason, RevokeReasons.Superseded), ct);
        return retired == 1 ? successor : null;
    }

    public async Task RevokeFamilyAsync(Guid userId, Guid familyId, string reason, CancellationToken ct)
    {
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.UtcNow).SetProperty(t => t.RevokeReason, reason), ct);
        sessionValidator.InvalidateSession(familyId);
        await BroadcastAsync(userId, familyId, ct);
    }

    /// <summary>Revoke every session of the user, optionally keeping the current one.</summary>
    public async Task RevokeAllAsync(Guid userId, string reason, Guid? keepFamilyId, CancellationToken ct)
    {
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && (keepFamilyId == null || t.FamilyId != keepFamilyId))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.UtcNow).SetProperty(t => t.RevokeReason, reason), ct);
        sessionValidator.InvalidateUser(userId);
        await BroadcastAsync(userId, null, ct);
    }

    public Task<bool> IsActiveAsync(Guid userId, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Id == userId && u.Status == UserStatus.Active, ct);

    public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken ct) =>
        await (from ur in db.UserRoles
               join r in db.Roles on ur.RoleId equals r.Id
               from p in r.Permissions
               where ur.UserId == userId
               select p.PermissionCode).Distinct().ToListAsync(ct);

    private async Task<(AuthResult Result, Guid RefreshTokenId)> IssueCoreAsync(User user, Guid familyId, string? device, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var refreshDays = await parameters.GetIntAsync(ParameterKeys.AuthRefreshTokenDays, ct);
        var accessMinutes = await parameters.GetIntAsync(ParameterKeys.AuthAccessTokenMinutes, ct);

        var refresh = secrets.NewOpaqueToken();
        var refreshExpires = now.AddDays(refreshDays);
        var row = new RefreshToken(user.Id, secrets.Hash(refresh), familyId, Truncate(device, 200),
            currentUser.IpAddress, now, refreshExpires);
        db.RefreshTokens.Add(row);
        await db.SaveChangesAsync(ct);

        var permissions = await GetPermissionsAsync(user.Id, ct);
        var access = tokenIssuer.Issue(new AccessTokenRequest(user.Id, user.FullName, familyId, permissions,
            user.MustChangePassword, TimeSpan.FromMinutes(accessMinutes)));

        return (new AuthResult(access.Token, access.ExpiresAt, refresh, refreshExpires,
            new AuthUserDto(user.Id, user.FullName, user.Phone, user.Email, user.AvatarUrl, user.MustChangePassword)), row.Id);
    }

    private async Task BroadcastAsync(Guid userId, Guid? sessionId, CancellationToken ct)
    {
        outbox.Enqueue(OutboxTypes.SessionsChanged, new SessionsChangedPayload(userId, sessionId));
        await db.SaveChangesAsync(ct);
    }

    public static AuthenticationFailedException Unauthenticated() =>
        new("Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.", "SESSION_INVALID");

    private static string? Truncate(string? v, int max) => v is null || v.Length <= max ? v : v[..max];
}
