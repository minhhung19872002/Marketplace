namespace ShopHub.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);

    // Spend the same time as a real verification (unknown account) so timing reveals nothing
    void SimulateVerify(string password);
}

/// <summary>Random secrets and their one-way hashes (OTP codes, refresh tokens, one-time tickets).</summary>
public interface ISecretGenerator
{
    string NewOtpCode();
    string NewOpaqueToken();
    string Hash(string secret);
}

public record AccessToken(string Token, DateTimeOffset ExpiresAt);

public record AccessTokenRequest(
    Guid UserId,
    string FullName,
    Guid SessionId,
    IReadOnlyCollection<string> Permissions,
    bool MustChangePassword,
    TimeSpan Lifetime);

public interface IAccessTokenIssuer
{
    AccessToken Issue(AccessTokenRequest request);
}

/// <summary>
/// Is the user (and the session behind the access token) still allowed in? Checked on every authenticated request so
/// a lock or remote logout cuts open sessions immediately, without waiting for the 15-minute token to expire.
/// </summary>
public interface ISessionValidator
{
    Task<bool> IsValidAsync(Guid userId, Guid sessionId, CancellationToken ct = default);
    void InvalidateUser(Guid userId);
    void InvalidateSession(Guid sessionId);
}

/// <summary>
/// Told whenever a user's or a session's access may have ended (lock, logout, password change…) on this instance, so
/// long-lived connections — the realtime hub — re-check and close instead of waiting for their token to expire (L128).
/// </summary>
public interface ISessionEndListener
{
    void SessionsChanged(Guid? userId, Guid? sessionId);
}

// Wakes the outbox dispatcher right after commit for latency-sensitive messages (OTP SMS/email)
public interface IOutboxSignal
{
    void Kick();
}

/// <summary>Blocks account deletion while something still depends on it (open orders, wallet balance…).</summary>
public interface IAccountDeletionGuard
{
    Task<string?> GetBlockingReasonAsync(Guid userId, CancellationToken ct);
}
