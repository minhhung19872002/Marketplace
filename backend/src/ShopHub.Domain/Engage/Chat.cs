using ShopHub.Domain.Common;

namespace ShopHub.Domain.Engage;

public enum ChatRole
{
    Buyer,
    Shop,
    System,  // automatic replies (outside working hours)
}

public enum MessageType
{
    Text,     // CHỮ
    Image,    // ẢNH
    Product,  // SẢN PHẨM (card)
    Order,    // ĐƠN HÀNG (card)
    Voucher,  // VOUCHER (card)
}

/// <summary>
/// One buyer ↔ one shop (unique pair, spec 4.8). Unread counters are cached per side and recomputed from the
/// messages when read; several staff of the shop may answer, the conversation can be assigned to one of them.
/// </summary>
public class Conversation : Entity
{
    private Conversation() { }

    public Conversation(Guid buyerId, Guid shopId, DateTimeOffset now)
    {
        BuyerId = buyerId;
        ShopId = shopId;
        CreatedAt = now;
        LastMessageAt = now;
    }

    public Guid BuyerId { get; private set; }
    public Guid ShopId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastMessageAt { get; private set; }
    public string? LastMessagePreview { get; private set; }
    public int BuyerUnread { get; private set; }
    public int ShopUnread { get; private set; }
    // Staff member the conversation is assigned to (null = any staff with CHAT.MANAGE)
    public Guid? AssignedTo { get; private set; }
    // The buyer blocked this shop: the shop can no longer write to them
    public bool BlockedByBuyer { get; private set; }
    // First buyer message still waiting for a shop answer (response rate / time of the shop)
    public DateTimeOffset? AwaitingReplySince { get; private set; }
    public uint Version { get; private set; }

    public void Posted(ChatRole by, string preview, DateTimeOffset at)
    {
        LastMessageAt = at;
        LastMessagePreview = preview.Length > 120 ? preview[..120] : preview;
        switch (by)
        {
            case ChatRole.Buyer:
                ShopUnread++;
                AwaitingReplySince ??= at;
                break;
            case ChatRole.Shop:
                BuyerUnread++;
                AwaitingReplySince = null;
                break;
            case ChatRole.System:
                BuyerUnread++;
                break;
        }
    }

    public void ReadBy(ChatRole side)
    {
        if (side == ChatRole.Buyer) BuyerUnread = 0;
        else ShopUnread = 0;
    }

    public void AssignTo(Guid? staffUserId) => AssignedTo = staffUserId;

    public void SetBlocked(bool blocked) => BlockedByBuyer = blocked;
}

public class ChatMessage : Entity
{
    public const int MaxText = 2_000;

    private ChatMessage() { }

    public ChatMessage(Guid conversationId, Guid? senderId, ChatRole senderRole, MessageType type, string body, string? payload, bool flagged,
        DateTimeOffset now)
    {
        if (type == MessageType.Text && string.IsNullOrWhiteSpace(body)) throw new BusinessRuleException("Tin nhắn không được để trống.");
        if (body.Length > MaxText) throw new BusinessRuleException($"Tin nhắn tối đa {MaxText} ký tự.");
        ConversationId = conversationId;
        SenderId = senderId;
        SenderRole = senderRole;
        Type = type;
        Body = body.Trim();
        Payload = payload;
        Flagged = flagged;
        CreatedAt = now;
    }

    public Guid ConversationId { get; private set; }
    public Guid? SenderId { get; private set; }
    public ChatRole SenderRole { get; private set; }
    public MessageType Type { get; private set; }
    public string Body { get; private set; } = string.Empty;
    // jsonb: product / order / voucher card, image URL
    public string? Payload { get; private set; }
    // Contains contact details outside the platform (phone, link): shown with a warning, not blocked
    public bool Flagged { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}

/// <summary>Tin trả lời nhanh of a shop: "/giaohang" → the saved text.</summary>
public class QuickReply : Entity
{
    private QuickReply() { }

    public QuickReply(Guid shopId, string shortcut, string content)
    {
        ShopId = shopId;
        Update(shortcut, content);
    }

    public Guid ShopId { get; private set; }
    public string Shortcut { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;

    public void Update(string shortcut, string content)
    {
        var s = shortcut.Trim().TrimStart('/').ToLowerInvariant();
        if (s.Length is < 1 or > 30 || !s.All(c => char.IsLetterOrDigit(c) || c == '-'))
            throw new BusinessRuleException("Phím tắt gồm 1–30 chữ/số không dấu cách.");
        if (string.IsNullOrWhiteSpace(content) || content.Length > 1_000) throw new BusinessRuleException("Nội dung từ 1 đến 1.000 ký tự.");
        Shortcut = s;
        Content = content.Trim();
    }
}

/// <summary>Working hours and the automatic reply sent outside them (spec III.6).</summary>
public class ShopChatSettings : Entity
{
    private ShopChatSettings() { }

