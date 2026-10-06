using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Common;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Media;
using ShopHub.Domain.Shops;
using ShopHub.Domain.SystemConfig;

namespace ShopHub.Infrastructure.Persistence;

public class ShopHubDbContext(DbContextOptions<ShopHubDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<SystemParameter> SystemParameters => Set<SystemParameter>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<AdminDivision> AdminDivisions => Set<AdminDivision>();
    public DbSet<SimulatedSms> SimulatedSms => Set<SimulatedSms>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategoryAttribute> CategoryAttributes => Set<CategoryAttribute>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Sku> Skus => Set<Sku>();
    public DbSet<VariantTier> VariantTiers => Set<VariantTier>();
    public DbSet<VariantOption> VariantOptions => Set<VariantOption>();
    public DbSet<ProductMedia> ProductMedia => Set<ProductMedia>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<ShopKyc> ShopKycs => Set<ShopKyc>();
    public DbSet<ShopWarehouse> ShopWarehouses => Set<ShopWarehouse>();
    public DbSet<ShopStaff> ShopStaff => Set<ShopStaff>();
    public DbSet<ShopBankAccount> ShopBankAccounts => Set<ShopBankAccount>();
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    public DbSet<ShopFollower> ShopFollowers => Set<ShopFollower>();
    public DbSet<ProductView> ProductViews => Set<ProductView>();
    public DbSet<SearchLog> SearchLogs => Set<SearchLog>();

    // Unique index name → what the user is told when a parallel request already took the value
    private static readonly Dictionary<string, string> UniqueMessages = new()
    {
        ["ux_users_phone"] = "Số điện thoại đã được đăng ký.",
        ["ux_users_email"] = "Email đã được đăng ký.",
        ["ux_users_username"] = "Tên đăng nhập đã được sử dụng.",
        ["ux_roles_code"] = "Mã vai trò đã tồn tại.",
        ["ux_addresses_default"] = "Chỉ có một địa chỉ mặc định. Vui lòng thử lại.",
        ["ux_shops_slug"] = "Tên shop đã được sử dụng.",
        ["ux_shops_name"] = "Tên shop đã được sử dụng.",
        ["ux_shop_staff_shop_user"] = "Người này đã là nhân viên của shop.",
        ["ux_brands_slug"] = "Thương hiệu đã tồn tại.",
        ["ux_categories_slug"] = "Đã có danh mục trùng tên.",
        ["ux_wishlists_user_product"] = "Sản phẩm đã có trong danh sách yêu thích.",
        ["ux_shop_followers_shop_user"] = "Bạn đã theo dõi shop này.",
        ["ux_variant_options_tier_value"] = "Phân loại có lựa chọn bị trùng.",
    };

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation } ck)
        {
            throw new ConflictException(ck.ConstraintName switch
            {
                "ck_skus_stock" or "ck_skus_reserved" => "Tồn kho không đủ hoặc thấp hơn số đang giữ cho đơn.",
                _ => "Dữ liệu vi phạm ràng buộc, vui lòng kiểm tra lại.",
            }, "CHECK_VIOLATION");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw new ConflictException(
                pg.ConstraintName is { } name && UniqueMessages.TryGetValue(name, out var message) ? message : "Dữ liệu bị trùng, vui lòng kiểm tra lại.",
                "UNIQUE_VIOLATION");
        }
    }

    public async Task<T> InLockedTransactionAsync<T>(string lockKey, Func<Task<T>> work, CancellationToken ct)
    {
        await using var tx = await Database.BeginTransactionAsync(ct);
        await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        var result = await work();
        await tx.CommitAsync(ct);
        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("unaccent");
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.HasPostgresExtension("pgcrypto");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShopHubDbContext).Assembly);

        modelBuilder.HasDbFunction(typeof(Search.SearchFunctions).GetMethod(nameof(Search.SearchFunctions.Unaccent))!)
            .HasName("immutable_unaccent").HasSchema("public");

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            // Ids are generated client-side (Entity ctor) so EF must treat a new child found in a collection as Added,
            // never as an existing row to UPDATE; the DB default only serves inserts made outside EF (SQL scripts)
            if (typeof(Entity).IsAssignableFrom(clrType))
                modelBuilder.Entity(clrType).Property(nameof(Entity.Id)).HasDefaultValueSql("gen_random_uuid()").ValueGeneratedNever();

            // Soft-deleted rows disappear from every query unless IgnoreQueryFilters() is used
            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                var param = Expression.Parameter(clrType, "e");
                var body = Expression.Equal(
                    Expression.Property(param, nameof(ISoftDeletable.DeletedAt)),
                    Expression.Constant(null, typeof(DateTimeOffset?)));
                modelBuilder.Entity(clrType).HasQueryFilter(Expression.Lambda(body, param));
            }
        }
    }
}
