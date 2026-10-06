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
                     select new NotificationPrefDto(c, ch, ch == NotificationChannel.InApp || (row?.Enabled ?? NotificationPref.Default(c, ch)),
                         ch == NotificationChannel.InApp)).ToList();
        return new NotificationPrefsDto(prefs, user.Email is not null, user.Phone is not null);
    }
}

public record PrefInput(NotificationCategory Category, NotificationChannel Channel, bool Enabled);

public record SaveNotificationPrefsCommand(IReadOnlyList<PrefInput> Prefs) : IRequest<Unit>;

/// <summary>In-app stays on (it is the inbox itself); every other channel follows the user's choice per category.</summary>
public sealed class SaveNotificationPrefsHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<SaveNotificationPrefsCommand, Unit>
{
    public async Task<Unit> Handle(SaveNotificationPrefsCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var saved = await db.NotificationPrefs.Where(p => p.UserId == userId).ToListAsync(ct);
        foreach (var p in (request.Prefs ?? []).Where(p => p.Channel != NotificationChannel.InApp))
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
/// Gửi thông báo hàng loạt theo phân khúc: one promotion notification per person per campaign (dedupe key) and never
/// more than one promotion notification per person per Vietnam day — people who already got one today are skipped.
/// </summary>
public sealed class SendBroadcastHandler(IApplicationDbContext db, ISystemParameters parameters, ICurrentUser currentUser, IClock clock)
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
        var days = await parameters.GetIntAsync(ParameterKeys.MemberWindowDays, ct);
        var since = now.AddDays(-days);
        var minSpend = request.Segment == BroadcastSegment.MemberDiamond
            ? await parameters.GetIntAsync(ParameterKeys.MemberDiamondMinSpend, ct)
            : await parameters.GetIntAsync(ParameterKeys.MemberGoldMinSpend, ct);
        IQueryable<Guid> targets = request.Segment switch
        {
            BroadcastSegment.NoOrderYet => users.Where(id => !db.Orders.Any(o => o.BuyerId == id)),
            BroadcastSegment.MemberGold or BroadcastSegment.MemberDiamond =>
                db.Orders.Where(o => o.Status == OrderStatus.Completed && o.CompletedAt >= since).GroupBy(o => o.BuyerId)
                    .Where(g => g.Sum(o => o.GrandTotal) >= minSpend)
                    .Select(g => g.Key),
            _ => users,
        };
        var dayStart = new DateTimeOffset(VietnamTime.ToLocal(now).Date, TimeSpan.FromHours(7)).ToUniversalTime();
        var recipients = await targets.ToListAsync(ct);
        var already = (await db.Notifications.AsNoTracking()
                .Where(n => n.Category == NotificationCategory.Promotion && n.CreatedAt >= dayStart && recipients.Contains(n.UserId))
                .Select(n => n.UserId).Distinct().ToListAsync(ct)).ToHashSet();
        var sending = recipients.Where(r => !already.Contains(r)).ToList();
        foreach (var batch in sending.Chunk(500))
        {
            foreach (var userId in batch)
                db.Notifications.Add(new Notification(userId, NotificationCategory.Promotion, broadcast.Title, broadcast.Body, broadcast.Link, "broadcast",
                    broadcast.Id, now, $"broadcast:{broadcast.Id}"));
            await db.SaveChangesAsync(ct);
        }
        broadcast.Sent(sending.Count, recipients.Count - sending.Count, now);
        await db.SaveChangesAsync(ct);
        return new BroadcastDto(broadcast.Id, broadcast.Title, broadcast.Body, broadcast.Link, broadcast.Segment, broadcast.Recipients,
            broadcast.SkippedToday, broadcast.CreatedAt);
    }
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