    public ShopChatSettings(Guid shopId)
    {
        ShopId = shopId;
        OpenFrom = new TimeOnly(8, 0);
        OpenTo = new TimeOnly(22, 0);
        AutoReplyText = "Cảm ơn bạn đã nhắn tin! Shop đang ngoài giờ làm việc và sẽ trả lời bạn sớm nhất có thể.";
    }

    public Guid ShopId { get; private set; }
    public bool AutoReplyEnabled { get; private set; }
    public string AutoReplyText { get; private set; } = string.Empty;
    // Vietnam wall-clock hours
    public TimeOnly OpenFrom { get; private set; }
    public TimeOnly OpenTo { get; private set; }

    public void Update(bool enabled, string text, TimeOnly from, TimeOnly to)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 500) throw new BusinessRuleException("Tin tự động từ 1 đến 500 ký tự.");
        if (from == to) throw new BusinessRuleException("Giờ mở và giờ đóng phải khác nhau.");
        AutoReplyEnabled = enabled;
        AutoReplyText = text.Trim();
        OpenFrom = from;
        OpenTo = to;
    }

    /// <summary>Within working hours (an overnight window such as 20:00–02:00 is allowed).</summary>
    public bool IsOpen(TimeOnly at) => OpenFrom < OpenTo ? at >= OpenFrom && at < OpenTo : at >= OpenFrom || at < OpenTo;
}

/// <summary>A buyer reported a shop from the chat (handled by the platform).</summary>
public class ChatReport : Entity
{
    private ChatReport() { }

    public ChatReport(Guid conversationId, Guid reporterId, string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Vui lòng nhập lý do báo cáo.");
        ConversationId = conversationId;
        ReporterId = reporterId;
        Reason = reason.Trim();
        CreatedAt = now;
    }

    public Guid ConversationId { get; private set; }
    public Guid ReporterId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public ChatReportStatus Status { get; private set; } = ChatReportStatus.Open;
    public string? Resolution { get; private set; }
    public Guid? ResolvedBy { get; private set; }
    // Set by the platform's decision (one conditional UPDATE, see ResolveChatReportHandler)
    public DateTimeOffset? ResolvedAt { get; private set; }
}

public enum ChatReportStatus
{
    Open,       // waiting for the platform
    Dismissed,  // no violation found
    Penalized,  // the shop got penalty points
}

public enum NotificationChannel
{
    InApp,  // TRONG APP (bell + realtime)
    Email,
    Sms,
    Push,   // FCM (prepared)
}

/// <summary>Kênh nhận cho một loại thông báo (spec 4.8). No row = the default for that channel.</summary>
public class NotificationPref : Entity
{
    private NotificationPref() { }

    public NotificationPref(Guid userId, NotificationCategory category, NotificationChannel channel, bool enabled)
    {
        UserId = userId;
        Category = category;
        Channel = channel;
        Enabled = enabled;
    }

    public Guid UserId { get; private set; }
    public NotificationCategory Category { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public bool Enabled { get; private set; }

    public void Set(bool enabled) => Enabled = enabled;

    /// <summary>In-app is always on; email on for orders and wallet; SMS and push off unless chosen.</summary>
    public static bool Default(NotificationCategory category, NotificationChannel channel) => channel switch
    {
        NotificationChannel.InApp => true,
        NotificationChannel.Email => category is NotificationCategory.Order or NotificationCategory.Wallet,
        _ => false,
    };
}

/// <summary>Mobile push token of a device (FCM), for the push channel.</summary>
public class DeviceToken : Entity
{
    private DeviceToken() { }

    public DeviceToken(Guid userId, string platform, string token, DateTimeOffset now)
    {
        UserId = userId;
        Platform = platform;
        Token = token;
        CreatedAt = now;
        LastSeenAt = now;
    }

    public Guid UserId { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public string Token { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    public void Seen(DateTimeOffset now) => LastSeenAt = now;
}

public enum BroadcastSegment
{
    Everyone,       // every buyer account
    MemberGold,     // members of the Gold tier or above (spend over MEMBER.WINDOW_DAYS)
    MemberDiamond,
    NoOrderYet,     // signed up but never ordered
}

/// <summary>
/// Thông báo hàng loạt of the platform (spec VI.6): one promotion notification per person per campaign (dedupe key)
/// and at most one promotion notification per person per day.
/// </summary>
public class Broadcast : Entity
{
    private Broadcast() { }

    public Broadcast(string title, string body, string? link, BroadcastSegment segment, Guid createdBy, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 120) throw new BusinessRuleException("Tiêu đề từ 1 đến 120 ký tự.");
        if (string.IsNullOrWhiteSpace(body) || body.Length > 500) throw new BusinessRuleException("Nội dung từ 1 đến 500 ký tự.");
        Title = title.Trim();
        Body = body.Trim();
        Link = string.IsNullOrWhiteSpace(link) ? null : link.Trim();
        Segment = segment;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public string? Link { get; private set; }
    public BroadcastSegment Segment { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int Recipients { get; private set; }
    public int SkippedToday { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    public void Sent(int recipients, int skipped, DateTimeOffset now)
    {
        Recipients = recipients;
        SkippedToday = skipped;
        SentAt = now;
    }
}
