namespace ShopHub.Application.Abstractions;

public record ExternalIdentity(string Subject, string Email, bool EmailVerified, string? Name);

/// <summary>
/// Đăng nhập Google (spec I.1, tuỳ chọn): checks a Google ID token — RS256 signature against Google's published keys, issuer,
/// audience = the configured client id, expiry. Off (<see cref="ClientId"/> null) unless SH_GOOGLE_CLIENT_ID is set.
/// Throws AuthenticationFailedException for any token that does not pass.
/// </summary>
public interface IGoogleTokenVerifier
{
    string? ClientId { get; }

    Task<ExternalIdentity> VerifyAsync(string idToken, CancellationToken ct);
}
