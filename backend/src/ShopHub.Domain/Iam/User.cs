using ShopHub.Domain.Common;

namespace ShopHub.Domain.Iam;

public enum UserStatus
{
    Active,
    Locked,
    Deleted,
}

public enum Gender
{
    Male,
    Female,
    Other,
}

public class User : AuditableEntity
{
    private User() { }

    public static User Register(string? phone, string? email, string passwordHash, string fullName, DateTimeOffset now)
    {
        if (phone is null && email is null) throw new BusinessRuleException("Cần số điện thoại hoặc email để đăng ký.");
        return new User
        {
            Phone = phone,
            Email = email,
            PasswordHash = passwordHash,
            FullName = fullName.Trim(),
            Status = UserStatus.Active,
            PhoneVerifiedAt = phone is null ? null : now,
            EmailVerifiedAt = email is null ? null : now,
            ConsentedAt = now,
        };
    }

    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Username { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string? AvatarUrl { get; private set; }
    public Gender? Gender { get; private set; }
    public DateOnly? DateOfBirth { get; private set; }
    public UserStatus Status { get; private set; }
    public string? LockReason { get; private set; }
    public DateTimeOffset? PhoneVerifiedAt { get; private set; }
    public DateTimeOffset? EmailVerifiedAt { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public bool MustChangePassword { get; private set; }

    // Consent to personal data processing (Decree 13/2023/NĐ-CP), captured at registration
    public DateTimeOffset? ConsentedAt { get; private set; }

    public uint Version { get; private set; }

    public bool IsTemporarilyLocked(DateTimeOffset now) => LockedUntil is { } until && until > now;

    public void SetUsername(string username) => Username = username.Trim().ToLowerInvariant();

    public void RequirePasswordChange() => MustChangePassword = true;

    public void ChangePassword(string newHash)
    {
        PasswordHash = newHash;
        MustChangePassword = false;
        FailedLoginCount = 0;
        LockedUntil = null;
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockedUntil = null;
        LastLoginAt = now;
    }

    public void UpdateProfile(string fullName, Gender? gender, DateOnly? dateOfBirth)
    {
        FullName = fullName.Trim();
        Gender = gender;
        DateOfBirth = dateOfBirth;
    }

    public void ChangePhone(string phone, DateTimeOffset now)
    {
        Phone = phone;
        PhoneVerifiedAt = now;
    }

    public void ChangeEmail(string email, DateTimeOffset now)
    {
        Email = email;
        EmailVerifiedAt = now;
    }

    public void SetAvatar(string? url) => AvatarUrl = url;

    public void Lock(string reason)
    {
        if (Status == UserStatus.Deleted) throw new BusinessRuleException("Tài khoản đã bị xoá.");
        Status = UserStatus.Locked;
        LockReason = reason.Trim();
    }

    public void Unlock()
    {
        if (Status != UserStatus.Locked) throw new BusinessRuleException("Tài khoản không ở trạng thái bị khoá.");
        Status = UserStatus.Active;
        LockReason = null;
        FailedLoginCount = 0;
        LockedUntil = null;
    }

    /// <summary>
    /// Account deletion keeps the row (orders reference it for accounting) but erases personal data.
    /// </summary>
    public void Anonymise(DateTimeOffset now)
    {
        Phone = null;
        Email = null;
        Username = null;
        FullName = "Người dùng đã xoá";
        AvatarUrl = null;
        Gender = null;
        DateOfBirth = null;
        PasswordHash = string.Empty;
        Status = UserStatus.Deleted;
        DeletedAt = now;
    }
}
