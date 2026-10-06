using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Media;
using ShopHub.Domain.Shops;

namespace ShopHub.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("categories", "catalog", t => t.HasCheckConstraint("ck_categories_level", "level BETWEEN 1 AND 3"));
        b.HasKey(c => c.Id);
        b.Property(c => c.Name).HasMaxLength(100).IsRequired();
        b.Property(c => c.Slug).HasMaxLength(120).IsRequired();
        b.Property(c => c.IconUrl).HasMaxLength(500);
        b.HasOne<Category>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
        // Globally unique: category pages live at /danh-muc/{slug}
        b.HasIndex(c => c.Slug).IsUnique().HasFilter("deleted_at IS NULL").HasDatabaseName("ux_categories_slug");
    }
}

internal sealed class CategoryAttributeConfiguration : IEntityTypeConfiguration<CategoryAttribute>
{
    public void Configure(EntityTypeBuilder<CategoryAttribute> b)
    {
        b.ToTable("category_attributes", "catalog");
        b.HasKey(a => a.Id);
        b.Property(a => a.Name).HasMaxLength(100).IsRequired();
        b.Property(a => a.InputType).HasConversion<string>().HasMaxLength(20);
        b.Property(a => a.Unit).HasMaxLength(20);
        b.Property(a => a.Options).HasColumnType("text[]");
        b.HasOne<Category>().WithMany().HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(a => a.CategoryId).HasDatabaseName("ix_category_attributes_category");
    }
}

internal sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> b)
    {
        b.ToTable("brands", "catalog");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        b.Property(x => x.LogoUrl).HasMaxLength(500);
        b.HasIndex(x => x.Slug).IsUnique().HasFilter("deleted_at IS NULL").HasDatabaseName("ux_brands_slug");
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products", "catalog", t =>
        {
            t.HasCheckConstraint("ck_products_weight", "weight_g > 0");
            t.HasCheckConstraint("ck_products_price_range", "min_price >= 0 AND max_price >= min_price");
        });
        b.HasKey(p => p.Id);
        b.Property(p => p.Name).HasMaxLength(Product.MaxNameLength).IsRequired();
        b.Property(p => p.Slug).HasMaxLength(120).IsRequired();
        b.Property(p => p.Description).IsRequired();
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.Condition).HasConversion<string>().HasMaxLength(10);
        b.Property(p => p.BanReason).HasMaxLength(500);
        b.Property(p => p.ReviewNote).HasMaxLength(1000);
        b.Property(p => p.Flags).HasMaxLength(500);
        b.Property(p => p.Version).IsRowVersion();

        b.HasOne<Shop>().WithMany().HasForeignKey(p => p.ShopId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Category>().WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Brand>().WithMany().HasForeignKey(p => p.BrandId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(p => p.Tiers).WithOne().HasForeignKey(t => t.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(p => p.Skus).WithOne().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(p => p.Media).WithOne().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(p => p.Attributes).WithOne().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Tiers).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(p => p.Skus).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(p => p.Media).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(p => p.Attributes).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(p => new { p.ShopId, p.Status, p.UpdatedAt }).HasDatabaseName("ix_products_shop_status");
        b.HasIndex(p => new { p.Status, p.SubmittedAt }).HasDatabaseName("ix_products_status_submitted");
        // "Gợi ý hôm nay" / best-seller lists walk this instead of sorting the catalogue (spec 6.3, 1 triệu sản phẩm)
        b.HasIndex(p => new { p.SoldCount, p.RatingAvg, p.PublishedAt, p.Id }).IsDescending(true, true, true, false)
            .HasFilter("status = 'Active' AND deleted_at IS NULL").HasDatabaseName("ix_products_best_selling");
        b.HasIndex(p => p.CategoryId).HasDatabaseName("ix_products_category");
    }
}

