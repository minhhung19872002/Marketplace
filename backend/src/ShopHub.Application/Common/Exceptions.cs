namespace ShopHub.Application.Common;

/// <summary>Resource missing or not owned by the caller — always 404 (never 403) to avoid leaking existence.</summary>
public class NotFoundException(string message = "Không tìm thấy dữ liệu yêu cầu.") : Exception(message);

/// <summary>Request conflicts with current state (stale version, duplicate…) — HTTP 409.</summary>
public class ConflictException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}
