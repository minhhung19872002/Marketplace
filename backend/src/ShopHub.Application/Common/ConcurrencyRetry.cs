using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;

namespace ShopHub.Application.Common;

public static class ConcurrencyRetry
{
    /// <summary>
    /// Runs "load → check → apply → save" again when the row changed underneath (xmin). System writes such as recomputed
    /// counters bump a shop's version all the time; re-running re-checks every rule, so a real conflicting decision (another
    /// admin approved first) still surfaces as its own business error. Tracked changes of the failed try are dropped.
    /// </summary>
    public static async Task<T> RetryOnStaleAsync<T>(this IApplicationDbContext db, Func<Task<T>> work, int attempts = 3)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await work();
            }
            catch (DbUpdateConcurrencyException) when (attempt < attempts)
            {
                db.ClearTracking();
            }
        }
    }

    public static Task RetryOnStaleAsync(this IApplicationDbContext db, Func<Task> work, int attempts = 3) =>
        db.RetryOnStaleAsync(async () =>
        {
            await work();
            return true;
        }, attempts);
}
