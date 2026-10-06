using ShopHub.Domain.Common;

namespace ShopHub.Domain.Iam;

/// <summary>
/// One rotation step of a login session. All tokens issued from one login share a FamilyId (= a "device");
/// presenting an already-rotated token means it leaked, so the whole family is revoked.
/// </summary>
public class RefreshToken : Entity
{
    private RefreshToken() { }

    public RefreshToken(Guid userId, string tokenHash, Guid familyId, string? device, string? ip,
        DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        Device = device;
        Ip = ip;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid FamilyId { get; private set; }
    public string? Device { get; private set; }
    public string? Ip { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokeReason { get; private set; }
    public Guid? ReplacedById { get; private set; }
}

public static class RevokeReasons
{
    public const string Rotated = "ROTATED";
    public const string Logout = "LOGOUT";
    public const string ReuseDetected = "REUSE_DETECTED";
    public const string PasswordChanged = "PASSWORD_CHANGED";
    public const string RemoteLogout = "REMOTE_LOGOUT";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string AccountDeleted = "ACCOUNT_DELETED";
}

public enum OtpPurpose
{
    Register,
    Login,
    ResetPassword,
    ChangePhone,
    ChangeEmail,
}

public class OtpCode : Entity
{
    private OtpCode() { }

    public OtpCode(string target, OtpPurpose purpose, string codeHash, string? ip, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Target = target;
        Purpose = purpose;
        CodeHash = codeHash;
        Ip = ip;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public string Target { get; private set; } = string.Empty;
    public OtpPurpose Purpose { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public string? Ip { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public int Attempts { get; private set; }

    // After a correct code: a one-time ticket the next step (register / reset) must present
    public string? VerificationTokenHash { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public void MarkVerified(string ticketHash, DateTimeOffset now)
    {
        VerificationTokenHash = ticketHash;
        VerifiedAt = now;
    }

    public void Consume(DateTimeOffset now) => ConsumedAt = now;
}

public class Role : AuditableEntity
{
    private readonly List<RolePermission> _permissions = [];

    private Role() { }

    public Role(string code, string name, string description, bool isSystem)
    {
        Code = code;
        Name = name;
        Description = description;
        IsSystem = isSystem;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    // System roles (seeded) cannot be deleted
    public bool IsSystem { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    public void Rename(string name, string description)
    {
        Name = name.Trim();
        Description = description.Trim();
    }

    public void SetPermissions(IEnumerable<string> codes)
    {
        var wanted = codes.Distinct().ToHashSet();
        _permissions.RemoveAll(p => !wanted.Contains(p.PermissionCode));
        foreach (var code in wanted.Where(c => _permissions.All(p => p.PermissionCode != c)))
            _permissions.Add(new RolePermission(Id, code));
    }
}

public class RolePermission
{
    private RolePermission() { }

    public RolePermission(Guid roleId, string permissionCode)
    {
        RoleId = roleId;
        PermissionCode = permissionCode;
    }

    public Guid RoleId { get; private set; }
    public string PermissionCode { get; private set; } = string.Empty;
}

public class Permission
{
    private Permission() { }

    public Permission(string code, string module, string name)
    {
        Code = code;
        Module = module;
        Name = name;
    }

    public string Code { get; private set; } = string.Empty;
    public string Module { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    public void Rename(string module, string name)
    {
        Module = module;
        Name = name;
    }
}

public class UserRole
{
    private UserRole() { }

    public UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
}
