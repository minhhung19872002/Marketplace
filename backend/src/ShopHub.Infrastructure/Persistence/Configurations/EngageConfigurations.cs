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

internal sealed class ProductViewConfiguration : IEntityTypeConfiguration<ProductView>
{
    public void Configure(EntityTypeBuilder<ProductView> b)
    {
        b.ToTable("product_views", "engage");
        b.HasKey(v => v.Id);
        b.Property(v => v.SessionKey).HasMaxLength(64);
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
