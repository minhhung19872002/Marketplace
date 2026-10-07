using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Seller;
using ShopHub.Application.Features.Storefront;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Common;
using ShopHub.Domain.Engage;
using ShopHub.Application.Features.Media;
using ShopHub.Domain.Media;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Chat;

public record MessageDto(Guid Id, Guid ConversationId, ChatRole SenderRole, Guid? SenderId, string? SenderName, MessageType Type, string Body,
    JsonElement? Payload, bool Flagged, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public record ConversationDto(Guid Id, Guid ShopId, string ShopName, string? ShopLogoUrl, string ShopSlug, Guid BuyerId, string BuyerName,
    string? LastMessagePreview, DateTimeOffset LastMessageAt, int Unread, Guid? AssignedTo, string? AssignedName, bool BlockedByBuyer);

/// <summary>Contact details outside the platform (spec II.11): matched by CHAT.CONTACT_PATTERNS, flagged with a warning — never blocked.</summary>
public sealed class ContactFilter(ISystemParameters parameters)
{
    public async Task<bool> FlagsAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var patterns = (await parameters.GetStringAsync(ParameterKeys.ChatContactPatterns, ct)).Split("||", StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in patterns)
        {
            try
            {
                if (Regex.IsMatch(text, p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))) return true;
            }
            catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
            {
                // A broken admin pattern must not stop the chat
            }
        }
        return false;
    }
}

/// <summary>Who may read / write a conversation, and to whom its events go.</summary>
public sealed class ChatAccess(IApplicationDbContext db, ICurrentUser currentUser, SellerAccess seller)
{
    /// <summary>The buyer's own conversation (404 for anybody else's).</summary>
    public async Task<Conversation> AsBuyerAsync(Guid conversationId, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        return await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId && c.BuyerId == userId, ct)
               ?? throw new NotFoundException("Không tìm thấy cuộc trò chuyện.");
    }

    public async Task<(Conversation Conversation, ShopStaff Staff)> AsShopAsync(Guid shopId, Guid conversationId, CancellationToken ct)
    {
        var staff = await seller.RequireAsync(shopId, ShopPermissions.ChatManage, ct);
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId && c.ShopId == shopId, ct)
                           ?? throw new NotFoundException("Không tìm thấy cuộc trò chuyện.");
        return (conversation, staff);
    }

    /// <summary>The buyer plus every staff member of the shop who handles chat (owners always do).</summary>
    public async Task<List<Guid>> ParticipantsAsync(Conversation c, CancellationToken ct)
    {
        var staff = await db.ShopStaff.AsNoTracking().Where(s => s.ShopId == c.ShopId).ToListAsync(ct);
        return staff.Where(s => s.Has(ShopPermissions.ChatManage)).Select(s => s.UserId).Append(c.BuyerId).Distinct().ToList();
    }
}

