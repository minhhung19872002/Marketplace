using Hangfire;
using ShopHub.Application.Abstractions;
using ShopHub.Application.SystemConfig;
using ShopHub.Infrastructure.Outbox;

namespace ShopHub.Infrastructure.Jobs;

public static class JobIds
{
    public const string OutboxDispatch = "sys.outbox-dispatch";
    public const string OutboxCleanup = "sys.outbox-cleanup";
    public const string CounterRecompute = "sys.counter-recompute";
    public const string PaymentExpiry = "sales.payment-expiry";
    public const string OrderAutomation = "sales.order-automation";
    public const string CarrierSimulator = "logistics.carrier-simulator";
    public const string Settlement = "finance.settlement";
    public const string LedgerCheck = "finance.ledger-check";

    // Jobs an admin may trigger on demand (POST /api/admin/job-runs/{id})
    public static readonly IReadOnlyList<string> Runnable =
        [OutboxDispatch, CounterRecompute, PaymentExpiry, OrderAutomation, CarrierSimulator, Settlement, LedgerCheck];
}

/// <summary>Hangfire entry for <see cref="Application.Features.Orders.OrderAutomationService"/>.</summary>
public sealed class OrderAutomationJob(Application.Features.Orders.OrderAutomationService orders, Application.Features.Returns.ReturnAutomationService returns)
{
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunJobAsync()
    {
        await orders.RunAsync(CancellationToken.None);
        await returns.RunAsync(CancellationToken.None);
    }
}

/// <summary>Hangfire entry for <see cref="Commerce.CarrierSimulator"/>.</summary>
public sealed class CarrierSimulatorJob(Commerce.CarrierSimulator simulator)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public Task RunJobAsync() => simulator.RunAsync(CancellationToken.None);
}

/// <summary>Hangfire entry for <see cref="Application.Features.Payments.PaymentExpiryService"/>.</summary>
public sealed class PaymentExpiryJob(Application.Features.Payments.PaymentExpiryService service)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public Task RunJobAsync() => service.RunAsync(CancellationToken.None);
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

        recurringJobs.AddOrUpdate<Search.SqlCounterRecomputer>(
            JobIds.CounterRecompute,
            j => j.RunJobAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobCounterRecomputeCron, ct),
            options);

        recurringJobs.AddOrUpdate<PaymentExpiryJob>(
            JobIds.PaymentExpiry,
            j => j.RunJobAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobPaymentExpiryCron, ct),
            options);

        recurringJobs.AddOrUpdate<OrderAutomationJob>(
            JobIds.OrderAutomation,
            j => j.RunJobAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobOrderAutomationCron, ct),
            options);

        recurringJobs.AddOrUpdate<CarrierSimulatorJob>(
            JobIds.CarrierSimulator,
            j => j.RunJobAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobCarrierSimulatorCron, ct),
            options);

        recurringJobs.AddOrUpdate<Finance.SettlementJob>(
            JobIds.Settlement,
            j => j.RunJobAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobSettlementCron, ct),
            options);

        recurringJobs.AddOrUpdate<Finance.LedgerCheckJob>(
            JobIds.LedgerCheck,
            j => j.RunJobAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobLedgerCheckCron, ct),
            options);

        recurringJobs.AddOrUpdate<OutboxCleanupJob>(
            JobIds.OutboxCleanup,
            j => j.RunAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobOutboxCleanupCron, ct),
            options);
    }
}
