using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Common;
using ShopHub.Domain.Iam;
using ShopHub.Domain.SystemConfig;

namespace ShopHub.Infrastructure.Persistence;

public class ShopHubDbContext(DbContextOptions<ShopHubDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<SystemParameter> SystemParameters => Set<SystemParameter>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

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
