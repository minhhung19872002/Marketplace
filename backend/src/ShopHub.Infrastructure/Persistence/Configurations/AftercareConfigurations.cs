using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;

namespace ShopHub.Infrastructure.Persistence.Configurations;

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.ToTable("reviews", "engage", t => t.HasCheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5"));
        b.HasKey(r => r.Id);
        b.Property(r => r.Content).HasMaxLength(Review.MaxContentLength).IsRequired();
        b.Property(r => r.Tags).HasColumnType("text[]");
        b.Property(r => r.VariantSnapshot).HasMaxLength(100);
        b.Property(r => r.SellerReply).HasMaxLength(500);
        b.Property(r => r.HiddenReason).HasMaxLength(300);
        b.HasOne<OrderItem>().WithMany().HasForeignKey(r => r.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(r => r.BuyerId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(r => r.Media).WithOne().HasForeignKey(m => m.ReviewId).OnDelete(DeleteBehavior.Cascade);
        // One review per order line (spec 4.10)
        b.HasIndex(r => r.OrderItemId).IsUnique().HasFilter("deleted_at IS NULL").HasDatabaseName("ux_review_item");
        b.HasIndex(r => new { r.ProductId, r.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_reviews_product");
        b.HasIndex(r => new { r.ShopId, r.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_reviews_shop");
    }
}

internal sealed class ReviewMediaConfiguration : IEntityTypeConfiguration<ReviewMedia>
{
    public void Configure(EntityTypeBuilder<ReviewMedia> b)
    {
        b.ToTable("review_media", "engage");
        b.HasKey(m => m.Id);
        b.Property(m => m.Type).HasConversion<string>().HasMaxLength(10);
        b.Property(m => m.Url).HasMaxLength(500).IsRequired();
    }
}

internal sealed class ReviewReportConfiguration : IEntityTypeConfiguration<ReviewReport>
{
    public void Configure(EntityTypeBuilder<ReviewReport> b)
    {
        b.ToTable("review_reports", "engage");
        b.HasKey(r => r.Id);
        b.Property(r => r.Reason).HasMaxLength(300).IsRequired();
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        b.HasOne<Review>().WithMany().HasForeignKey(r => r.ReviewId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => new { r.ReviewId, r.ReporterId }).IsUnique().HasDatabaseName("ux_review_reports");
        b.HasIndex(r => new { r.Status, r.CreatedAt }).HasDatabaseName("ix_review_reports_status");
    }
}

internal sealed class ReturnRequestConfiguration : IEntityTypeConfiguration<ReturnRequest>
{
    public void Configure(EntityTypeBuilder<ReturnRequest> b)
    {
        b.ToTable("return_requests", "sales", t =>
            t.HasCheckConstraint("ck_returns_amounts", "requested_amount >= 0 AND requested_coins >= 0 AND (refund_amount IS NULL OR refund_amount <= requested_amount)"));
        b.HasKey(r => r.Id);
        b.Property(r => r.Code).HasMaxLength(20).IsRequired();
        b.Property(r => r.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Reason).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Description).HasMaxLength(1000).IsRequired();
        b.Property(r => r.ShopNote).HasMaxLength(500);
        b.Property(r => r.PlatformBorne).HasDefaultValue(false);
        b.Property(r => r.Version).IsRowVersion();
        b.Ignore(r => r.IsOpen);
        b.HasOne<Order>().WithMany().HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Shop>().WithMany().HasForeignKey(r => r.ShopId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(r => r.Items).WithOne().HasForeignKey(i => i.ReturnId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(r => r.Evidence).WithOne().HasForeignKey(e => e.ReturnId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(r => r.History).WithOne().HasForeignKey(h => h.ReturnId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => r.Code).IsUnique().HasDatabaseName("ux_returns_code");
        b.HasIndex(r => new { r.ShopId, r.Status, r.CreatedAt }).HasDatabaseName("ix_returns_shop");
        b.HasIndex(r => new { r.BuyerId, r.CreatedAt }).HasDatabaseName("ix_returns_buyer");
        b.HasIndex(r => new { r.Status, r.RespondBy }).HasDatabaseName("ix_returns_due");
    }
}

internal sealed class ReturnItemConfiguration : IEntityTypeConfiguration<ReturnItem>
{
    public void Configure(EntityTypeBuilder<ReturnItem> b)
    {
        b.ToTable("return_items", "sales", t => t.HasCheckConstraint("ck_return_items", "quantity > 0 AND refund_amount >= 0 AND refund_coins >= 0"));
        b.HasKey(i => i.Id);
        b.HasOne<OrderItem>().WithMany().HasForeignKey(i => i.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        // One open return per order line (spec 6.2)
        b.HasIndex(i => i.OrderItemId).IsUnique().HasFilter("is_open").HasDatabaseName("ux_return_items_open");
    }
}

internal sealed class ReturnEvidenceConfiguration : IEntityTypeConfiguration<ReturnEvidence>
{
    public void Configure(EntityTypeBuilder<ReturnEvidence> b)
    {
        b.ToTable("return_evidences", "sales");
        b.HasKey(e => e.Id);
        b.Property(e => e.Party).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.Url).HasMaxLength(500).IsRequired();
        b.Property(e => e.Note).HasMaxLength(300);
    }
}

internal sealed class ReturnHistoryConfiguration : IEntityTypeConfiguration<ReturnHistory>
{
    public void Configure(EntityTypeBuilder<ReturnHistory> b)
    {
        b.ToTable("return_history", "sales");
        b.HasKey(h => h.Id);
        b.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(h => h.By).HasConversion<string>().HasMaxLength(10);
        b.Property(h => h.Note).HasMaxLength(500);
    }
}

internal sealed class DisputeConfiguration : IEntityTypeConfiguration<Dispute>
{
    public void Configure(EntityTypeBuilder<Dispute> b)
    {
        b.ToTable("disputes", "sales");
        b.HasKey(d => d.Id);
        b.Property(d => d.Reason).HasMaxLength(1000).IsRequired();
        b.Property(d => d.Decision).HasConversion<string>().HasMaxLength(20);
        b.Property(d => d.DecisionReason).HasMaxLength(1000);
        b.HasOne<ReturnRequest>().WithMany().HasForeignKey(d => d.ReturnId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(d => d.ReturnId).IsUnique().HasDatabaseName("ux_disputes_return");
        b.HasIndex(d => d.ClosedAt).HasDatabaseName("ix_disputes_open");
    }
}
