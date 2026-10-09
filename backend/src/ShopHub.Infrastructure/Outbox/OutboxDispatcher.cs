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
/// The message being handled, for handlers that need more than its payload: a notification is dated when its event
/// happened, not when the outbox got round to it (G3 A4 — the sample orders' notifications all carried the seed time).
/// </summary>
public sealed class OutboxContext
{
    public DateTimeOffset? OccurredAt { get; set; }
}

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
    OutboxContext context,
    ILogger<OutboxDispatcher> logger)
{
    private readonly Dictionary<string, List<IOutboxHandler>> _handlers = handlers.GroupBy(h => h.Type).ToDictionary(g => g.Key, g => g.ToList());

    // Codes, password resets and session cut-offs go first: a backlog of ordinary notifications must never delay them
    private static readonly string[] Urgent = [OutboxTypes.NotifySms, OutboxTypes.NotifyEmail, OutboxTypes.SessionsChanged];

    /// <summary>The recurring job: batch after batch until the outbox is empty or ~45 s have passed (the next run continues).</summary>
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunJobAsync()
    {
        var batchSize = (int)await parameters.GetIntAsync(ParameterKeys.JobOutboxBatchSize, CancellationToken.None);
        var until = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < until)
        {
            var result = await DispatchAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (result.Processed + result.Failed < batchSize) break;
        }
    }

    /// <param name="onlyTypes">Only these message types (the sample seeder delivers order events before the search index
    /// exists — search messages must wait for it, G3 L169); null = all.</param>
    public async Task<OutboxDispatchResult> DispatchAsync(CancellationToken ct, IReadOnlyCollection<string>? onlyTypes = null)
    {
        var types = onlyTypes?.ToArray() ?? [];
        var batchSize = (int)await parameters.GetIntAsync(ParameterKeys.JobOutboxBatchSize, ct);
        var maxAttempts = (int)await parameters.GetIntAsync(ParameterKeys.JobOutboxMaxAttempts, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var batch = await db.OutboxMessages
            .FromSqlInterpolated($"""
                SELECT * FROM sys.outbox_messages
                WHERE processed_at IS NULL AND attempts < {maxAttempts} AND (cardinality({types}) = 0 OR type = ANY({types}))
                ORDER BY CASE WHEN type = ANY({Urgent}) THEN 0 ELSE 1 END, occurred_at, id
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
                context.OccurredAt = message.OccurredAt;
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