internal static class ChatViews
{
    public static async Task<List<MessageDto>> MessagesAsync(IApplicationDbContext db, IObjectStorage storage, IReadOnlyList<ChatMessage> messages,
        CancellationToken ct)
    {
        var senders = messages.Where(m => m.SenderId is not null).Select(m => m.SenderId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => senders.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var result = new List<MessageDto>();
        foreach (var m in messages) result.Add(await ToDtoAsync(storage, m, m.SenderId is { } s ? names.GetValueOrDefault(s) : null, ct));
        return result;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Chat photos live in a private bucket: each read gets a short-lived signed URL.</summary>
    public static async Task<MessageDto> ToDtoAsync(IObjectStorage storage, ChatMessage m, string? senderName, CancellationToken ct)
    {
        JsonElement? payload = m.Payload is null ? null : JsonDocument.Parse(m.Payload).RootElement.Clone();
        if (m.Type == MessageType.Image && payload is { } p && p.TryGetProperty("key", out var key))
        {
            var url = await storage.SignedUrlAsync(Buckets.Chat, key.GetString()!, TimeSpan.FromHours(2), ct);
            payload = JsonSerializer.SerializeToElement(new { url }, Json);
        }
        return new MessageDto(m.Id, m.ConversationId, m.SenderRole, m.SenderId, senderName, m.Type, m.Body, payload, m.Flagged, m.CreatedAt, m.ReadAt);
    }

    public static async Task<List<ConversationDto>> ConversationsAsync(IApplicationDbContext db, IReadOnlyList<Conversation> list, ChatRole side,
        CancellationToken ct)
    {
        var shopIds = list.Select(c => c.ShopId).Distinct().ToList();
        var userIds = list.Select(c => c.BuyerId).Concat(list.Where(c => c.AssignedTo != null).Select(c => c.AssignedTo!.Value)).Distinct().ToList();
        var shops = await db.Shops.AsNoTracking().Where(s => shopIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return list.Select(c =>
        {
            var shop = shops.GetValueOrDefault(c.ShopId);
            return new ConversationDto(c.Id, c.ShopId, shop?.Name ?? "", shop?.LogoUrl, shop?.Slug ?? "", c.BuyerId, users.GetValueOrDefault(c.BuyerId) ?? "",
                c.LastMessagePreview, c.LastMessageAt, side == ChatRole.Buyer ? c.BuyerUnread : c.ShopUnread, c.AssignedTo,
                c.AssignedTo is { } a ? users.GetValueOrDefault(a) : null, c.BlockedByBuyer);
        }).ToList();
    }
}

// ---------- buyer side ----------

public record StartConversationCommand(Guid ShopId) : IRequest<ConversationDto>;

/// <summary>"Chat ngay": the buyer's conversation with the shop, created on first use (unique pair, parallel-safe).</summary>
public sealed class StartConversationHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<StartConversationCommand, ConversationDto>
{
    public async Task<ConversationDto> Handle(StartConversationCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var shop = await db.Shops.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.ShopId && (s.Status == ShopStatus.Active || s.Status == ShopStatus.Vacation), ct)
                   ?? throw new NotFoundException("Không tìm thấy shop.");
        if (shop.OwnerId == userId) throw new ConflictException("Bạn không thể chat với shop của chính mình.", "OWN_SHOP");
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.BuyerId == userId && c.ShopId == shop.Id, ct);
        if (conversation is null)
        {
            conversation = new Conversation(userId, shop.Id, clock.UtcNow);
            db.Conversations.Add(conversation);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (ConflictException e) when (e.Constraint == "ux_conversations_pair")
            {
                db.ClearTracking();
                conversation = await db.Conversations.SingleAsync(c => c.BuyerId == userId && c.ShopId == shop.Id, ct);
            }
        }
        return (await ChatViews.ConversationsAsync(db, [conversation], ChatRole.Buyer, ct))[0];
    }
}

public record MyConversationsQuery(string? Q = null, int Page = 1, int PageSize = 30) : IRequest<PagedResult<ConversationDto>>, IPagedRequest;

public sealed class MyConversationsHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<MyConversationsQuery, PagedResult<ConversationDto>>
{
    public async Task<PagedResult<ConversationDto>> Handle(MyConversationsQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var q = request.Q?.Trim().ToLower() ?? "";
        var query = db.Conversations.AsNoTracking().Where(c => c.BuyerId == userId
            && (q == "" || db.Shops.Any(s => s.Id == c.ShopId && s.Name.ToLower().Contains(q))))
            .OrderByDescending(c => c.LastMessageAt).ThenBy(c => c.Id);
        var rows = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new PagedResult<ConversationDto>(await ChatViews.ConversationsAsync(db, rows, ChatRole.Buyer, ct), await query.CountAsync(ct),
            request.Page, request.PageSize);
    }
}

public record ChatUnreadQuery : IRequest<int>;

public sealed class ChatUnreadHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<ChatUnreadQuery, int>
{
    public async Task<int> Handle(ChatUnreadQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        return await db.Conversations.Where(c => c.BuyerId == userId).SumAsync(c => (int?)c.BuyerUnread, ct) ?? 0;
    }
}

public record ConversationMessagesQuery(Guid ConversationId, Guid? ShopId, DateTimeOffset? Before, int Limit = 30) : IRequest<IReadOnlyList<MessageDto>>;

/// <summary>Messages newest first (paging backwards with <c>before</c>), for the buyer or the shop's chat staff.</summary>
public sealed class ConversationMessagesHandler(IApplicationDbContext db, ChatAccess access, IObjectStorage storage)
    : IRequestHandler<ConversationMessagesQuery, IReadOnlyList<MessageDto>>
{
    public async Task<IReadOnlyList<MessageDto>> Handle(ConversationMessagesQuery request, CancellationToken ct)
    {
        var conversation = request.ShopId is { } shopId
            ? (await access.AsShopAsync(shopId, request.ConversationId, ct)).Conversation
            : await access.AsBuyerAsync(request.ConversationId, ct);
        var before = request.Before?.ToUniversalTime();
        var rows = await db.ChatMessages.AsNoTracking().Where(m => m.ConversationId == conversation.Id && (before == null || m.CreatedAt < before))
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(Math.Clamp(request.Limit, 1, 100)).ToListAsync(ct);
        return await ChatViews.MessagesAsync(db, storage, rows, ct);
    }
}

/// <param name="ShopId">Set when a shop staff member writes; null for the buyer.</param>
public record SendMessageCommand(Guid ConversationId, Guid? ShopId, MessageType Type, string? Text, Guid? ProductId, string? OrderCode, Guid? VoucherId,
    Guid? ImageAssetId) : IRequest<MessageDto>;

public sealed class SendMessageValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageValidator()
    {
        RuleFor(x => x.Text).NotEmpty().WithMessage("Tin nhắn không được để trống.").MaximumLength(ChatMessage.MaxText)
            .WithMessage($"Tin nhắn tối đa {ChatMessage.MaxText} ký tự.").When(x => x.Type == MessageType.Text);
        RuleFor(x => x.ProductId).NotNull().WithMessage("Thiếu sản phẩm.").When(x => x.Type == MessageType.Product);
        RuleFor(x => x.OrderCode).NotEmpty().WithMessage("Thiếu mã đơn.").When(x => x.Type == MessageType.Order);
        RuleFor(x => x.VoucherId).NotNull().WithMessage("Thiếu voucher.").When(x => x.Type == MessageType.Voucher);
        RuleFor(x => x.ImageAssetId).NotNull().WithMessage("Thiếu ảnh.").When(x => x.Type == MessageType.Image);
    }
}

