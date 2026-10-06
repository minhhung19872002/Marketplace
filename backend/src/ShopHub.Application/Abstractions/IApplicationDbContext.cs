using Microsoft.EntityFrameworkCore;
using ShopHub.Domain.Iam;
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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
