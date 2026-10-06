using Microsoft.EntityFrameworkCore;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Media;
using ShopHub.Domain.Shops;
using ShopHub.Domain.SystemConfig;

namespace ShopHub.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<SystemParameter> SystemParameters { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<AuditLog> AuditLogs { get; }

    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<OtpCode> OtpCodes { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<Address> Addresses { get; }
    DbSet<AdminDivision> AdminDivisions { get; }

    DbSet<MediaAsset> MediaAssets { get; }
    DbSet<Category> Categories { get; }
    DbSet<CategoryAttribute> CategoryAttributes { get; }
    DbSet<Brand> Brands { get; }
    DbSet<Product> Products { get; }
    DbSet<Sku> Skus { get; }
    DbSet<VariantTier> VariantTiers { get; }
    DbSet<VariantOption> VariantOptions { get; }
    DbSet<ProductMedia> ProductMedia { get; }
    DbSet<InventoryMovement> InventoryMovements { get; }
    DbSet<Shop> Shops { get; }
    DbSet<ShopKyc> ShopKycs { get; }
    DbSet<ShopWarehouse> ShopWarehouses { get; }
    DbSet<ShopStaff> ShopStaff { get; }
    DbSet<ShopBankAccount> ShopBankAccounts { get; }

    DbSet<Wishlist> Wishlists { get; }
    DbSet<ShopFollower> ShopFollowers { get; }
    DbSet<ProductView> ProductViews { get; }
    DbSet<SearchLog> SearchLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> in a transaction holding a PostgreSQL advisory lock on <paramref name="lockKey"/>,
    /// so concurrent requests for the same key run one after another (check-then-insert without races).
    /// </summary>
    Task<T> InLockedTransactionAsync<T>(string lockKey, Func<Task<T>> work, CancellationToken ct);
}
