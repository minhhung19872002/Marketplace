namespace ShopHub.Domain.Common;

// Base for every persisted row: uuid primary key (DB default gen_random_uuid())
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
}

// Entities that carry who/when columns, filled by the audit interceptor
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
    Guid? UpdatedBy { get; set; }
}

// Soft delete: rows are never physically removed, DeletedAt is set instead
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
}

public abstract class AuditableEntity : Entity, IAuditable, ISoftDeletable
{
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
