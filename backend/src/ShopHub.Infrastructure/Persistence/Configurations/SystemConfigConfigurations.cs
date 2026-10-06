using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopHub.Domain.Iam;
using ShopHub.Domain.SystemConfig;

namespace ShopHub.Infrastructure.Persistence.Configurations;

internal sealed class SystemParameterConfiguration : IEntityTypeConfiguration<SystemParameter>
{
    public void Configure(EntityTypeBuilder<SystemParameter> b)
    {
        b.ToTable("system_parameters", "sys");
        b.HasKey(p => p.Id);
        b.Property(p => p.Key).HasMaxLength(100).IsRequired();
        b.Property(p => p.Value).HasMaxLength(4000).IsRequired();
        b.Property(p => p.DataType).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.Group).HasMaxLength(50).IsRequired();
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.Property(p => p.Description).HasMaxLength(1000);
        b.Property(p => p.Version).IsRowVersion();
        b.HasIndex(p => p.Key).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages", "sys");
        b.HasKey(m => m.Id);
        b.Property(m => m.Type).HasMaxLength(100).IsRequired();
        b.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        b.Property(m => m.LastError).HasMaxLength(2000);
        // Pending messages are always read in arrival order
        b.HasIndex(m => new { m.OccurredAt, m.Id }).HasFilter("processed_at IS NULL").HasDatabaseName("ix_outbox_pending");
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs", "iam");
        b.HasKey(a => a.Id);
        b.Property(a => a.Ip).HasMaxLength(64);
        b.Property(a => a.UserAgent).HasMaxLength(500);
        b.Property(a => a.Action).HasMaxLength(20).IsRequired();
        b.Property(a => a.Entity).HasMaxLength(100).IsRequired();
        b.Property(a => a.EntityId).HasMaxLength(100);
        b.Property(a => a.OldValue).HasColumnType("jsonb");
        b.Property(a => a.NewValue).HasColumnType("jsonb");
        b.HasIndex(a => new { a.OccurredAt, a.Id }).HasDatabaseName("ix_audit_logs_occurred");
        b.HasIndex(a => new { a.Entity, a.EntityId }).HasDatabaseName("ix_audit_logs_entity");
        b.HasIndex(a => a.UserId).HasDatabaseName("ix_audit_logs_user");
    }
}
