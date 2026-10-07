using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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

internal sealed class CmsPageConfiguration : IEntityTypeConfiguration<CmsPage>
{
    public void Configure(EntityTypeBuilder<CmsPage> b)
    {
        b.ToTable("cms_pages", "sys");
        b.HasKey(p => p.Id);
        b.Property(p => p.Kind).HasConversion<string>().HasMaxLength(10);
        b.Property(p => p.Slug).HasMaxLength(120).IsRequired();
        b.Property(p => p.Title).HasMaxLength(200).IsRequired();
        b.Property(p => p.Topic).HasMaxLength(100);
        b.Property(p => p.Content).IsRequired();
        b.HasIndex(p => p.Slug).IsUnique().HasDatabaseName("ux_cms_pages_slug");
    }
}

internal sealed class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> b)
    {
        b.ToTable("message_templates", "sys");
        b.HasKey(t => t.Id);
        b.Property(t => t.Key).HasMaxLength(60).IsRequired();
        b.Property(t => t.Channel).HasConversion<string>().HasMaxLength(10);
        b.Property(t => t.Name).HasMaxLength(200).IsRequired();
        b.Property(t => t.Subject).HasMaxLength(200);
        b.Property(t => t.Body).HasMaxLength(4000).IsRequired();
        b.Property(t => t.Placeholders).HasMaxLength(300);
        b.HasIndex(t => new { t.Key, t.Channel }).IsUnique().HasDatabaseName("ux_message_templates_key");
    }
}

internal sealed class BackgroundTaskConfiguration : IEntityTypeConfiguration<BackgroundTask>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<BackgroundTask> b)
    {
        b.ToTable("background_tasks", "sys");
        b.HasKey(t => t.Id);
        b.Property(t => t.Kind).HasConversion<string>().HasMaxLength(30);
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.FileName).HasMaxLength(255).IsRequired();
        b.Property(t => t.Message).HasMaxLength(1000);
        b.Property(t => t.Input).HasColumnType("bytea");
        b.Property(t => t.Output).HasColumnType("bytea");
        b.Property(t => t.OutputName).HasMaxLength(255);
        b.Property(t => t.OutputType).HasMaxLength(100);
        b.HasIndex(t => new { t.OwnerUserId, t.CreatedAt }).HasDatabaseName("ix_background_tasks_owner");
        b.Property(t => t.Errors).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, Json),
            v => JsonSerializer.Deserialize<List<TaskRowError>>(v, Json) ?? new List<TaskRowError>(),
            new ValueComparer<List<TaskRowError>>((a, c) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(c, Json),
                v => JsonSerializer.Serialize(v, Json).GetHashCode(), v => v.ToList()));
        b.HasIndex(t => new { t.ShopId, t.CreatedAt }).HasDatabaseName("ix_background_tasks_shop");
        b.HasIndex(t => new { t.Status, t.CreatedAt }).HasDatabaseName("ix_background_tasks_status");
    }
}