/// <summary>
/// Gửi tin (spec II.11, III.6): text, image, product / order / voucher card. Contact details outside the platform are
/// flagged, not blocked. A buyer message outside the shop's working hours gets the automatic reply (at most once an
/// hour). The message reaches every open tab of the participants at once (SignalR) and a notification (at most one an
/// hour per conversation) is left for the recipient.
/// </summary>
public sealed class SendMessageHandler(
    IApplicationDbContext db,
    ChatAccess access,
    ContactFilter contacts,
    IObjectStorage storage,
    IRealtime realtime,
    ICurrentUser currentUser,
    Admin.MessageTemplates templates,
    IClock clock) : IRequestHandler<SendMessageCommand, MessageDto>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<MessageDto> Handle(SendMessageCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        Conversation conversation;
        ChatRole role;
        if (request.ShopId is { } shopId)
        {
            (conversation, _) = await access.AsShopAsync(shopId, request.ConversationId, ct);
            if (conversation.BlockedByBuyer) throw new ConflictException("Người mua đã chặn shop, không gửi được tin.", "CHAT_BLOCKED");
            role = ChatRole.Shop;
        }
        else
        {
            conversation = await access.AsBuyerAsync(request.ConversationId, ct);
            role = ChatRole.Buyer;
        }

        var (body, payload) = await ContentAsync(request, conversation, ct);
        var now = clock.UtcNow;
        var flagged = request.Type == MessageType.Text && await contacts.FlagsAsync(body, ct);
        var message = new ChatMessage(conversation.Id, userId, role, request.Type, body, payload, flagged, now);
        // One conversation's sends / reads run one after another, so each recount sees every message before it
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"chat:{conversation.Id}", ct);
        db.ChatMessages.Add(message);
        var posts = new List<(ChatRole Role, string Preview, DateTimeOffset At)> { (role, Preview(message), now) };

        ChatMessage? autoReply = null;
        if (role == ChatRole.Buyer)
        {
            var settings = await db.ShopChatSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ShopId == conversation.ShopId, ct);
            var recentAuto = await db.ChatMessages.AnyAsync(m => m.ConversationId == conversation.Id && m.SenderRole == ChatRole.System
                                                                 && m.CreatedAt > now.AddHours(-1), ct);
            if (settings is { AutoReplyEnabled: true } && !settings.IsOpen(TimeOnly.FromDateTime(VietnamTime.ToLocal(now).DateTime)) && !recentAuto)
            {
                autoReply = new ChatMessage(conversation.Id, null, ChatRole.System, MessageType.Text, settings.AutoReplyText, null, false, now.AddMilliseconds(1));
                db.ChatMessages.Add(autoReply);
                posts.Add((ChatRole.System, autoReply.Body, autoReply.CreatedAt));
            }
        }

        var participants = await access.ParticipantsAsync(conversation, ct);
        var recipients = role == ChatRole.Buyer ? participants.Where(p => p != conversation.BuyerId).ToList() : [conversation.BuyerId];
        var hour = VietnamTime.ToLocal(now).ToString("yyyyMMddHH");
        var shopName = await db.Shops.Where(s => s.Id == conversation.ShopId).Select(s => s.Name).SingleAsync(ct);
        var sender = await db.Users.Where(u => u.Id == userId).Select(u => u.FullName).SingleAsync(ct);
        foreach (var r in recipients)
        {
            var dedupe = $"chat:{conversation.Id}:{hour}";
            if (await db.Notifications.AnyAsync(n => n.UserId == r && n.DedupeKey == dedupe, ct)) continue;
            var (title, text) = await templates.NoticeAsync(role == ChatRole.Buyer ? Admin.TemplateCatalog.ChatToShop : Admin.TemplateCatalog.ChatToBuyer,
                new Dictionary<string, string> { ["sender"] = sender, ["shop"] = shopName, ["message"] = Preview(message) }, ct);
            db.Notifications.Add(new Notification(r, NotificationCategory.Activity, title, text,
                role == ChatRole.Buyer ? $"/seller/chat?c={conversation.Id}" : $"/chat?c={conversation.Id}", "conversation", conversation.Id, now, dedupe));
        }
        await db.SaveChangesAsync(ct);
        foreach (var post in posts) await ConversationState.AfterPostAsync(db, conversation.Id, post.Role, post.Preview, post.At, ct);
        await tx.CommitAsync(ct);

        var dto = await ChatViews.ToDtoAsync(storage, message, sender, ct);
        await realtime.ToUsersAsync(participants, RealtimeEvents.ChatMessage, dto, ct);
        if (autoReply is not null)
            await realtime.ToUsersAsync(participants, RealtimeEvents.ChatMessage, await ChatViews.ToDtoAsync(storage, autoReply, null, ct), ct);
        return dto;
    }

    private static string Preview(ChatMessage m) => m.Type switch
    {
        MessageType.Image => "[Hình ảnh]",
        MessageType.Product => $"[Sản phẩm] {m.Body}",
        MessageType.Order => $"[Đơn hàng] {m.Body}",
        MessageType.Voucher => $"[Voucher] {m.Body}",
        _ => m.Body,
    };

    /// <summary>The card content is read from the database (never trusted from the client), and only what the sender may see.</summary>
    private async Task<(string Body, string? Payload)> ContentAsync(SendMessageCommand r, Conversation c, CancellationToken ct)
    {
        switch (r.Type)
        {
            case MessageType.Text:
                return (r.Text!.Trim(), null);
            case MessageType.Product:
                var p = await db.Products.AsNoTracking().Where(x => x.Id == r.ProductId && x.Status == ProductStatus.Active)
                            .Select(x => new
                            {
                                x.Id, x.Name, x.MinPrice,
                                Image = x.Media.Where(m => m.Type == MediaType.Image).OrderBy(m => m.SortOrder).Select(m => m.Url).FirstOrDefault(),
                            }).FirstOrDefaultAsync(ct)
                        ?? throw new NotFoundException("Không tìm thấy sản phẩm.");
                return (p.Name, JsonSerializer.Serialize(new { productId = p.Id, name = p.Name, price = p.MinPrice, imageUrl = p.Image }, Json));
            case MessageType.Order:
                // Only an order between this buyer and this shop
                var o = await db.Orders.AsNoTracking().Where(x => x.Code == r.OrderCode && x.BuyerId == c.BuyerId && x.ShopId == c.ShopId)
                            .Select(x => new { x.Code, x.Status, x.GrandTotal, Items = x.Items.Count }).FirstOrDefaultAsync(ct)
                        ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
                return (o.Code, JsonSerializer.Serialize(new { code = o.Code, status = Domain.Sales.OrderStateMachine.Label(o.Status), total = o.GrandTotal, items = o.Items }, Json));
            case MessageType.Voucher:
                var v = await db.Vouchers.AsNoTracking().Where(x => x.Id == r.VoucherId && x.IsActive && (x.ShopId == c.ShopId || x.Owner == Domain.Promo.VoucherOwner.Platform))
                            .Select(x => new { x.Id, x.Code, x.Name, x.EndAt }).FirstOrDefaultAsync(ct)
                        ?? throw new NotFoundException("Không tìm thấy voucher.");
                return (v.Code, JsonSerializer.Serialize(new { voucherId = v.Id, code = v.Code, name = v.Name, endAt = v.EndAt }, Json));
            case MessageType.Image:
                var a = await db.MediaAssets.AsNoTracking()
                            .FirstOrDefaultAsync(x => x.Id == r.ImageAssetId && x.OwnerUserId == currentUser.UserId && x.Bucket == Buckets.Chat, ct)
                        ?? throw new NotFoundException("Không tìm thấy ảnh.");
                return ("", JsonSerializer.Serialize(new { key = ImageSizes.Key(a.ObjectKey, ImageSizes.Large) }, Json));
            default:
                throw new BusinessRuleException("Loại tin nhắn không hợp lệ.");
        }
    }
}

