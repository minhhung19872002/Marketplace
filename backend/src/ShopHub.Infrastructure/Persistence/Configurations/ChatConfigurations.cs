using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Shops;

namespace ShopHub.Infrastructure.Persistence.Configurations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> b)
    {
        b.ToTable("conversations", "engage", t =>
        {
            t.HasCheckConstraint("ck_conversations_unread", "buyer_unread >= 0 AND shop_unread >= 0");
        });
        b.HasKey(c => c.Id);
        b.Property(c => c.LastMessagePreview).HasMaxLength(120);
        b.Property(c => c.Version).IsRowVersion();
        b.HasOne<User>().WithMany().HasForeignKey(c => c.BuyerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Shop>().WithMany().HasForeignKey(c => c.ShopId).OnDelete(DeleteBehavior.Restrict);
        // One conversation per buyer and shop, even when both press "Chat ngay" at the same moment
        b.HasIndex(c => new { c.BuyerId, c.ShopId }).IsUnique().HasDatabaseName("ux_conversations_pair");
        b.HasIndex(c => new { c.ShopId, c.LastMessageAt }).IsDescending(false, true).HasDatabaseName("ix_conversations_shop");
        b.HasIndex(c => new { c.BuyerId, c.LastMessageAt }).IsDescending(false, true).HasDatabaseName("ix_conversations_buyer");
    }
}

internal sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> b)
    {
        b.ToTable("messages", "engage");
        b.HasKey(m => m.Id);
        b.Property(m => m.SenderRole).HasConversion<string>().HasMaxLength(10);
        b.Property(m => m.Type).HasConversion<string>().HasMaxLength(10);
        b.Property(m => m.Body).HasMaxLength(ChatMessage.MaxText).IsRequired();
        b.Property(m => m.Payload).HasColumnType("jsonb");
        b.HasOne<Conversation>().WithMany().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(m => new { m.ConversationId, m.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_messages_conversation");
    }
}

internal sealed class QuickReplyConfiguration : IEntityTypeConfiguration<QuickReply>
{
    public void Configure(EntityTypeBuilder<QuickReply> b)
    {
        b.ToTable("quick_replies", "engage");
        b.HasKey(q => q.Id);
        b.Property(q => q.Shortcut).HasMaxLength(30).IsRequired();
        b.Property(q => q.Content).HasMaxLength(1_000).IsRequired();
        b.HasOne<Shop>().WithMany().HasForeignKey(q => q.ShopId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(q => new { q.ShopId, q.Shortcut }).IsUnique().HasDatabaseName("ux_quick_replies_shortcut");
    }
}

internal sealed class ShopChatSettingsConfiguration : IEntityTypeConfiguration<ShopChatSettings>
{
    public void Configure(EntityTypeBuilder<ShopChatSettings> b)
    {
        b.ToTable("shop_chat_settings", "engage");
        b.HasKey(s => s.Id);
        b.Property(s => s.AutoReplyText).HasMaxLength(500).IsRequired();
        b.HasOne<Shop>().WithMany().HasForeignKey(s => s.ShopId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(s => s.ShopId).IsUnique().HasDatabaseName("ux_shop_chat_settings_shop");
    }
}

internal sealed class ChatReportConfiguration : IEntityTypeConfiguration<ChatReport>
{
    public void Configure(EntityTypeBuilder<ChatReport> b)
    {
        b.ToTable("chat_reports", "engage");
        b.HasKey(r => r.Id);
        b.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Resolution).HasMaxLength(500);
        b.HasOne<Conversation>().WithMany().HasForeignKey(r => r.ConversationId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => new { r.Status, r.CreatedAt }).HasDatabaseName("ix_chat_reports_status");
    }
}

internal sealed class NotificationPrefConfiguration : IEntityTypeConfiguration<NotificationPref>
{
    public void Configure(EntityTypeBuilder<NotificationPref> b)
    {
        b.ToTable("notification_prefs", "engage");
        b.HasKey(p => p.Id);
        b.Property(p => p.Category).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.Channel).HasConversion<string>().HasMaxLength(10);
        b.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(p => new { p.UserId, p.Category, p.Channel }).IsUnique().HasDatabaseName("ux_notification_prefs");
    }
}

internal sealed class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> b)
    {
        b.ToTable("device_tokens", "engage");
        b.HasKey(d => d.Id);
        b.Property(d => d.Platform).HasMaxLength(20).IsRequired();
        b.Property(d => d.Token).HasMaxLength(500).IsRequired();
        b.HasOne<User>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(d => d.Token).IsUnique().HasDatabaseName("ux_device_tokens_token");
    }
}

internal sealed class BroadcastConfiguration : IEntityTypeConfiguration<Broadcast>
{
    public void Configure(EntityTypeBuilder<Broadcast> b)
    {
        b.ToTable("broadcasts", "engage");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(120).IsRequired();
        b.Property(x => x.Body).HasMaxLength(500).IsRequired();
        b.Property(x => x.Link).HasMaxLength(500);
        b.Property(x => x.Segment).HasConversion<string>().HasMaxLength(20);
    }
}
