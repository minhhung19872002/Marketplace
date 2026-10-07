using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;

namespace ShopHub.Infrastructure.Persistence.Configurations;

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> b)
    {
        b.ToTable("shipments", "logistics", t => t.HasCheckConstraint("ck_shipments_amounts", "fee >= 0 AND cod_amount >= 0 AND weight_g > 0"));
        b.HasKey(s => s.Id);
        b.Property(s => s.CarrierCode).HasMaxLength(30).IsRequired();
        b.Property(s => s.TrackingNo).HasMaxLength(40).IsRequired();
        b.Property(s => s.Direction).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.PickupMethod).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.PickupSlot).HasMaxLength(60);
        b.Property(s => s.Version).IsRowVersion();
        b.Ignore(s => s.IsFinal);
        b.HasOne<Order>().WithMany().HasForeignKey(s => s.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(s => s.Events).WithOne().HasForeignKey(e => e.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(s => s.TrackingNo).IsUnique().HasDatabaseName("ux_shipments_tracking");
        b.HasIndex(s => new { s.OrderId, s.Direction }).HasDatabaseName("ix_shipments_order");
        b.HasIndex(s => s.ReturnId).HasDatabaseName("ix_shipments_return");
        b.HasIndex(s => new { s.Status, s.LastEventAt }).HasDatabaseName("ix_shipments_open");
    }
}

internal sealed class ShipmentEventConfiguration : IEntityTypeConfiguration<ShipmentEvent>
{
    public void Configure(EntityTypeBuilder<ShipmentEvent> b)
    {
        b.ToTable("shipment_events", "logistics");
        b.HasKey(e => e.Id);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Location).HasMaxLength(200);
        b.Property(e => e.Description).HasMaxLength(300).IsRequired();
        b.Property(e => e.ExternalId).HasMaxLength(100).IsRequired();
        b.Property(e => e.Raw).HasColumnType("jsonb");
        // A carrier notification delivered twice is stored once
        b.HasIndex(e => new { e.ShipmentId, e.ExternalId }).IsUnique().HasDatabaseName("ux_shipment_events_external");
    }
}

internal sealed class OrderCancelRequestConfiguration : IEntityTypeConfiguration<OrderCancelRequest>
{
    public void Configure(EntityTypeBuilder<OrderCancelRequest> b)
    {
        b.ToTable("order_cancel_requests", "sales");
        b.HasKey(r => r.Id);
        b.Property(r => r.Reason).HasMaxLength(300).IsRequired();
        b.Property(r => r.RejectReason).HasMaxLength(300);
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        b.HasOne<Order>().WithMany().HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(r => r.BuyerId).OnDelete(DeleteBehavior.Restrict);
        // At most one open request per order
        b.HasIndex(r => r.OrderId).IsUnique().HasFilter("status = 'Pending'").HasDatabaseName("ux_cancel_requests_open");
        b.HasIndex(r => new { r.Status, r.DueAt }).HasDatabaseName("ix_cancel_requests_due");
    }
}

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> b)
    {
        b.ToTable("refunds", "sales", t => t.HasCheckConstraint("ck_refunds_amount", "amount > 0"));
        b.HasKey(r => r.Id);
        b.Property(r => r.Destination).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Reason).HasMaxLength(300).IsRequired();
        b.Property(r => r.ProviderRef).HasMaxLength(100);
        b.HasOne<Order>().WithMany().HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Payment>().WithMany().HasForeignKey(r => r.PaymentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(r => r.OrderId).HasDatabaseName("ix_refunds_order");
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications", "engage");
        b.HasKey(n => n.Id);
        b.Property(n => n.Category).HasConversion<string>().HasMaxLength(20);
        b.Property(n => n.Title).HasMaxLength(150).IsRequired();
        b.Property(n => n.Body).HasMaxLength(500).IsRequired();
        b.Property(n => n.Link).HasMaxLength(300);
        b.Property(n => n.RefType).HasMaxLength(30);
        b.Property(n => n.DedupeKey).HasMaxLength(120);
        b.HasIndex(n => new { n.UserId, n.DedupeKey }).IsUnique().HasFilter("dedupe_key IS NOT NULL").HasDatabaseName("ux_notifications_dedupe");
        // One promotion a day per person (spec VII): the database keeps it, not a read-then-write
        b.HasIndex(n => new { n.UserId, n.PromoDay }).IsUnique().HasFilter("promo_day IS NOT NULL").HasDatabaseName("ux_notifications_promo_day");
        b.HasOne<User>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(n => new { n.UserId, n.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_notifications_user");
        b.HasIndex(n => new { n.UserId, n.IsRead }).HasDatabaseName("ix_notifications_unread");
    }
}

internal sealed class ShopPenaltyConfiguration : IEntityTypeConfiguration<ShopPenalty>
{
    public void Configure(EntityTypeBuilder<ShopPenalty> b)
    {
        b.ToTable("shop_penalties", "shop", t => t.HasCheckConstraint("ck_shop_penalties_points", "points > 0"));
        b.HasKey(p => p.Id);
        b.Property(p => p.Reason).HasMaxLength(300).IsRequired();
        b.Property(p => p.RevokeReason).HasMaxLength(300);
        b.HasOne<Shop>().WithMany().HasForeignKey(p => p.ShopId).OnDelete(DeleteBehavior.Cascade);
        // One penalty per order and reason (re-running a job never doubles it)
        b.HasIndex(p => new { p.OrderId, p.Reason }).IsUnique().HasFilter("order_id IS NOT NULL").HasDatabaseName("ux_shop_penalties_order");
        b.HasIndex(p => new { p.ShopId, p.CreatedAt }).HasDatabaseName("ix_shop_penalties_shop");
    }
}