/// <summary>
/// The conversation row follows its messages (spec bẫy 2): last message, "awaiting a shop answer", and the unread
/// counters — counted from <c>messages</c> (the other side's messages with no read time), never incremented. Called
/// inside the conversation's lock, after the messages are saved.
/// </summary>
internal static class ConversationState
{
    public static Task<int> AfterPostAsync(IApplicationDbContext db, Guid conversationId, ChatRole by, string preview, DateTimeOffset at, CancellationToken ct)
    {
        var text = preview.Length > 120 ? preview[..120] : preview;
        var role = by.ToString();
        return db.ExecuteSqlAsync($"""
            UPDATE engage.conversations c SET
                last_message_preview = CASE WHEN {at} >= c.last_message_at THEN {text} ELSE c.last_message_preview END,
                last_message_at = GREATEST(c.last_message_at, {at}),
                awaiting_reply_since = CASE {role} WHEN 'Buyer' THEN COALESCE(c.awaiting_reply_since, {at}) WHEN 'Shop' THEN NULL ELSE c.awaiting_reply_since END,
                buyer_unread = (SELECT count(*) FROM engage.messages m WHERE m.conversation_id = c.id AND m.read_at IS NULL AND m.sender_role <> 'Buyer'),
                shop_unread = (SELECT count(*) FROM engage.messages m WHERE m.conversation_id = c.id AND m.read_at IS NULL AND m.sender_role = 'Buyer')
            WHERE c.id = {conversationId}
            """, ct);
    }

