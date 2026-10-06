using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopHub.Application.Abstractions;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Engage;
using ShopHub.Domain.SystemConfig;

namespace ShopHub.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Two jobs around the outbox (spec VII "gửi qua outbox"):
/// <list type="bullet">
/// <item>every new in-app notification gets a <c>notify.deliver</c> message in the same SaveChanges (so in the same
/// transaction) — the delivery handler then pushes it live and sends the email / SMS / push the user chose;</item>
/// <item>once the messages are committed, the in-process dispatcher is woken up, so a notification or an order event
/// goes out within milliseconds instead of at the next cron tick.</item>
/// </list>
/// </summary>
public sealed class OutboxInterceptor(IOutboxSignal signal, IClock clock) : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private bool _pending;

    private void Collect(DbContext? context)
    {
        if (context is null) return;
        var now = clock.UtcNow;
        foreach (var n in context.ChangeTracker.Entries<Notification>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList())
            context.Add(new OutboxMessage(OutboxTypes.NotifyDeliver, JsonSerializer.Serialize(new NotifyDeliverPayload(n.Id), Json), now));
        if (context.ChangeTracker.Entries<OutboxMessage>().Any(e => e.State == EntityState.Added)) _pending = true;
    }

    private void KickIfCommitted(DbContext? context)
    {
        if (!_pending || context?.Database.CurrentTransaction is not null) return;
        _pending = false;
        signal.Kick();
    }

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        KickIfCommitted(eventData.Context);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        KickIfCommitted(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (!_pending) return;
        _pending = false;
        signal.Kick();
    }

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        TransactionCommitted(transaction, eventData);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => _pending = false;

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending = false;
        return Task.CompletedTask;
    }
}
