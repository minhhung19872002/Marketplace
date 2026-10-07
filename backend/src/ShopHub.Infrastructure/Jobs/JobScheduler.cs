using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    public const string FlashReconcile = "promo.flash-reconcile";
    public const string CoinExpiry = "promo.coin-expiry";
    public const string Reminders = "engage.reminders";
    public const string CartCleanup = "sales.cart-cleanup";
    public const string CarrierSync = "logistics.carrier-sync";
    public const string BulkSweep = "seller.bulk-sweep";
    public const string PriceIndex = "search.price-index";
    public const string Backup = "sys.backup";

    /// <summary>The JOB.*_CRON parameter holding each job's schedule (Vietnam time).</summary>
    public static readonly IReadOnlyDictionary<string, string> ScheduleParameter = new Dictionary<string, string>
    {
        [OutboxDispatch] = ParameterKeys.JobOutboxDispatchCron, [OutboxCleanup] = ParameterKeys.JobOutboxCleanupCron,
        [CounterRecompute] = ParameterKeys.JobCounterRecomputeCron, [PaymentExpiry] = ParameterKeys.JobPaymentExpiryCron,
        [OrderAutomation] = ParameterKeys.JobOrderAutomationCron, [CarrierSimulator] = ParameterKeys.JobCarrierSimulatorCron,
        [Settlement] = ParameterKeys.JobSettlementCron, [LedgerCheck] = ParameterKeys.JobLedgerCheckCron,
        [FlashReconcile] = ParameterKeys.JobFlashReconcileCron, [CoinExpiry] = ParameterKeys.JobCoinExpiryCron,
        [Reminders] = ParameterKeys.JobRemindersCron, [CartCleanup] = ParameterKeys.JobCartCleanupCron,
        [CarrierSync] = ParameterKeys.JobCarrierSyncCron, [BulkSweep] = ParameterKeys.JobBulkSweepCron, [PriceIndex] = ParameterKeys.JobPriceIndexCron,
        [Backup] = ParameterKeys.JobBackupCron,
    };

    // Jobs an admin may trigger on demand (POST /api/admin/job-runs/{id})
    public static readonly IReadOnlyList<string> Runnable =
        [OutboxDispatch, CounterRecompute, PaymentExpiry, OrderAutomation, CarrierSimulator, Settlement, LedgerCheck, FlashReconcile, CoinExpiry, Reminders,
            CarrierSync, CartCleanup, Backup];
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

/// <summary>Hangfire entry for <see cref="Application.Features.Chat.ReminderService"/>.</summary>
public sealed class RemindersJob(Application.Features.Chat.ReminderService service)
{
    [DisableConcurrentExecution(timeoutInSeconds: 900)]
    public Task RunJobAsync() => service.RunAsync(CancellationToken.None);
}

/// <summary>
/// Dọn giỏ khách cũ (6.4): guest carts untouched for CART.GUEST_RETENTION_DAYS go, in batches; signed-in buyers' carts
/// are never touched.
/// </summary>
public sealed class CartCleanupJob(Persistence.ShopHubDbContext db, ISystemParameters parameters, IClock clock, ILogger<CartCleanupJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 900)]
    public Task RunJobAsync() => RunAsync(CancellationToken.None);

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var days = await parameters.GetIntAsync(ParameterKeys.CartGuestRetentionDays, ct);
        var before = clock.UtcNow.AddDays(-Math.Max(1, days));
        var removed = 0;
        while (true)
        {
            var ids = await db.Carts.Where(c => c.UserId == null && c.GuestToken != null && c.UpdatedAt < before).OrderBy(c => c.Id).Select(c => c.Id)
                .Take(1_000).ToListAsync(ct);
            if (ids.Count == 0) break;
            await db.CartItems.Where(i => ids.Contains(i.CartId)).ExecuteDeleteAsync(ct);
            removed += await db.Carts.Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync(ct);
        }
        if (removed > 0) logger.LogInformation("Removed {Count} guest carts older than {Days} days", removed, days);
        return removed;
    }
}

/// <summary>Hangfire entry for <see cref="Application.Features.Orders.CarrierSyncService"/>.</summary>
public sealed class CarrierSyncJob(Application.Features.Orders.CarrierSyncService service)
{
    [DisableConcurrentExecution(timeoutInSeconds: 900)]
    public Task RunJobAsync() => service.RunAsync(CancellationToken.None);
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
        // Schedules are written in Vietnam time, like every other time an admin types (L081)
        var options = new RecurringJobOptions { TimeZone = Application.Common.VietnamTime.Zone };

        recurringJobs.AddOrUpdate<Ops.BackupJob>(
            JobIds.Backup,
            j => j.RunJobAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobBackupCron, ct),
            options);

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

        recurringJobs.AddOrUpdate<Marketing.FlashReconcileJob>(
            JobIds.FlashReconcile,
            j => j.RunJobAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobFlashReconcileCron, ct),
            options);

        recurringJobs.AddOrUpdate<Marketing.CoinExpiryJob>(
            JobIds.CoinExpiry,
            j => j.RunJobAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobCoinExpiryCron, ct),
            options);

        recurringJobs.AddOrUpdate<Search.PriceIndexJob>(
            JobIds.PriceIndex,
            j => j.RunJobAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobPriceIndexCron, ct),
            options);

        recurringJobs.AddOrUpdate<BulkTaskJob>(
            JobIds.BulkSweep,
            j => j.RunPendingAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobBulkSweepCron, ct),
            options);

        recurringJobs.AddOrUpdate<CartCleanupJob>(
            JobIds.CartCleanup,
            j => j.RunJobAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobCartCleanupCron, ct),
            options);

        recurringJobs.AddOrUpdate<RemindersJob>(
            JobIds.Reminders,
            j => j.RunJobAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobRemindersCron, ct),
            options);

        recurringJobs.AddOrUpdate<CarrierSyncJob>(
            JobIds.CarrierSync,
            j => j.RunJobAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobCarrierSyncCron, ct),
            options);

        recurringJobs.AddOrUpdate<OutboxCleanupJob>(
            JobIds.OutboxCleanup,
            j => j.RunAsync(),
            await parameters.GetStringAsync(ParameterKeys.JobOutboxCleanupCron, ct),
            options);
    }
}
