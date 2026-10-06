using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Domain.Common;
using ShopHub.Domain.Iam;
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

    // Unique index name → what the user is told when a parallel request already took the value
    private static readonly Dictionary<string, string> UniqueMessages = new()
    {
        ["ux_users_phone"] = "Số điện thoại đã được đăng ký.",
        ["ux_users_email"] = "Email đã được đăng ký.",
        ["ux_users_username"] = "Tên đăng nhập đã được sử dụng.",
        ["ux_roles_code"] = "Mã vai trò đã tồn tại.",
        ["ux_addresses_default"] = "Chỉ có một địa chỉ mặc định. Vui lòng thử lại.",
    };

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw new ConflictException(
                pg.ConstraintName is { } name && UniqueMessages.TryGetValue(name, out var message) ? message : "Dữ liệu bị trùng, vui lòng kiểm tra lại.",
                "UNIQUE_VIOLATION");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("unaccent");
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.HasPostgresExtension("pgcrypto");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShopHubDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            // uuid keys default to gen_random_uuid() when inserted outside EF (SQL scripts, seeds)
            if (typeof(Entity).IsAssignableFrom(clrType))
                modelBuilder.Entity(clrType).Property(nameof(Entity.Id)).HasDefaultValueSql("gen_random_uuid()");

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
