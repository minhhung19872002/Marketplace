namespace ShopHub.Application.Common;

/// <summary>Resource missing or not owned by the caller — always 404 (never 403) to avoid leaking existence.</summary>
public class NotFoundException(string message = "Không tìm thấy dữ liệu yêu cầu.") : Exception(message);

/// <summary>Request conflicts with current state (stale version, duplicate…) — HTTP 409.</summary>
public class ConflictException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}

/// <summary>Credentials or session not accepted — HTTP 401 (message stays generic: never says which part was wrong).</summary>
public class AuthenticationFailedException(string message = "Thông tin đăng nhập không đúng.", string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}

/// <summary>The caller owns the resource but lacks the specific permission (e.g. shop staff role) — HTTP 403.</summary>
public class ForbiddenException(string message = "Bạn không có quyền thực hiện thao tác này.") : Exception(message);
