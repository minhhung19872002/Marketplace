using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.SystemConfig;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Process-wide cache of system parameters. Invalidated locally on change and across instances via the
/// "sys.parameter.changed" outbox message (Redis pub/sub); entries also expire after a short TTL as a safety net.
/// </summary>
public sealed class CachedSystemParameters(IServiceScopeFactory scopeFactory, IClock clock) : ISystemParameters
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, (string Value, DateTimeOffset LoadedAt)> _cache = new();

    public async Task<string> GetStringAsync(string key, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out var hit) && clock.UtcNow - hit.LoadedAt < Ttl) return hit.Value;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopHubDbContext>();
        var value = await db.SystemParameters.AsNoTracking()
            .Where(p => p.Key == key)
            .Select(p => p.Value)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Thiếu tham số hệ thống '{key}' — chạy lại bộ gieo dữ liệu.");

        _cache[key] = (value, clock.UtcNow);
        return value;
    }

    public async Task<long> GetIntAsync(string key, CancellationToken ct = default) =>
        long.Parse(await GetStringAsync(key, ct), NumberStyles.Integer, CultureInfo.InvariantCulture);

    public async Task<bool> GetBoolAsync(string key, CancellationToken ct = default) =>
        await GetStringAsync(key, ct) == "true";

    public void Invalidate(string? key = null)
    {
        if (key is null) _cache.Clear();
        else _cache.TryRemove(key, out _);
    }
}

// Adds the message to the current DbContext so it commits atomically with the business change
public sealed class EfOutbox(ShopHubDbContext db, IClock clock) : IOutbox
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Enqueue<TPayload>(string type, TPayload payload) =>
        db.OutboxMessages.Add(new OutboxMessage(type, JsonSerializer.Serialize(payload, Json), clock.UtcNow));
}
