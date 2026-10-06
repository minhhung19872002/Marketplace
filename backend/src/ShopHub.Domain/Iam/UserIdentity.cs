using ShopHub.Domain.Common;

namespace ShopHub.Domain.Iam;

public enum ExternalProvider
{
    Google,
}

/// <summary>A sign-in method from another provider linked to the account (spec 4.1 user_identities); unique per provider + key.</summary>
public class UserIdentity : Entity
{
    private UserIdentity() { }

    public UserIdentity(Guid userId, ExternalProvider provider, string providerKey, string? email, DateTimeOffset now)
    {
        UserId = userId;
        Provider = provider;
        ProviderKey = providerKey;
        Email = email;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }
    public ExternalProvider Provider { get; private set; }
    // The provider's stable subject id ("sub"), never the e-mail (it can change)
    public string ProviderKey { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
