using Hangfire;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Seller;

namespace ShopHub.Infrastructure.Jobs;

/// <summary>Hangfire entry points of the seller's Excel bulk tasks (each job gets its own DI scope).</summary>
public sealed class BulkTaskJob(BulkTaskRunner runner)
{
    // A task claims itself (Queued → Running) and records its own failure: Hangfire must not run it again
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(Guid taskId) => runner.RunAsync(taskId, CancellationToken.None);

    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    [AutomaticRetry(Attempts = 0)]
    public Task RunPendingAsync() => runner.RunPendingAsync(CancellationToken.None);
}

public sealed class HangfireBackgroundTasks(IBackgroundJobClient jobs) : IBackgroundTasks
{
    public void Enqueue(Guid taskId) => jobs.Enqueue<BulkTaskJob>(j => j.RunAsync(taskId));
}
