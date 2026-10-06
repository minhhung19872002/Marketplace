using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Shops;

namespace ShopHub.Infrastructure.Persistence.Configurations;

internal sealed class WishlistConfiguration : IEntityTypeConfiguration<Wishlist>
{
    public void Configure(EntityTypeBuilder<Wishlist> b)
    {
        b.ToTable("wishlists", "engage");
        b.HasKey(w => w.Id);
        b.HasOne<User>().WithMany().HasForeignKey(w => w.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Product>().WithMany().HasForeignKey(w => w.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(w => new { w.UserId, w.ProductId }).IsUnique().HasDatabaseName("ux_wishlists_user_product");
        b.HasIndex(w => w.ProductId).HasDatabaseName("ix_wishlists_product");
    }
}

internal sealed class ShopFollowerConfiguration : IEntityTypeConfiguration<ShopFollower>
{
    public void Configure(EntityTypeBuilder<ShopFollower> b)
    {
        b.ToTable("shop_followers", "shop");
        b.HasKey(f => f.Id);
        b.HasOne<Shop>().WithMany().HasForeignKey(f => f.ShopId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(f => new { f.ShopId, f.UserId }).IsUnique().HasDatabaseName("ux_shop_followers_shop_user");
        b.HasIndex(f => f.UserId).HasDatabaseName("ix_shop_followers_user");
    }
}

internal sealed class CartAddConfiguration : IEntityTypeConfiguration<CartAdd>
{
    public void Configure(EntityTypeBuilder<CartAdd> b)
    {
        b.ToTable("cart_adds", "engage", t => t.HasCheckConstraint("ck_cart_adds_quantity", "quantity > 0"));
        b.HasKey(a => a.Id);
        b.Property(a => a.SessionKey).HasMaxLength(64);
        b.HasOne<Product>().WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(a => a.AddedAt).HasDatabaseName("ix_cart_adds_at");
        b.HasIndex(a => new { a.ProductId, a.AddedAt }).HasDatabaseName("ix_cart_adds_product");
    }
}

internal sealed class ProductReportConfiguration : IEntityTypeConfiguration<ProductReport>
{
    public void Configure(EntityTypeBuilder<ProductReport> b)
    {
        b.ToTable("product_reports", "catalog");
        b.HasKey(r => r.Id);
        b.Property(r => r.Reason).HasConversion<string>().HasMaxLength(30);
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Details).HasMaxLength(1000);
        b.Property(r => r.Resolution).HasMaxLength(500);
        b.HasOne<Product>().WithMany().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Cascade);
        // One open report per reporter and product
        b.HasIndex(r => new { r.ProductId, r.ReporterId }).IsUnique().HasFilter("status = 'Open'").HasDatabaseName("ux_product_reports_open");
        b.HasIndex(r => new { r.Status, r.CreatedAt }).HasDatabaseName("ix_product_reports_status");
    }
}

internal sealed class ProductViewConfiguration : IEntityTypeConfiguration<ProductView>
{
    public void Configure(EntityTypeBuilder<ProductView> b)
    {
        b.ToTable("product_views", "engage");
        b.HasKey(v => v.Id);
        b.Property(v => v.SessionKey).HasMaxLength(64);
        b.Property(v => v.Source).HasConversion<string>().HasMaxLength(20).HasDefaultValue(ViewSource.Direct);
        b.HasOne<Product>().WithMany().HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(v => new { v.UserId, v.ViewedAt }).HasDatabaseName("ix_product_views_user");
        b.HasIndex(v => new { v.SessionKey, v.ViewedAt }).HasDatabaseName("ix_product_views_session");
        b.HasIndex(v => new { v.ProductId, v.ViewedAt }).HasDatabaseName("ix_product_views_product");
    }
}

internal sealed class SearchLogConfiguration : IEntityTypeConfiguration<SearchLog>
{
    public void Configure(EntityTypeBuilder<SearchLog> b)
    {
        b.ToTable("search_logs", "engage");
        b.HasKey(l => l.Id);
        b.Property(l => l.Keyword).HasMaxLength(100).IsRequired();
        b.HasIndex(l => new { l.OccurredAt, l.Keyword }).HasDatabaseName("ix_search_logs_time");
        b.HasIndex(l => l.Keyword).HasDatabaseName("ix_search_logs_keyword").HasOperators("text_pattern_ops");
    }
}
