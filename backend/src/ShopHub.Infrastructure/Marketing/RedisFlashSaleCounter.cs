using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using StackExchange.Redis;

namespace ShopHub.Infrastructure.Marketing;

/// <summary>
/// Flash Sale quota on Redis (spec 3.10). One Lua script checks the units left and the buyer's own count and takes
/// both at once, so a thousand buyers pressing "Mua" in the same second are served one after another inside Redis.
/// Keys: <c>fs:{item}:left</c> (units left) and <c>fs:{item}:buyers</c> (hash user → units). They expire a day after
/// the last write; the reconcile job reloads them from PostgreSQL.
/// </summary>
public sealed class RedisFlashSaleCounter(IConnectionMultiplexer redis, ILogger<RedisFlashSaleCounter> logger) : IFlashSaleCounter
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(1);

    // KEYS[1] left, KEYS[2] buyers · ARGV[1] quantity, ARGV[2] user, ARGV[3] per-user limit
    private const string TakeScript = """
        local left = redis.call('GET', KEYS[1])
        if not left then return -2 end
        left = tonumber(left)
        local qty = tonumber(ARGV[1])
        local limit = tonumber(ARGV[3])
        local bought = tonumber(redis.call('HGET', KEYS[2], ARGV[2]) or '0')
        if limit > 0 and bought + qty > limit then return -3 end
        if left < qty then return -1 end
        redis.call('DECRBY', KEYS[1], qty)
        redis.call('HINCRBY', KEYS[2], ARGV[2], qty)
        return left - qty
        """;

    private const string GiveBackScript = """
        if redis.call('EXISTS', KEYS[1]) == 1 then redis.call('INCRBY', KEYS[1], tonumber(ARGV[1])) end
        local bought = tonumber(redis.call('HGET', KEYS[2], ARGV[2]) or '0')
        if bought > 0 then redis.call('HINCRBY', KEYS[2], ARGV[2], -math.min(bought, tonumber(ARGV[1]))) end
        return 1
        """;

    private static RedisKey Left(Guid item) => $"fs:{item:N}:left";

    private static RedisKey Buyers(Guid item) => $"fs:{item:N}:buyers";

    public async Task<FlashTakeResult> TryTakeAsync(Guid itemId, Guid userId, int quantity, int perUserLimit, CancellationToken ct)
    {
        try
        {
            var result = (long)await redis.GetDatabase().ScriptEvaluateAsync(TakeScript, [Left(itemId), Buyers(itemId)],
                [quantity, userId.ToString("N"), perUserLimit]);
            return result switch
            {
                -2 => FlashTakeResult.NotLoaded,
                -3 => FlashTakeResult.OverUserLimit,
                -1 => FlashTakeResult.SoldOut,
                _ => FlashTakeResult.Taken,
            };
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis unavailable for flash item {ItemId}", itemId);
            return FlashTakeResult.Unavailable;
        }
    }

    public async Task GiveBackAsync(Guid itemId, Guid userId, int quantity, CancellationToken ct) =>
        await redis.GetDatabase().ScriptEvaluateAsync(GiveBackScript, [Left(itemId), Buyers(itemId)], [quantity, userId.ToString("N")]);

    public async Task LoadAsync(Guid itemId, int left, IReadOnlyDictionary<Guid, int> buyers, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var tx = db.CreateTransaction();
        _ = tx.StringSetAsync(Left(itemId), left, Ttl);
        _ = tx.KeyDeleteAsync(Buyers(itemId));
        if (buyers.Count > 0)
        {
            _ = tx.HashSetAsync(Buyers(itemId), buyers.Select(b => new HashEntry(b.Key.ToString("N"), b.Value)).ToArray());
            _ = tx.KeyExpireAsync(Buyers(itemId), Ttl);
        }
        await tx.ExecuteAsync();
    }

    public async Task<int?> LeftAsync(Guid itemId, CancellationToken ct)
    {
        var value = await redis.GetDatabase().StringGetAsync(Left(itemId));
        return value.HasValue ? (int)value : null;
    }
}

/// <summary>Hangfire entry for <see cref="Application.Features.Marketing.FlashSaleReconciler"/>.</summary>
public sealed class FlashReconcileJob(Application.Features.Marketing.FlashSaleReconciler reconciler)
{
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 120)]
    public Task RunJobAsync() => reconciler.RunAsync(CancellationToken.None);
}

/// <summary>Order completed / refunded → voucher cash-back in xu follows (own scope and transaction, idempotent).</summary>
public sealed class CashbackOrderEventHandler(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes) : Outbox.IOutboxHandler
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    public string Type => Application.SystemConfig.OutboxTypes.OrderEvent;

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var e = System.Text.Json.JsonSerializer.Deserialize<Application.SystemConfig.OrderEventPayload>(payload, Json)
                ?? throw new InvalidOperationException("Tin outbox rỗng.");
        if (e.Event is not (Application.SystemConfig.OrderEvents.Completed or Application.SystemConfig.OrderEvents.ReturnRefunded)) return;
        await using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(scopes);
        var sp = scope.ServiceProvider;
        var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IApplicationDbContext>(sp);
        await using var tx = await db.BeginTransactionAsync(ct);
        await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Application.Features.Marketing.CashbackService>(sp).SyncAsync(e.OrderId, ct);
        await tx.CommitAsync(ct);
    }
}

/// <summary>Hangfire entry for <see cref="Application.Features.Marketing.CoinExpiryService"/>.</summary>
public sealed class CoinExpiryJob(Application.Features.Marketing.CoinExpiryService service)
{
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public Task RunJobAsync() => service.RunAsync(CancellationToken.None);
}

/// <summary>Opens the platform's Flash Sale slots ahead of time (G4-C, JOB.FLASH_AUTO_OPEN_CRON).</summary>
public sealed class FlashAutoOpenJob(Application.Features.Marketing.FlashAutoOpener opener)
{
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 300)]
    public Task RunJobAsync() => opener.RunAsync(CancellationToken.None);
}
