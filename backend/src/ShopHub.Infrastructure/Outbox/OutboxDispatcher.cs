using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.SystemConfig;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Outbox;

// Handles one outbox message type; several handlers may share a type (all must succeed, all are idempotent)
public interface IOutboxHandler
{
    string Type { get; }
    Task HandleAsync(string payload, CancellationToken ct);
}

public record OutboxDispatchResult(int Processed, int Failed);

/// <summary>
/// Delivers pending outbox messages at-least-once. Rows are claimed with FOR UPDATE SKIP LOCKED so parallel
/// workers never pick the same message; a message whose handler keeps failing is retried until
/// JOB.OUTBOX_MAX_ATTEMPTS and then left for manual handling (attempts/last_error stay visible).
/// </summary>
public sealed class OutboxDispatcher(
    ShopHubDbContext db,
    IEnumerable<IOutboxHandler> handlers,
    ISystemParameters parameters,
    IClock clock,
    ILogger<OutboxDispatcher> logger)
{
    private readonly Dictionary<string, List<IOutboxHandler>> _handlers = handlers.GroupBy(h => h.Type).ToDictionary(g => g.Key, g => g.ToList());

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 0)]
    public Task RunJobAsync() => DispatchAsync(CancellationToken.None);

    public async Task<OutboxDispatchResult> DispatchAsync(CancellationToken ct)
    {
        var batchSize = (int)await parameters.GetIntAsync(ParameterKeys.JobOutboxBatchSize, ct);
        var maxAttempts = (int)await parameters.GetIntAsync(ParameterKeys.JobOutboxMaxAttempts, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var batch = await db.OutboxMessages
            .FromSqlInterpolated($"""
                SELECT * FROM sys.outbox_messages
                WHERE processed_at IS NULL AND attempts < {maxAttempts}
                ORDER BY occurred_at, id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        int processed = 0, failed = 0;
        foreach (var message in batch)
        {
            try
            {
                if (!_handlers.TryGetValue(message.Type, out var forType))
                    throw new InvalidOperationException($"Không có bộ xử lý cho loại tin outbox '{message.Type}'.");

                // A failure retries the whole message later: every handler is idempotent
                foreach (var handler in forType) await handler.HandleAsync(message.Payload, ct);
                message.MarkProcessed(clock.UtcNow);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.MarkFailed(ex.Message);
                failed++;
                logger.LogWarning(ex, "Outbox message {MessageId} ({Type}) failed, attempt {Attempt}",
                    message.Id, message.Type, message.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new OutboxDispatchResult(processed, failed);
    }
}

// Removes delivered messages older than JOB.OUTBOX_RETENTION_DAYS
public sealed class OutboxCleanupJob(ShopHubDbContext db, ISystemParameters parameters, IClock clock)
{
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task<int> RunAsync()
    {
        var days = await parameters.GetIntAsync(ParameterKeys.JobOutboxRetentionDays);
        var cutoff = clock.UtcNow.AddDays(-days);
        return await db.OutboxMessages
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
            .ExecuteDeleteAsync();
    }
}
