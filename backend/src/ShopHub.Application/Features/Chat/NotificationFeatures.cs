using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Storefront;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Common;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Chat;

// ---------- channel preferences (notification_prefs) ----------

public record NotificationPrefDto(NotificationCategory Category, NotificationChannel Channel, bool Enabled, bool Locked);

public record NotificationPrefsDto(IReadOnlyList<NotificationPrefDto> Prefs, bool HasEmail, bool HasPhone);

public record NotificationPrefsQuery : IRequest<NotificationPrefsDto>;

public sealed class NotificationPrefsHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<NotificationPrefsQuery, NotificationPrefsDto>
{
    public async Task<NotificationPrefsDto> Handle(NotificationPrefsQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var saved = await db.NotificationPrefs.AsNoTracking().Where(p => p.UserId == userId).ToListAsync(ct);
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.Email, u.Phone }).SingleAsync(ct);
        var prefs = (from c in Enum.GetValues<NotificationCategory>()
                     from ch in Enum.GetValues<NotificationChannel>()
                     let row = saved.FirstOrDefault(p => p.Category == c && p.Channel == ch)
                     // Order, wallet and account news always show in the app; promotions can be turned off there too
                     let locked = ch == NotificationChannel.InApp && c != NotificationCategory.Promotion
                     select new NotificationPrefDto(c, ch, locked || (row?.Enabled ?? NotificationPref.Default(c, ch)), locked)).ToList();
        return new NotificationPrefsDto(prefs, user.Email is not null, user.Phone is not null);
    }
}

public record PrefInput(NotificationCategory Category, NotificationChannel Channel, bool Enabled);

public record SaveNotificationPrefsCommand(IReadOnlyList<PrefInput> Prefs) : IRequest<Unit>;

