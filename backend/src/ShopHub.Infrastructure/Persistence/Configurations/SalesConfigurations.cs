using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;
using ShopHub.Domain.SystemConfig;

namespace ShopHub.Infrastructure.Persistence.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> b)
    {
        b.ToTable("carts", "sales", t => t.HasCheckConstraint("ck_carts_owner", "(user_id IS NULL) <> (guest_token IS NULL)"));
        b.HasKey(c => c.Id);
        b.Property(c => c.GuestToken).HasMaxLength(64);
        b.Property(c => c.Version).IsRowVersion();
        b.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(c => c.UserId).IsUnique().HasFilter("user_id IS NOT NULL").HasDatabaseName("ux_carts_user");
        b.HasIndex(c => c.GuestToken).IsUnique().HasFilter("guest_token IS NOT NULL").HasDatabaseName("ux_carts_guest");
        b.HasMany(c => c.Items).WithOne().HasForeignKey(i => i.CartId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(c => c.Items).AutoInclude(false);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> b)
    {
        b.ToTable("cart_items", "sales", t => t.HasCheckConstraint("ck_cart_items_quantity", $"quantity BETWEEN 1 AND {CartItem.MaxQuantity}"));
        b.HasKey(i => i.Id);
        b.HasOne<Sku>().WithMany().HasForeignKey(i => i.SkuId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(i => new { i.CartId, i.SkuId }).IsUnique().HasDatabaseName("ux_cart_items_cart_sku");
    }
}

internal sealed class CheckoutSessionConfiguration : IEntityTypeConfiguration<CheckoutSession>
{
    public void Configure(EntityTypeBuilder<CheckoutSession> b)
    {
        b.ToTable("checkout_sessions", "sales", t =>
        {
            t.HasCheckConstraint("ck_checkout_totals", "subtotal >= 0 AND shipping_fee >= 0 AND shipping_discount >= 0 AND discount_total >= 0 AND coin_used >= 0 AND grand_total >= 0");
        });
        b.HasKey(c => c.Id);
        b.Property(c => c.IdempotencyKey).HasMaxLength(100).IsRequired();
        b.Property(c => c.AddressSnapshot).HasColumnType("jsonb").IsRequired();
        b.Property(c => c.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.Version).IsRowVersion();
        b.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
        // One checkout per Idempotency-Key (spec 3.5)
        b.HasIndex(c => new { c.UserId, c.IdempotencyKey }).IsUnique().HasDatabaseName("ux_checkout_idem");
        b.HasIndex(c => new { c.Status, c.PaymentExpiresAt }).HasDatabaseName("ix_checkout_awaiting");
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders", "sales", t =>
        {
            t.HasCheckConstraint("ck_orders_amounts",
                "subtotal >= 0 AND shop_discount >= 0 AND platform_discount >= 0 AND shipping_fee >= 0 AND shipping_discount >= 0 AND coin_used >= 0 AND grand_total >= 0 AND shipping_discount <= shipping_fee");
            t.HasCheckConstraint("ck_orders_total",
                "grand_total = subtotal - shop_discount - platform_discount + shipping_fee - shipping_discount - coin_used");
        });
        b.HasKey(o => o.Id);
        b.Property(o => o.Code).HasMaxLength(20).IsRequired();
        b.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(o => o.PaymentStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(o => o.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        b.Property(o => o.CancelledBy).HasConversion<string>().HasMaxLength(20);
        b.Property(o => o.CarrierCode).HasMaxLength(30).IsRequired();
        b.Property(o => o.BuyerNote).HasMaxLength(500);
        b.Property(o => o.CancelReason).HasMaxLength(500);
        b.Property(o => o.Version).IsRowVersion();
        b.HasOne<CheckoutSession>().WithMany().HasForeignKey(o => o.CheckoutId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(o => o.BuyerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Shop>().WithMany().HasForeignKey(o => o.ShopId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(o => o.History).WithOne().HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(o => o.Code).IsUnique().HasDatabaseName("ux_orders_code");
        b.HasIndex(o => new { o.BuyerId, o.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_orders_buyer");
        b.HasIndex(o => new { o.ShopId, o.Status, o.CreatedAt }).IsDescending(false, false, true).HasDatabaseName("ix_orders_shop_status");
        b.HasIndex(o => o.CheckoutId).HasDatabaseName("ix_orders_checkout");
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("order_items", "sales", t =>
        {
            t.HasCheckConstraint("ck_order_items_quantity", "quantity > 0");
            t.HasCheckConstraint("ck_order_items_total", "line_total = unit_price * quantity AND unit_price >= 0");
        });
        b.HasKey(i => i.Id);
        b.Property(i => i.NameSnapshot).HasMaxLength(Product.MaxNameLength).IsRequired();
        b.Property(i => i.VariantSnapshot).HasMaxLength(100);
        b.Property(i => i.ImageSnapshot).HasMaxLength(500);
        b.Ignore(i => i.PaidAmount);
        b.HasOne<Sku>().WithMany().HasForeignKey(i => i.SkuId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(i => i.Discounts).WithOne().HasForeignKey(d => d.OrderItemId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(i => i.SkuId).HasDatabaseName("ix_order_items_sku");
    }
}

internal sealed class OrderItemDiscountConfiguration : IEntityTypeConfiguration<OrderItemDiscount>
{
    public void Configure(EntityTypeBuilder<OrderItemDiscount> b)
    {
        b.ToTable("order_item_discounts", "sales", t => t.HasCheckConstraint("ck_order_item_discounts_amount", "amount >= 0"));
        b.HasKey(d => d.Id);
        b.Property(d => d.Source).HasConversion<string>().HasMaxLength(20);
    }
}

internal sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> b)
    {
        b.ToTable("order_status_history", "sales");
        b.HasKey(h => h.Id);
        b.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(h => h.ActorType).HasConversion<string>().HasMaxLength(20);
        b.Property(h => h.Reason).HasMaxLength(500);
        b.HasIndex(h => new { h.OrderId, h.OccurredAt }).HasDatabaseName("ix_order_status_history_order");
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments", "sales", t => t.HasCheckConstraint("ck_payments_amount", "amount > 0"));
        b.HasKey(p => p.Id);
        b.Property(p => p.Method).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.ProviderTxnId).HasMaxLength(100);
        b.Property(p => p.Raw).HasColumnType("jsonb");
        b.Property(p => p.FailureReason).HasMaxLength(300);
        b.Property(p => p.RedirectUrl).HasMaxLength(1000);
        b.Property(p => p.Version).IsRowVersion();
        b.HasOne<CheckoutSession>().WithMany().HasForeignKey(p => p.CheckoutId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => new { p.Method, p.ProviderTxnId }).IsUnique().HasFilter("provider_txn_id IS NOT NULL").HasDatabaseName("ux_payment_txn");
        b.HasIndex(p => p.CheckoutId).HasDatabaseName("ix_payments_checkout");
    }
}

internal sealed class PaymentWebhookEventConfiguration : IEntityTypeConfiguration<PaymentWebhookEvent>
{
    public void Configure(EntityTypeBuilder<PaymentWebhookEvent> b)
    {
        b.ToTable("payment_webhook_events", "sales");
        b.HasKey(e => e.Id);
        b.Property(e => e.Provider).HasMaxLength(30).IsRequired();
        b.Property(e => e.EventId).HasMaxLength(100).IsRequired();
        b.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Result).HasMaxLength(100);
        // A notification is processed exactly once (spec IV)
        b.HasIndex(e => new { e.Provider, e.EventId }).IsUnique().HasDatabaseName("ux_webhook_event");
    }
}

internal sealed class VoucherConfiguration : IEntityTypeConfiguration<Voucher>
{
    public void Configure(EntityTypeBuilder<Voucher> b)
    {
        b.ToTable("vouchers", "promo", t =>
        {
            // The quota can never be exceeded, whatever the application does (spec 6.2)
            t.HasCheckConstraint("ck_vouchers_quota", "used_count >= 0 AND (total_quota IS NULL OR used_count <= total_quota)");
            t.HasCheckConstraint("ck_vouchers_period", "end_at > start_at");
            t.HasCheckConstraint("ck_vouchers_owner", "(owner = 'Shop') = (shop_id IS NOT NULL)");
        });
        b.HasKey(v => v.Id);
        b.Property(v => v.Owner).HasConversion<string>().HasMaxLength(20);
        b.Property(v => v.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(v => v.Audience).HasConversion<string>().HasMaxLength(20);
        b.Property(v => v.Channel).HasConversion<string>().HasMaxLength(10);
        b.Property(v => v.Code).HasMaxLength(Voucher.MaxCodeLength).IsRequired();
        b.Property(v => v.Name).HasMaxLength(100).IsRequired();
        b.Property(v => v.CategoryIds).HasColumnType("uuid[]");
        b.Property(v => v.ProductIds).HasColumnType("uuid[]");
        b.HasOne<Shop>().WithMany().HasForeignKey(v => v.ShopId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(v => v.Code).IsUnique().HasFilter("deleted_at IS NULL").HasDatabaseName("ux_voucher_code");
        b.HasIndex(v => new { v.Owner, v.ShopId, v.EndAt }).HasDatabaseName("ix_vouchers_owner");
    }
}

internal sealed class VoucherClaimConfiguration : IEntityTypeConfiguration<VoucherClaim>
{
    public void Configure(EntityTypeBuilder<VoucherClaim> b)
    {
        b.ToTable("voucher_claims", "promo");
        b.HasKey(c => c.Id);
        b.HasOne<Voucher>().WithMany().HasForeignKey(c => c.VoucherId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(c => new { c.VoucherId, c.UserId }).IsUnique().HasDatabaseName("ux_voucher_claims");
        b.HasIndex(c => c.UserId).HasDatabaseName("ix_voucher_claims_user");
    }
}

internal sealed class VoucherUsageConfiguration : IEntityTypeConfiguration<VoucherUsage>
{
    public void Configure(EntityTypeBuilder<VoucherUsage> b)
    {
        b.ToTable("voucher_usages", "promo", t => t.HasCheckConstraint("ck_voucher_usages_amount", "amount >= 0"));
        b.HasKey(u => u.Id);
        b.HasOne<Voucher>().WithMany().HasForeignKey(u => u.VoucherId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CheckoutSession>().WithMany().HasForeignKey(u => u.CheckoutId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(u => new { u.VoucherId, u.UserId }).HasDatabaseName("ix_voucher_usages_user");
        b.HasIndex(u => u.CheckoutId).HasDatabaseName("ix_voucher_usages_checkout");
    }
}

internal sealed class VoucherUserCounterConfiguration : IEntityTypeConfiguration<VoucherUserCounter>
{
    public void Configure(EntityTypeBuilder<VoucherUserCounter> b)
    {
        b.ToTable("voucher_user_counters", "promo", t => t.HasCheckConstraint("ck_voucher_user_counters_used", "used_count >= 0"));
        b.HasKey(c => new { c.VoucherId, c.UserId });
        b.HasOne<Voucher>().WithMany().HasForeignKey(c => c.VoucherId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CoinEntryConfiguration : IEntityTypeConfiguration<CoinEntry>
{
    public void Configure(EntityTypeBuilder<CoinEntry> b)
    {
        b.ToTable("coin_ledger", "promo", t => t.HasCheckConstraint("ck_coin_ledger_delta", "delta <> 0"));
        b.HasKey(c => c.Id);
        b.Property(c => c.Reason).HasConversion<string>().HasMaxLength(30);
        b.Property(c => c.RefType).HasMaxLength(30);
        b.Property(c => c.Note).HasMaxLength(300);
        b.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(c => new { c.UserId, c.CreatedAt }).HasDatabaseName("ix_coin_ledger_user");
    }
}

internal sealed class CarrierConfiguration : IEntityTypeConfiguration<Carrier>
{
    public void Configure(EntityTypeBuilder<Carrier> b)
    {
        b.ToTable("carriers", "logistics");
        b.HasKey(c => c.Id);
        b.Property(c => c.Code).HasMaxLength(30).IsRequired();
        b.Property(c => c.Name).HasMaxLength(100).IsRequired();
        b.Property(c => c.Provider).HasMaxLength(30).IsRequired();
        b.Property(c => c.Description).HasMaxLength(300);
        b.HasIndex(c => c.Code).IsUnique().HasDatabaseName("ux_carriers_code");
    }
}

internal sealed class ShippingRateConfiguration : IEntityTypeConfiguration<ShippingRate>
{
    public void Configure(EntityTypeBuilder<ShippingRate> b)
    {
        b.ToTable("shipping_rates", "logistics", t =>
            t.HasCheckConstraint("ck_shipping_rates_band", "weight_from_g >= 0 AND (weight_to_g IS NULL OR weight_to_g > weight_from_g) AND fee >= 0"));
        b.HasKey(r => r.Id);
        b.Property(r => r.Zone).HasConversion<string>().HasMaxLength(20);
        b.HasOne<Carrier>().WithMany().HasForeignKey(r => r.CarrierId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => new { r.CarrierId, r.Zone, r.WeightFromG }).IsUnique().HasDatabaseName("ux_shipping_rates_band");
    }
}

internal sealed class SimulatedPaymentConfiguration : IEntityTypeConfiguration<SimulatedPayment>
{
    public void Configure(EntityTypeBuilder<SimulatedPayment> b)
    {
        b.ToTable("simulated_payments", "sys");
        b.HasKey(p => p.Id);
        b.Property(p => p.TxnId).HasMaxLength(40).IsRequired();
        b.Property(p => p.Outcome).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(p => p.PaymentId).HasDatabaseName("ix_simulated_payments_payment");
    }
}
