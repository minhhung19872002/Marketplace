using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Common;
using ShopHub.Domain.Iam;
using ShopHub.Domain.SystemConfig;

namespace ShopHub.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Fills who/when columns, turns deletes into soft deletes, and writes one audit_logs row per changed entity
/// (in the same SaveChanges, hence the same transaction).
/// </summary>
public sealed class AuditSaveChangesInterceptor(ICurrentUser currentUser, IClock clock) : SaveChangesInterceptor
{
    // Never journal secrets in clear text (property names are matched case-insensitively)
    private static readonly string[] SensitiveFragments =
        ["password", "token", "otp", "secret", "accountno", "account_no", "cardnumber", "idcard"];

    // High-volume or secret-bearing rows are not journalled (sessions/OTPs have their own tables and timestamps)
    private static readonly HashSet<Type> NotAudited =
        [typeof(AuditLog), typeof(OutboxMessage), typeof(SimulatedSms), typeof(AdminDivision), typeof(RefreshToken), typeof(OtpCode),
         typeof(Domain.Catalog.InventoryMovement), typeof(Domain.Media.MediaAsset), typeof(Domain.Engage.ProductView),
         typeof(Domain.Engage.SearchLog), typeof(Domain.Engage.Wishlist), typeof(Domain.Engage.ShopFollower),
         // Orders keep their own status history; carts, payments and ledgers are their own journal
         typeof(Domain.Sales.Cart), typeof(Domain.Sales.CartItem), typeof(Domain.Sales.CheckoutSession), typeof(Domain.Sales.Order),
         typeof(Domain.Sales.OrderItem), typeof(Domain.Sales.OrderItemDiscount), typeof(Domain.Sales.OrderStatusHistory),
         typeof(Domain.Sales.Payment), typeof(Domain.Sales.PaymentWebhookEvent), typeof(Domain.Promo.VoucherClaim),
         typeof(Domain.Promo.VoucherUsage), typeof(Domain.Promo.VoucherUserCounter), typeof(Domain.Promo.CoinEntry),
         typeof(Domain.Logistics.ShippingRate), typeof(SimulatedPayment), typeof(Domain.Logistics.Shipment), typeof(Domain.Logistics.ShipmentEvent),
         typeof(Domain.Sales.OrderCancelRequest), typeof(Domain.Sales.Refund), typeof(Domain.Engage.Notification), typeof(Domain.Shops.ShopPenalty)];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext context)
    {
        var now = clock.UtcNow;
        var userId = currentUser.UserId;
        var logs = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged) continue;

            var action = entry.State switch
            {
                EntityState.Added => AuditActions.Create,
                EntityState.Deleted => AuditActions.Delete,
                _ => AuditActions.Update,
            };

            if (entry.Entity is IAuditable auditable)
            {
                if (entry.State == EntityState.Added)
                {
                    auditable.CreatedAt = now;
                    auditable.CreatedBy = userId;
                }
                else
                {
                    auditable.UpdatedAt = now;
                    auditable.UpdatedBy = userId;
                }
            }

            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable softDeletable)
            {
                entry.State = EntityState.Modified;
                softDeletable.DeletedAt = now;
            }

            if (NotAudited.Contains(entry.Entity.GetType())) continue;

            var (oldValues, newValues) = Snapshot(entry, action);
            if (action == AuditActions.Update && newValues.Count == 0) continue;

            logs.Add(new AuditLog(
                userId,
                currentUser.IpAddress,
                Truncate(currentUser.UserAgent, 500),
                action,
                entry.Metadata.ClrType.Name,
                entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString(),
                oldValues.Count > 0 ? JsonSerializer.Serialize(oldValues) : null,
                newValues.Count > 0 ? JsonSerializer.Serialize(newValues) : null,
                now));
        }

        if (logs.Count > 0) context.Set<AuditLog>().AddRange(logs);
    }

    private static (Dictionary<string, object?> Old, Dictionary<string, object?> New) Snapshot(EntityEntry entry, string action)
    {
        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();

        foreach (var prop in entry.Properties)
        {
            var name = prop.Metadata.Name;
            // Bookkeeping columns would only add noise to the diff
            if (name is nameof(IAuditable.UpdatedAt) or nameof(IAuditable.UpdatedBy) or "Version") continue;

            var sensitive = IsSensitive(name);
            switch (action)
            {
                case AuditActions.Create:
                    newValues[name] = sensitive ? "***" : prop.CurrentValue;
                    break;
                case AuditActions.Delete:
                    oldValues[name] = sensitive ? "***" : prop.OriginalValue;
                    break;
                default:
                    if (!prop.IsModified || Equals(prop.OriginalValue, prop.CurrentValue)) continue;
                    oldValues[name] = sensitive ? "***" : prop.OriginalValue;
                    newValues[name] = sensitive ? "***" : prop.CurrentValue;
                    break;
            }
        }

        return (oldValues, newValues);
    }

    private static bool IsSensitive(string propertyName)
    {
        var lower = propertyName.ToLowerInvariant();
        return SensitiveFragments.Any(lower.Contains);
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
