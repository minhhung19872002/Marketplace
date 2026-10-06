using System.Text.RegularExpressions;
using FluentValidation;

namespace ShopHub.Application.Identity;

public enum IdentifierKind
{
    Phone,
    Email,
    Username,
}

public static partial class Identifiers
{
    // Vietnamese mobile numbers: 0 + carrier prefix (3/5/7/8/9) + 8 digits
    [GeneratedRegex(@"^0[35789]\d{8}$")]
    private static partial Regex VnMobile();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailShape();

    [GeneratedRegex(@"^[a-z0-9._]{4,30}$")]
    private static partial Regex UsernameShape();

    /// <summary>"+84 912 345 678", "84912345678", "0912.345.678" → "0912345678"; null when not a VN mobile.</summary>
    public static string? NormalisePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (raw.TrimStart().StartsWith('+') || digits.StartsWith("84", StringComparison.Ordinal) && digits.Length == 11)
            digits = "0" + digits[2..];
        return VnMobile().IsMatch(digits) ? digits : null;
    }

    public static string? NormaliseEmail(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var email = raw.Trim().ToLowerInvariant();
        return email.Length <= 254 && EmailShape().IsMatch(email) ? email : null;
    }

    public static (IdentifierKind Kind, string Value)? Classify(string? raw)
    {
        if (NormalisePhone(raw) is { } phone) return (IdentifierKind.Phone, phone);
        if (NormaliseEmail(raw) is { } email) return (IdentifierKind.Email, email);
        var username = raw?.Trim().ToLowerInvariant();
        return username is not null && UsernameShape().IsMatch(username) ? (IdentifierKind.Username, username) : null;
    }

    /// <summary>Phone or email OTP target, normalised; null when neither.</summary>
    public static string? NormaliseOtpTarget(string? raw) => NormalisePhone(raw) ?? NormaliseEmail(raw);

    public static bool IsPhone(string target) => VnMobile().IsMatch(target);

    /// <summary>0912***678 — for logs and admin lists.</summary>
    public static string MaskPhone(string? phone) =>
        phone is { Length: 10 } ? $"{phone[..4]}***{phone[^3..]}" : "***";
}

public static class PasswordRules
{
    /// <summary>At least 8 characters with both letters and digits.</summary>
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Vui lòng nhập mật khẩu.")
            .MinimumLength(8).WithMessage("Mật khẩu phải có ít nhất 8 ký tự.")
            .MaximumLength(128).WithMessage("Mật khẩu tối đa 128 ký tự.")
            .Must(p => p.Any(char.IsLetter) && p.Any(char.IsDigit)).WithMessage("Mật khẩu phải có cả chữ và số.");
}
