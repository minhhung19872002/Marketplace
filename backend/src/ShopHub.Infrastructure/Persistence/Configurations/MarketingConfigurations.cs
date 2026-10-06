using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Shops;

namespace ShopHub.Infrastructure.Persistence.Configurations;

internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> b)
    {
        b.ToTable("promotions", "promo", t => t.HasCheckConstraint("ck_promotions_period", "end_at > start_at"));
        b.HasKey(p => p.Id);
        b.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.Name).HasMaxLength(150).IsRequired();
        b.Property(p => p.Version).IsRowVersion();
        b.HasOne<Shop>().WithMany().HasForeignKey(p => p.ShopId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(p => p.Products).WithOne().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(p => p.Skus).WithOne().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(p => new { p.ShopId, p.Status, p.EndAt }).HasDatabaseName("ix_promotions_shop");
    }
}

internal sealed class PromotionProductConfiguration : IEntityTypeConfiguration<PromotionProduct>
{
    public void Configure(EntityTypeBuilder<PromotionProduct> b)
    {
        b.ToTable("promotion_products", "promo");
        b.HasKey(p => p.Id);
        b.HasOne<Product>().WithMany().HasForeignKey(p => p.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => new { p.PromotionId, p.ProductId }).IsUnique().HasDatabaseName("ux_promotion_products");
        b.HasIndex(p => p.ProductId).HasDatabaseName("ix_promotion_products_product");
    }
}

internal sealed class PromotionSkuConfiguration : IEntityTypeConfiguration<PromotionSku>
{
    public void Configure(EntityTypeBuilder<PromotionSku> b)
    {
        b.ToTable("promotion_skus", "promo", t => t.HasCheckConstraint("ck_promotion_skus_price", "price > 0"));
        b.HasKey(p => p.Id);
        b.HasOne<Sku>().WithMany().HasForeignKey(p => p.SkuId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => new { p.PromotionId, p.SkuId }).IsUnique().HasDatabaseName("ux_promotion_skus");
        b.HasIndex(p => p.SkuId).HasDatabaseName("ix_promotion_skus_sku");
    }
}

internal sealed class PriceProgramConfiguration : IEntityTypeConfiguration<PriceProgram>
{
    public void Configure(EntityTypeBuilder<PriceProgram> b)
    {
        // The exclusion constraint ex_price_programs_sku_period (one price programme per SKU at a time) is created in the migration
        b.ToTable("price_programs", "promo", t =>
        {
            t.HasCheckConstraint("ck_price_programs_period", "end_at > start_at");
            t.HasCheckConstraint("ck_price_programs_price", "price > 0");
        });
        b.HasKey(p => p.Id);
        b.Property(p => p.Kind).HasConversion<string>().HasMaxLength(20);
        b.HasOne<Sku>().WithMany().HasForeignKey(p => p.SkuId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => new { p.SkuId, p.StartAt, p.EndAt }).HasFilter("is_active").HasDatabaseName("ix_price_programs_sku");
        b.HasIndex(p => new { p.Kind, p.RefId }).HasDatabaseName("ix_price_programs_ref");
    }
}

internal sealed class FlashSaleSlotConfiguration : IEntityTypeConfiguration<FlashSaleSlot>
{
    public void Configure(EntityTypeBuilder<FlashSaleSlot> b)
    {
        b.ToTable("flash_sale_slots", "promo", t => t.HasCheckConstraint("ck_flash_sale_slots_period", "end_at > start_at"));
        b.HasKey(s => s.Id);
        b.Property(s => s.Owner).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.CategoryIds).HasColumnType("uuid[]");
        b.HasOne<Shop>().WithMany().HasForeignKey(s => s.ShopId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(s => new { s.Owner, s.StartAt }).HasDatabaseName("ix_flash_sale_slots_start");
    }
}

internal sealed class FlashSaleItemConfiguration : IEntityTypeConfiguration<FlashSaleItem>
{
    public void Configure(EntityTypeBuilder<FlashSaleItem> b)
    {
        // sold never above quota: the database guard behind the Redis counter
        b.ToTable("flash_sale_items", "promo", t => t.HasCheckConstraint("ck_flash_sale_items_sold", "sold >= 0 AND sold <= quota"));
        b.HasKey(i => i.Id);
        b.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(i => i.RejectReason).HasMaxLength(300);
        b.HasOne<FlashSaleSlot>().WithMany().HasForeignKey(i => i.SlotId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Sku>().WithMany().HasForeignKey(i => i.SkuId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(i => new { i.SlotId, i.SkuId }).IsUnique().HasDatabaseName("ux_flash_sale_items_slot_sku");
        b.HasIndex(i => new { i.ShopId, i.CreatedAt }).HasDatabaseName("ix_flash_sale_items_shop");
    }
}

internal sealed class FlashSaleBuyerConfiguration : IEntityTypeConfiguration<FlashSaleBuyer>
{
    public void Configure(EntityTypeBuilder<FlashSaleBuyer> b)
    {
        b.ToTable("flash_sale_buyers", "promo", t => t.HasCheckConstraint("ck_flash_sale_buyers_quantity", "quantity >= 0"));
        b.HasKey(x => x.Id);
        b.HasOne<FlashSaleItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ItemId, x.UserId }).IsUnique().HasDatabaseName("ux_flash_sale_buyers");
    }
}

internal sealed class BannerConfiguration : IEntityTypeConfiguration<Banner>
{
    public void Configure(EntityTypeBuilder<Banner> b)
    {
        b.ToTable("banners", "promo", t => t.HasCheckConstraint("ck_banners_period", "end_at > start_at"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Position).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Title).HasMaxLength(120).IsRequired();
        b.Property(x => x.ImageUrl).HasMaxLength(500).IsRequired();
        b.Property(x => x.Link).HasMaxLength(500).IsRequired();
        b.HasIndex(x => new { x.Position, x.StartAt, x.EndAt }).HasDatabaseName("ix_banners_position");
    }
}

internal sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public void Configure(EntityTypeBuilder<Campaign> b)
    {
        b.ToTable("campaigns", "promo", t => t.HasCheckConstraint("ck_campaigns_period", "end_at > start_at"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        b.Property(x => x.Blocks).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, Json),
            v => JsonSerializer.Deserialize<List<CampaignBlock>>(v, Json) ?? new List<CampaignBlock>(),
            new ValueComparer<List<CampaignBlock>>((a, c) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(c, Json),
                v => JsonSerializer.Serialize(v, Json).GetHashCode(), v => v.ToList()));
        b.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("ux_campaigns_slug");
    }
}

internal sealed class CheckInConfiguration : IEntityTypeConfiguration<CheckIn>
{
    public void Configure(EntityTypeBuilder<CheckIn> b)
    {
        b.ToTable("check_ins", "engage", t => t.HasCheckConstraint("ck_check_ins_streak", "streak_day BETWEEN 1 AND 7"));
        b.HasKey(x => x.Id);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        // One check-in per user per day, even when the button is pressed twice at once
        b.HasIndex(x => new { x.UserId, x.Day }).IsUnique().HasDatabaseName("ux_check_ins_user_day");
    }
}
