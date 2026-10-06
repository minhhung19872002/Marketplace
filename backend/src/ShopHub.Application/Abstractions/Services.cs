namespace ShopHub.Application.Abstractions;

// Who is calling (null user = anonymous / system job)
public interface ICurrentUser
{
    Guid? UserId { get; }

    // Login session (refresh token family) behind the current access token
    Guid? SessionId { get; }

    string? IpAddress { get; }
    string? UserAgent { get; }
    bool HasPermission(string permission);
}

// Time source — always UTC; convert to Asia/Ho_Chi_Minh only for display/business days
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

// Typed, cached access to system parameters (sys.system_parameters)
public interface ISystemParameters
{
    Task<string> GetStringAsync(string key, CancellationToken ct = default);
    Task<long> GetIntAsync(string key, CancellationToken ct = default);
    Task<bool> GetBoolAsync(string key, CancellationToken ct = default);

    // Drop cached values in this process (called after a change, and on cross-instance notification)
    void Invalidate(string? key = null);
}

// Record a side effect to run after commit (same transaction as the business change)
public interface IOutbox
{
    void Enqueue<TPayload>(string type, TPayload payload);
}

// Re-registers recurring jobs from the current schedule parameters (no restart needed)
public interface IJobScheduler
{
    Task RegisterRecurringJobsAsync(CancellationToken ct = default);
}
