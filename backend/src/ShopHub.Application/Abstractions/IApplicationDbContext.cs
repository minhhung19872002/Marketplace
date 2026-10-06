using Microsoft.EntityFrameworkCore;
using ShopHub.Domain.Iam;
using ShopHub.Domain.SystemConfig;

namespace ShopHub.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<SystemParameter> SystemParameters { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
