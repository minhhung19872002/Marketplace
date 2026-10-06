using Hangfire;
using ShopHub.Application.Abstractions;
using ShopHub.Application.SystemConfig;
using ShopHub.Infrastructure.Outbox;

namespace ShopHub.Infrastructure.Jobs;

public static class JobIds
{
    public const string OutboxDispatch = "sys.outbox-dispatch";
    public const string OutboxCleanup = "sys.outbox-cleanup";
}

/// <summary>
/// Registers recurring jobs from the JOB.* parameters. Called at startup and again right after an admin changes
/// a schedule, so the new cron applies immediately.
/// </summary>
public sealed class HangfireJobScheduler(IRecurringJobManager recurringJobs, ISystemParameters parameters) : IJobScheduler
{
    public async Task RegisterRecurringJobsAsync(CancellationToken ct = default)
    {
        var options = new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc };

        recurringJobs.AddOrUpdate<OutboxDispatcher>(
            JobIds.OutboxDispatch,
            d => d.RunJobAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobOutboxDispatchCron, ct),
            options);

        recurringJobs.AddOrUpdate<OutboxCleanupJob>(
            JobIds.OutboxCleanup,
            j => j.RunAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobOutboxCleanupCron, ct),
            options);
    }
}
