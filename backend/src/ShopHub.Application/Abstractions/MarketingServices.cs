namespace ShopHub.Application.Abstractions;

public enum FlashTakeResult
{
    Taken,
    SoldOut,
    OverUserLimit,
    // The counter is not loaded yet (Redis restarted / new item): load it from the database and try again
    NotLoaded,
    // Redis is down: the database guard alone decides
    Unavailable,
}

/// <summary>
/// Fast Flash Sale quota counter (spec 3.10): an atomic Lua script on Redis takes units for a buyer (quota and
/// per-buyer limit in one step). PostgreSQL stays the source of truth (conditional UPDATE sold + n &lt;= quota) and the
/// reconcile job reloads the counters from it.
/// </summary>
public interface IFlashSaleCounter
{
    Task<FlashTakeResult> TryTakeAsync(Guid itemId, Guid userId, int quantity, int perUserLimit, CancellationToken ct);

    Task GiveBackAsync(Guid itemId, Guid userId, int quantity, CancellationToken ct);

    /// <summary>Set the counter from the database: units left and what each buyer already took.</summary>
    Task LoadAsync(Guid itemId, int left, IReadOnlyDictionary<Guid, int> buyers, CancellationToken ct);

    Task<int?> LeftAsync(Guid itemId, CancellationToken ct);
}