/// <summary>In-app stays on for orders, wallet and account news (they are the inbox itself); promotions and every other channel follow the user's choice.</summary>
public sealed class SaveNotificationPrefsHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<SaveNotificationPrefsCommand, Unit>
{
    public async Task<Unit> Handle(SaveNotificationPrefsCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var saved = await db.NotificationPrefs.Where(p => p.UserId == userId).ToListAsync(ct);
        foreach (var p in (request.Prefs ?? []).Where(p => p.Channel != NotificationChannel.InApp || p.Category == NotificationCategory.Promotion))
        {
            var row = saved.FirstOrDefault(x => x.Category == p.Category && x.Channel == p.Channel);
            if (row is null) db.NotificationPrefs.Add(new NotificationPref(userId, p.Category, p.Channel, p.Enabled));
            else row.Set(p.Enabled);
        }
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public record RegisterDeviceCommand(string Platform, string Token) : IRequest<Unit>;

public sealed class RegisterDeviceValidator : AbstractValidator<RegisterDeviceCommand>
{
    public RegisterDeviceValidator()
    {
        RuleFor(x => x.Platform).Must(p => p is "android" or "ios" or "web").WithMessage("Nền tảng không hợp lệ.");
        RuleFor(x => x.Token).NotEmpty().WithMessage("Thiếu mã thiết bị.").MaximumLength(500).WithMessage("Mã thiết bị quá dài.");
    }
}

/// <summary>FCM token of a device (the same token moves to whoever signs in on it).</summary>
public sealed class RegisterDeviceHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock) : IRequestHandler<RegisterDeviceCommand, Unit>
{
    public async Task<Unit> Handle(RegisterDeviceCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        await db.DeviceTokens.Where(d => d.Token == request.Token && d.UserId != userId).ExecuteDeleteAsync(ct);
        var row = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == request.Token, ct);
        if (row is null) db.DeviceTokens.Add(new DeviceToken(userId, request.Platform, request.Token, clock.UtcNow));
        else row.Seen(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ---------- platform broadcast (VI.6) ----------

public record BroadcastDto(Guid Id, string Title, string Body, string? Link, BroadcastSegment Segment, int Recipients, int SkippedToday, DateTimeOffset CreatedAt);

public record BroadcastsQuery : IRequest<IReadOnlyList<BroadcastDto>>;

public sealed class BroadcastsHandler(IApplicationDbContext db) : IRequestHandler<BroadcastsQuery, IReadOnlyList<BroadcastDto>>
{
    public async Task<IReadOnlyList<BroadcastDto>> Handle(BroadcastsQuery request, CancellationToken ct) =>
        await db.Broadcasts.AsNoTracking().OrderByDescending(b => b.CreatedAt).Take(50)
            .Select(b => new BroadcastDto(b.Id, b.Title, b.Body, b.Link, b.Segment, b.Recipients, b.SkippedToday, b.CreatedAt)).ToListAsync(ct);
}

public record SendBroadcastCommand(string Title, string Body, string? Link, BroadcastSegment Segment) : IRequest<BroadcastDto>;

/// <summary>
/// Gửi thông báo hàng loạt theo phân khúc (active accounts only): one promotion per person per campaign and never more
/// than one promotion per person per Vietnam day — kept by unique indexes (<see cref="PromoNotifications"/>), so two
/// broadcasts sent at the same moment cannot both reach anyone. People who turned promotions off are skipped. The member
/// segments are exact tiers by the same spending as <see cref="Marketing.Membership"/>.
/// </summary>
public sealed partial class SendBroadcastHandler(IApplicationDbContext db, ISystemParameters parameters, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<SendBroadcastCommand, BroadcastDto>
{
    public async Task<BroadcastDto> Handle(SendBroadcastCommand request, CancellationToken ct)
    {
        var adminId = currentUser.UserId ?? throw new AuthenticationFailedException("Bạn cần đăng nhập để tiếp tục.");
        if (request.Link is { } link && !Domain.Promo.Banner.IsSafeLink(link))
            throw new BusinessRuleException("Liên kết phải là đường dẫn trong sàn (/...) hoặc https://.");
        var now = clock.UtcNow;
        var broadcast = new Broadcast(request.Title ?? "", request.Body ?? "", request.Link, request.Segment, adminId, now);
        db.Broadcasts.Add(broadcast);

        var users = db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active).Select(u => u.Id);
        List<Guid> recipients;
        if (request.Segment is BroadcastSegment.MemberGold or BroadcastSegment.MemberDiamond)
        {
            var gold = await parameters.GetIntAsync(ParameterKeys.MemberGoldMinSpend, ct);
            var diamond = await parameters.GetIntAsync(ParameterKeys.MemberDiamondMinSpend, ct);
            var spend = await Marketing.Membership.SpendByBuyerAsync(db, now.AddDays(-await parameters.GetIntAsync(ParameterKeys.MemberWindowDays, ct)), ct);
            var active = (await users.Where(id => spend.Keys.Contains(id)).ToListAsync(ct)).ToHashSet();
            recipients = MemberSegment(request.Segment, spend, active, gold, diamond);
        }
        else
            recipients = await (request.Segment == BroadcastSegment.NoOrderYet ? users.Where(id => !db.Orders.Any(o => o.BuyerId == id)) : users).ToListAsync(ct);
        await db.SaveChangesAsync(ct);
        var sent = await PromoNotifications.SendAsync(db, recipients.Select(userId => new PromoNotice(userId, broadcast.Title, broadcast.Body, broadcast.Link,
            "broadcast", broadcast.Id, $"broadcast:{broadcast.Id}")).ToList(), now, ct);
        broadcast.Sent(sent, recipients.Count - sent, now);
        await db.SaveChangesAsync(ct);
        return new BroadcastDto(broadcast.Id, broadcast.Title, broadcast.Body, broadcast.Link, broadcast.Segment, broadcast.Recipients,
            broadcast.SkippedToday, broadcast.CreatedAt);
    }
}

public sealed partial class SendBroadcastHandler
{
    /// <summary>Active members of exactly the segment's tier: Gold = gold ≤ spend &lt; diamond, Diamond = spend ≥ diamond.</summary>
    public static List<Guid> MemberSegment(BroadcastSegment segment, IReadOnlyDictionary<Guid, long> spend, IReadOnlySet<Guid> active, long gold, long diamond) =>
        spend.Where(x => active.Contains(x.Key) && (segment == BroadcastSegment.MemberDiamond ? x.Value >= diamond : x.Value >= gold && x.Value < diamond))
            .OrderBy(x => x.Key).Select(x => x.Key).ToList();
}

// ---------- public chat stats of a shop ----------

public record ShopChatStatsDto(int ResponseRatePercent, string ResponseTime, DateTimeOffset? LastActiveAt);

public record ShopChatStatsQuery(Guid ShopId) : IRequest<ShopChatStatsDto>;

/// <summary>Tỉ lệ & thời gian phản hồi chat, online lần cuối (spec II.4 shop block) — computed from the messages.</summary>
public sealed class ShopChatStatsHandler(IApplicationDbContext db, ChatPerformanceService performance) : IRequestHandler<ShopChatStatsQuery, ShopChatStatsDto>
{
    public async Task<ShopChatStatsDto> Handle(ShopChatStatsQuery request, CancellationToken ct)
    {
        if (!await db.Shops.AnyAsync(s => s.Id == request.ShopId, ct)) throw new NotFoundException("Không tìm thấy shop.");
        var p = await performance.OfShopAsync(request.ShopId, ct);
        var last = await (from m in db.ChatMessages.AsNoTracking()
                          join c in db.Conversations.AsNoTracking() on m.ConversationId equals c.Id
                          where c.ShopId == request.ShopId && m.SenderRole == ChatRole.Shop
                          select (DateTimeOffset?)m.CreatedAt).MaxAsync(ct);
        return new ShopChatStatsDto(p.ResponseRatePercent, p.ResponseTime, last);
    }
}