    public static Task<int> RecountAsync(IApplicationDbContext db, Guid conversationId, CancellationToken ct) => db.ExecuteSqlAsync($"""
        UPDATE engage.conversations c SET
            buyer_unread = (SELECT count(*) FROM engage.messages m WHERE m.conversation_id = c.id AND m.read_at IS NULL AND m.sender_role <> 'Buyer'),
            shop_unread = (SELECT count(*) FROM engage.messages m WHERE m.conversation_id = c.id AND m.read_at IS NULL AND m.sender_role = 'Buyer')
        WHERE c.id = {conversationId}
        """, ct);
}

public record MarkConversationReadCommand(Guid ConversationId, Guid? ShopId) : IRequest<Unit>;

/// <summary>"Đã xem": the other side's messages get their read time, the reader's unread counter goes to 0, the sender sees it live.</summary>
public sealed class MarkConversationReadHandler(IApplicationDbContext db, ChatAccess access, IRealtime realtime, IClock clock)
    : IRequestHandler<MarkConversationReadCommand, Unit>
{
    public async Task<Unit> Handle(MarkConversationReadCommand request, CancellationToken ct)
    {
        var side = request.ShopId is null ? ChatRole.Buyer : ChatRole.Shop;
        var conversation = request.ShopId is { } shopId
            ? (await access.AsShopAsync(shopId, request.ConversationId, ct)).Conversation
            : await access.AsBuyerAsync(request.ConversationId, ct);
        var now = clock.UtcNow;
        var others = side == ChatRole.Buyer ? new[] { ChatRole.Shop, ChatRole.System } : [ChatRole.Buyer];
        await using (var tx = await db.BeginTransactionAsync(ct))
        {
            await db.LockAsync($"chat:{conversation.Id}", ct);
            await db.ExecuteSqlAsync($"""
                UPDATE engage.messages SET read_at = {now}
                WHERE conversation_id = {conversation.Id} AND read_at IS NULL AND sender_role = ANY({others.Select(r => r.ToString()).ToArray()})
                """, ct);
            await ConversationState.RecountAsync(db, conversation.Id, ct);
            await tx.CommitAsync(ct);
        }
        await realtime.ToUsersAsync(await access.ParticipantsAsync(conversation, ct), RealtimeEvents.ChatRead,
            new { conversationId = conversation.Id, side, at = now }, ct);
        return Unit.Value;
    }
}

