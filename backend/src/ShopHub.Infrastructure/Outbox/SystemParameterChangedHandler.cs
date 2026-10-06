using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.SystemConfig;
using StackExchange.Redis;

namespace ShopHub.Infrastructure.Outbox;

public static class RedisChannels
{
    // All Redis keys/channels carry the sh: prefix
    public static readonly RedisChannel ParameterChanged = RedisChannel.Literal("sh:sys:parameter-changed");
}

// Fan a parameter change out to every API instance so their caches drop the stale value
public sealed class SystemParameterChangedHandler(IConnectionMultiplexer redis, Search.MeiliSearchIndexer indexer) : IOutboxHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Type => OutboxTypes.SystemParameterChanged;

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var data = JsonSerializer.Deserialize<SystemParameterChangedPayload>(payload, Json)
            ?? throw new InvalidOperationException("Nội dung tin outbox rỗng.");
        await redis.GetSubscriber().PublishAsync(RedisChannels.ParameterChanged, data.Key);
        // New synonyms only take effect once pushed to the search engine
        if (data.Key == ParameterKeys.SearchSynonyms) await indexer.ConfigureAsync(ct);
    }
}

// Listens for cross-instance parameter changes and invalidates the local cache
public sealed class ParameterChangeSubscriber(
    IConnectionMultiplexer redis,
    ISystemParameters parameters,
    ILogger<ParameterChangeSubscriber> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await redis.GetSubscriber().SubscribeAsync(RedisChannels.ParameterChanged, (_, key) =>
                parameters.Invalidate(key.IsNullOrEmpty ? null : key.ToString()));
        }
        catch (RedisException ex)
        {
            // Redis down at boot: cache TTL still bounds staleness; readiness check reports the outage
            logger.LogWarning(ex, "Could not subscribe to parameter change channel");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