internal sealed class VariantTierConfiguration : IEntityTypeConfiguration<VariantTier>
{
    public void Configure(EntityTypeBuilder<VariantTier> b)
    {
        b.ToTable("variant_tiers", "catalog", t => t.HasCheckConstraint("ck_variant_tiers_index", "tier_index IN (0, 1)"));
        b.HasKey(t => t.Id);
        b.Property(t => t.Name).HasMaxLength(50).IsRequired();
        b.HasIndex(t => new { t.ProductId, t.TierIndex }).IsUnique().HasDatabaseName("ux_variant_tiers_product_index");
        b.HasMany(t => t.Options).WithOne().HasForeignKey(o => o.TierId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(t => t.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class VariantOptionConfiguration : IEntityTypeConfiguration<VariantOption>
{
    public void Configure(EntityTypeBuilder<VariantOption> b)
    {
        b.ToTable("variant_options", "catalog");
        b.HasKey(o => o.Id);
        b.Property(o => o.Value).HasMaxLength(50).IsRequired();
        b.Property(o => o.ImageUrl).HasMaxLength(500);
        b.HasIndex(o => new { o.TierId, o.Value }).IsUnique().HasDatabaseName("ux_variant_options_tier_value");
    }
}

internal sealed class SkuConfiguration : IEntityTypeConfiguration<Sku>
{
    public void Configure(EntityTypeBuilder<Sku> b)
    {
        // Stock can never go negative nor drop below what is reserved for orders — enforced by the database
        b.ToTable("skus", "catalog", t =>
        {
            t.HasCheckConstraint("ck_skus_reserved", "reserved >= 0");
            t.HasCheckConstraint("ck_skus_stock", "stock >= reserved");
            t.HasCheckConstraint("ck_skus_price", "price > 0 AND original_price >= price");
        });
        b.HasKey(s => s.Id);
        b.Property(s => s.SellerSku).HasMaxLength(50);
        b.Property(s => s.Version).IsRowVersion();
        b.Ignore(s => s.Available);
        b.HasOne<VariantOption>().WithMany().HasForeignKey(s => s.Option1Id).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<VariantOption>().WithMany().HasForeignKey(s => s.Option2Id).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(s => new { s.ProductId, s.Option1Id, s.Option2Id }).IsUnique().AreNullsDistinct(false)
            .HasDatabaseName("ux_skus_product_options");
    }
}

internal sealed class ProductMediaConfiguration : IEntityTypeConfiguration<ProductMedia>
{
    public void Configure(EntityTypeBuilder<ProductMedia> b)
    {
        b.ToTable("product_media", "catalog");
        b.HasKey(m => m.Id);
        b.Property(m => m.Type).HasConversion<string>().HasMaxLength(10);
        b.Property(m => m.Url).HasMaxLength(500).IsRequired();
        b.HasIndex(m => new { m.ProductId, m.SortOrder }).HasDatabaseName("ix_product_media_product");
    }
}

internal sealed class ProductAttributeConfiguration : IEntityTypeConfiguration<ProductAttribute>
{
    public void Configure(EntityTypeBuilder<ProductAttribute> b)
    {
        b.ToTable("product_attributes", "catalog");
        b.HasKey(a => new { a.ProductId, a.AttributeId });
        b.Property(a => a.Values).HasColumnType("text[]");
        b.HasOne<CategoryAttribute>().WithMany().HasForeignKey(a => a.AttributeId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> b)
    {
        b.ToTable("inventory_movements", "catalog");
        b.HasKey(m => m.Id);
        b.Property(m => m.Reason).HasConversion<string>().HasMaxLength(20);
        b.Property(m => m.RefType).HasMaxLength(30);
        b.Property(m => m.Note).HasMaxLength(200);
        b.HasOne<Sku>().WithMany().HasForeignKey(m => m.SkuId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(m => new { m.SkuId, m.OccurredAt }).HasDatabaseName("ix_inventory_movements_sku");
    }
}

internal sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> b)
    {
        b.ToTable("media_assets", "catalog");
        b.HasKey(a => a.Id);
        b.Property(a => a.Kind).HasConversion<string>().HasMaxLength(10);
        b.Property(a => a.Purpose).HasMaxLength(20).IsRequired();
        b.Property(a => a.Bucket).HasMaxLength(50).IsRequired();
        b.Property(a => a.ObjectKey).HasMaxLength(300).IsRequired();
        b.Property(a => a.ContentType).HasMaxLength(50).IsRequired();
        b.HasOne<User>().WithMany().HasForeignKey(a => a.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(a => new { a.OwnerUserId, a.CreatedAt }).HasDatabaseName("ix_media_assets_owner");
    }
}

internal sealed class ShopConfiguration : IEntityTypeConfiguration<Shop>
{
    public void Configure(EntityTypeBuilder<Shop> b)
    {
        b.ToTable("shops", "shop");
        b.HasKey(s => s.Id);
        b.Property(s => s.Name).HasMaxLength(50).IsRequired();
        b.Property(s => s.Slug).HasMaxLength(80).IsRequired();
        b.Property(s => s.LogoUrl).HasMaxLength(500);
        b.Property(s => s.CoverUrl).HasMaxLength(500);
        b.Property(s => s.Description).HasMaxLength(2000);
        b.Property(s => s.Type).HasConversion<string>().HasMaxLength(10);
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.RejectReason).HasMaxLength(500);
        b.Property(s => s.LockReason).HasMaxLength(500);
        b.Property(s => s.Version).IsRowVersion();
        b.Ignore(s => s.IsMall);
        b.HasOne<User>().WithMany().HasForeignKey(s => s.OwnerId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(s => s.Slug).IsUnique().HasFilter("deleted_at IS NULL").HasDatabaseName("ux_shops_slug");
        b.HasIndex(s => new { s.Status, s.CreatedAt }).HasDatabaseName("ix_shops_status");
    }
}

internal sealed class ShopKycConfiguration : IEntityTypeConfiguration<ShopKyc>
{
    public void Configure(EntityTypeBuilder<ShopKyc> b)
    {
        b.ToTable("shop_kyc", "shop");
        b.HasKey(k => k.Id);
        b.Property(k => k.LegalName).HasMaxLength(200).IsRequired();
        b.Property(k => k.TaxCode).HasMaxLength(20);
        b.Property(k => k.IdCardNumberEncrypted).HasMaxLength(200);
        b.Property(k => k.IdCardFrontKey).HasMaxLength(300);
        b.Property(k => k.IdCardBackKey).HasMaxLength(300);
        b.Property(k => k.BusinessLicenseKey).HasMaxLength(300);
        b.Property(k => k.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(k => k.RejectReason).HasMaxLength(500);
        b.HasOne<Shop>().WithMany().HasForeignKey(k => k.ShopId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(k => k.ShopId).IsUnique().HasDatabaseName("ux_shop_kyc_shop");
    }
}

internal sealed class ShopWarehouseConfiguration : IEntityTypeConfiguration<ShopWarehouse>
{
    public void Configure(EntityTypeBuilder<ShopWarehouse> b)
    {
        b.ToTable("shop_warehouses", "shop");
        b.HasKey(w => w.Id);
        b.Property(w => w.Name).HasMaxLength(100).IsRequired();
        b.Property(w => w.ContactName).HasMaxLength(100).IsRequired();
        b.Property(w => w.Phone).HasMaxLength(15).IsRequired();
        b.Property(w => w.ProvinceCode).HasMaxLength(10).IsRequired();
        b.Property(w => w.DistrictCode).HasMaxLength(10).IsRequired();
        b.Property(w => w.WardCode).HasMaxLength(10).IsRequired();
        b.Property(w => w.Street).HasMaxLength(255).IsRequired();
        b.HasOne<Shop>().WithMany().HasForeignKey(w => w.ShopId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(w => w.ShopId).HasDatabaseName("ix_shop_warehouses_shop");
    }
}

internal sealed class ShopStaffConfiguration : IEntityTypeConfiguration<ShopStaff>
{
    public void Configure(EntityTypeBuilder<ShopStaff> b)
    {
        b.ToTable("shop_staff", "shop");
        b.HasKey(s => s.Id);
        b.Property(s => s.Role).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.Permissions).HasColumnType("text[]");
        b.HasOne<Shop>().WithMany().HasForeignKey(s => s.ShopId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(s => new { s.ShopId, s.UserId }).IsUnique().HasFilter("deleted_at IS NULL").HasDatabaseName("ux_shop_staff_shop_user");
        b.HasIndex(s => s.UserId).HasDatabaseName("ix_shop_staff_user");
    }
}

internal sealed class ShopBankAccountConfiguration : IEntityTypeConfiguration<ShopBankAccount>
{
    public void Configure(EntityTypeBuilder<ShopBankAccount> b)
    {
        b.ToTable("shop_bank_accounts", "shop");
        b.HasKey(a => a.Id);
        b.Property(a => a.BankCode).HasMaxLength(20).IsRequired();
        b.Property(a => a.AccountNoEncrypted).HasMaxLength(200).IsRequired();
        b.Property(a => a.AccountNoLast4).HasMaxLength(4).IsRequired();
        b.Property(a => a.AccountName).HasMaxLength(100).IsRequired();
        b.HasOne<Shop>().WithMany().HasForeignKey(a => a.ShopId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(a => a.ShopId).IsUnique().HasFilter("is_default AND deleted_at IS NULL").HasDatabaseName("ux_shop_bank_default");
    }
}