public record BlockShopCommand(Guid ConversationId, bool Blocked) : IRequest<Unit>;

public sealed class BlockShopHandler(IApplicationDbContext db, ChatAccess access) : IRequestHandler<BlockShopCommand, Unit>
{
    public async Task<Unit> Handle(BlockShopCommand request, CancellationToken ct)
    {
        var conversation = await access.AsBuyerAsync(request.ConversationId, ct);
        conversation.SetBlocked(request.Blocked);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public record ReportShopCommand(Guid ConversationId, string Reason) : IRequest<Unit>;

public sealed class ReportShopHandler(IApplicationDbContext db, ChatAccess access, ICurrentUser currentUser, IClock clock) : IRequestHandler<ReportShopCommand, Unit>
{
    public async Task<Unit> Handle(ReportShopCommand request, CancellationToken ct)
    {
        var conversation = await access.AsBuyerAsync(request.ConversationId, ct);
        db.ChatReports.Add(new ChatReport(conversation.Id, UserGuard.Require(currentUser), request.Reason ?? "", clock.UtcNow));
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

/// <summary>Typing indicator relayed by the hub to the other participants (nothing is stored).</summary>
public sealed class ChatTyping(IApplicationDbContext db, IRealtime realtime)
{
    public async Task RelayAsync(Guid userId, Guid conversationId, CancellationToken ct)
    {
        var c = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (c is null) return;
        var staff = await db.ShopStaff.AsNoTracking().Where(s => s.ShopId == c.ShopId).ToListAsync(ct);
        var shopSide = staff.Where(s => s.Has(ShopPermissions.ChatManage)).Select(s => s.UserId).ToList();
        ChatRole side;
        if (c.BuyerId == userId) side = ChatRole.Buyer;
        else if (shopSide.Contains(userId)) side = ChatRole.Shop;
        else return;
        var to = side == ChatRole.Buyer ? shopSide : [c.BuyerId];
        await realtime.ToUsersAsync(to, RealtimeEvents.ChatTyping, new { conversationId, side }, ct);
    }
}

// ---------- shop side ----------

public enum ShopInboxFilter
{
    All,
    Unread,
    Mine,
    Unassigned,
}

public record ShopConversationsQuery(Guid ShopId, ShopInboxFilter Filter = ShopInboxFilter.All, string? Q = null, int Page = 1, int PageSize = 30)
    : IRequest<PagedResult<ConversationDto>>, IPagedRequest;

public sealed class ShopConversationsHandler(IApplicationDbContext db, SellerAccess seller) : IRequestHandler<ShopConversationsQuery, PagedResult<ConversationDto>>
{
    public async Task<PagedResult<ConversationDto>> Handle(ShopConversationsQuery request, CancellationToken ct)
    {
        var staff = await seller.RequireAsync(request.ShopId, ShopPermissions.ChatManage, ct);
        var q = request.Q?.Trim().ToLower() ?? "";
        var query = db.Conversations.AsNoTracking().Where(c => c.ShopId == request.ShopId
            && (request.Filter != ShopInboxFilter.Unread || c.ShopUnread > 0)
            && (request.Filter != ShopInboxFilter.Mine || c.AssignedTo == staff.UserId)
            && (request.Filter != ShopInboxFilter.Unassigned || c.AssignedTo == null)
            && (q == "" || db.Users.Any(u => u.Id == c.BuyerId && u.FullName.ToLower().Contains(q))))
            .OrderByDescending(c => c.LastMessageAt).ThenBy(c => c.Id);
        var rows = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new PagedResult<ConversationDto>(await ChatViews.ConversationsAsync(db, rows, ChatRole.Shop, ct), await query.CountAsync(ct),
            request.Page, request.PageSize);
    }
}

public record AssignConversationCommand(Guid ShopId, Guid ConversationId, Guid? StaffUserId) : IRequest<Unit>;

/// <summary>Giao hội thoại: to a staff member who handles chat (or back to everybody).</summary>
public sealed class AssignConversationHandler(IApplicationDbContext db, ChatAccess access) : IRequestHandler<AssignConversationCommand, Unit>
{
    public async Task<Unit> Handle(AssignConversationCommand request, CancellationToken ct)
    {
        var (conversation, _) = await access.AsShopAsync(request.ShopId, request.ConversationId, ct);
        if (request.StaffUserId is { } staffId)
        {
            var target = await db.ShopStaff.AsNoTracking().FirstOrDefaultAsync(s => s.ShopId == request.ShopId && s.UserId == staffId, ct);
            if (target is null || !target.Has(ShopPermissions.ChatManage)) throw new NotFoundException("Nhân viên này không trực chat của shop.");
        }
        conversation.AssignTo(request.StaffUserId);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public record ChatStaffDto(Guid UserId, string Name);

public record ChatStaffQuery(Guid ShopId) : IRequest<IReadOnlyList<ChatStaffDto>>;

public sealed class ChatStaffHandler(IApplicationDbContext db, SellerAccess seller) : IRequestHandler<ChatStaffQuery, IReadOnlyList<ChatStaffDto>>
{
    public async Task<IReadOnlyList<ChatStaffDto>> Handle(ChatStaffQuery request, CancellationToken ct)
    {
        await seller.RequireAsync(request.ShopId, ShopPermissions.ChatManage, ct);
        var staff = await db.ShopStaff.AsNoTracking().Where(s => s.ShopId == request.ShopId).ToListAsync(ct);
        var ids = staff.Where(s => s.Has(ShopPermissions.ChatManage)).Select(s => s.UserId).ToList();
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).OrderBy(u => u.FullName).Select(u => new ChatStaffDto(u.Id, u.FullName)).ToListAsync(ct);
    }
}

public record QuickReplyDto(Guid Id, string Shortcut, string Content);

public record QuickRepliesQuery(Guid ShopId) : IRequest<IReadOnlyList<QuickReplyDto>>;

public sealed class QuickRepliesHandler(IApplicationDbContext db, SellerAccess seller) : IRequestHandler<QuickRepliesQuery, IReadOnlyList<QuickReplyDto>>
{
    public async Task<IReadOnlyList<QuickReplyDto>> Handle(QuickRepliesQuery request, CancellationToken ct)
    {
        await seller.RequireAsync(request.ShopId, ShopPermissions.ChatManage, ct);
        return await db.QuickReplies.AsNoTracking().Where(q => q.ShopId == request.ShopId).OrderBy(q => q.Shortcut)
            .Select(q => new QuickReplyDto(q.Id, q.Shortcut, q.Content)).ToListAsync(ct);
    }
}

public record SaveQuickReplyCommand(Guid ShopId, Guid? Id, string Shortcut, string Content) : IRequest<Guid>;

public sealed class SaveQuickReplyHandler(IApplicationDbContext db, SellerAccess seller) : IRequestHandler<SaveQuickReplyCommand, Guid>
{
    public async Task<Guid> Handle(SaveQuickReplyCommand request, CancellationToken ct)
    {
        await seller.RequireAsync(request.ShopId, ShopPermissions.ChatManage, ct);
        QuickReply reply;
        if (request.Id is { } id)
        {
            reply = await db.QuickReplies.FirstOrDefaultAsync(q => q.Id == id && q.ShopId == request.ShopId, ct) ?? throw new NotFoundException("Không tìm thấy tin mẫu.");
            reply.Update(request.Shortcut ?? "", request.Content ?? "");
        }
        else
        {
            if (await db.QuickReplies.CountAsync(q => q.ShopId == request.ShopId, ct) >= 50) throw new ConflictException("Tối đa 50 tin trả lời nhanh.", "QUICK_REPLY_LIMIT");
            reply = new QuickReply(request.ShopId, request.Shortcut ?? "", request.Content ?? "");
            db.QuickReplies.Add(reply);
        }
        await db.SaveChangesAsync(ct);
        return reply.Id;
    }
}

public record DeleteQuickReplyCommand(Guid ShopId, Guid Id) : IRequest<Unit>;

public sealed class DeleteQuickReplyHandler(IApplicationDbContext db, SellerAccess seller) : IRequestHandler<DeleteQuickReplyCommand, Unit>
{
    public async Task<Unit> Handle(DeleteQuickReplyCommand request, CancellationToken ct)
    {
        await seller.RequireAsync(request.ShopId, ShopPermissions.ChatManage, ct);
        await db.QuickReplies.Where(q => q.Id == request.Id && q.ShopId == request.ShopId).ExecuteDeleteAsync(ct);
        return Unit.Value;
    }
}

public record ChatSettingsDto(bool AutoReplyEnabled, string AutoReplyText, TimeOnly OpenFrom, TimeOnly OpenTo);

public record ChatSettingsQuery(Guid ShopId) : IRequest<ChatSettingsDto>;

public sealed class ChatSettingsHandler(IApplicationDbContext db, SellerAccess seller) : IRequestHandler<ChatSettingsQuery, ChatSettingsDto>
{
    public async Task<ChatSettingsDto> Handle(ChatSettingsQuery request, CancellationToken ct)
    {
        await seller.RequireAsync(request.ShopId, ShopPermissions.ChatManage, ct);
        var s = await db.ShopChatSettings.AsNoTracking().FirstOrDefaultAsync(x => x.ShopId == request.ShopId, ct) ?? new ShopChatSettings(request.ShopId);
        return new ChatSettingsDto(s.AutoReplyEnabled, s.AutoReplyText, s.OpenFrom, s.OpenTo);
    }
}

public record SaveChatSettingsCommand(Guid ShopId, bool AutoReplyEnabled, string AutoReplyText, TimeOnly OpenFrom, TimeOnly OpenTo) : IRequest<Unit>;

public sealed class SaveChatSettingsHandler(IApplicationDbContext db, SellerAccess seller) : IRequestHandler<SaveChatSettingsCommand, Unit>
{
    public async Task<Unit> Handle(SaveChatSettingsCommand request, CancellationToken ct)
    {
        await seller.RequireAsync(request.ShopId, ShopPermissions.SettingsManage, ct);
        var s = await db.ShopChatSettings.FirstOrDefaultAsync(x => x.ShopId == request.ShopId, ct);
        if (s is null)
        {
            s = new ShopChatSettings(request.ShopId);
            db.ShopChatSettings.Add(s);
        }
        s.Update(request.AutoReplyEnabled, request.AutoReplyText ?? "", request.OpenFrom, request.OpenTo);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ---------- shop chat performance (spec II.4: real numbers on the product / shop page) ----------

public record ChatPerformance(int ResponseRatePercent, string ResponseTime, int Conversations);

/// <summary>
/// Over CHAT.RESPONSE_WINDOW_DAYS: the share of conversations where the shop answered the buyer (within 12 hours of
/// the buyer's first message) and the median time of that first answer.
/// </summary>
public sealed class ChatPerformanceService(IApplicationDbContext db, ISystemParameters parameters, IClock clock)
{
    public async Task<ChatPerformance> OfShopAsync(Guid shopId, CancellationToken ct)
    {
        var days = await parameters.GetIntAsync(ParameterKeys.ChatResponseWindowDays, ct);
        var since = clock.UtcNow.AddDays(-days);
        var rows = await (from c in db.Conversations.AsNoTracking()
                          where c.ShopId == shopId && c.LastMessageAt >= since
                          let firstBuyer = db.ChatMessages.Where(m => m.ConversationId == c.Id && m.SenderRole == ChatRole.Buyer && m.CreatedAt >= since)
                              .Min(m => (DateTimeOffset?)m.CreatedAt)
                          select new
                          {
                              FirstBuyer = firstBuyer,
                              FirstShop = db.ChatMessages.Where(m => m.ConversationId == c.Id && m.SenderRole == ChatRole.Shop && m.CreatedAt >= firstBuyer)
                                  .Min(m => (DateTimeOffset?)m.CreatedAt),
                          }).ToListAsync(ct);
        var asked = rows.Where(r => r.FirstBuyer is not null).ToList();
        if (asked.Count == 0) return new ChatPerformance(0, "Chưa có dữ liệu", 0);
        var answered = asked.Where(r => r.FirstShop is not null && r.FirstShop - r.FirstBuyer <= TimeSpan.FromHours(12)).ToList();
        var times = asked.Where(r => r.FirstShop is not null).Select(r => (r.FirstShop - r.FirstBuyer)!.Value).OrderBy(t => t).ToList();
        var median = times.Count == 0 ? (TimeSpan?)null : times[times.Count / 2];
        var label = median switch
        {
            null => "Chưa phản hồi",
            { TotalMinutes: < 60 } => "trong vài phút",
            { TotalHours: < 24 } m => $"trong vài giờ ({Math.Ceiling(m.TotalHours)} giờ)",
            _ => "trong vài ngày",
        };
        return new ChatPerformance((int)Math.Round(answered.Count * 100.0 / asked.Count), label, asked.Count);
    }
}
