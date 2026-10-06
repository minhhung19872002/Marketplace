using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Engage;
using ShopHub.Infrastructure.Outbox;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Notifications;

public record PushMessage(string Title, string Body, string? Link);

/// <summary>Mobile push (FCM) — prepared for the app (spec VII, 6.7); simulated until FCM keys are configured.</summary>
public interface IPushSender
{
    Task SendAsync(IReadOnlyList<string> deviceTokens, PushMessage message, CancellationToken ct);
}

/// <summary>Stands in for FCM: accepts the push and logs how many devices it went to (never the tokens).</summary>
public sealed class SimulatedPushSender(ILogger<SimulatedPushSender> logger) : IPushSender
{
    public Task SendAsync(IReadOnlyList<string> deviceTokens, PushMessage message, CancellationToken ct)
    {
        logger.LogInformation("Simulated push \"{Title}\" to {Devices} device(s)", message.Title, deviceTokens.Count);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Delivers one in-app notification on every channel the user wants (spec VII, 4.8 notification_prefs): in-app is
/// pushed live to the open tabs (SignalR); email (HTML, SMTP — Mailpit in the dev stack), SMS and push follow the
/// user's choices, defaulting to email for orders and wallet only. Runs from the outbox after the business commit.
/// </summary>
public sealed class NotificationDeliveryHandler(
    ShopHubDbContext db,
    IRealtime realtime,
    IEmailSender email,
    ISmsSender sms,
    IPushSender push,
    ISystemParameters parameters,
    ILogger<NotificationDeliveryHandler> logger) : IOutboxHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Type => OutboxTypes.NotifyDeliver;

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<NotifyDeliverPayload>(payload, Json) ?? throw new InvalidOperationException("Tin outbox rỗng.");
        var n = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == p.NotificationId, ct);
        if (n is null) return;
        var user = await db.Users.AsNoTracking().Where(u => u.Id == n.UserId).Select(u => new { u.Email, u.Phone, u.FullName }).FirstOrDefaultAsync(ct);
        if (user is null) return;
        var prefs = await db.NotificationPrefs.AsNoTracking().Where(x => x.UserId == n.UserId && x.Category == n.Category)
            .ToDictionaryAsync(x => x.Channel, x => x.Enabled, ct);
        bool Wants(NotificationChannel c) => prefs.TryGetValue(c, out var on) ? on : NotificationPref.Default(n.Category, c);

        var unread = await db.Notifications.CountAsync(x => x.UserId == n.UserId && !x.IsRead, ct);
        await realtime.ToUsersAsync([n.UserId], RealtimeEvents.Notification,
            new { n.Id, n.Title, n.Body, n.Link, n.Category, n.CreatedAt, unread }, ct);

        if (Wants(NotificationChannel.Email) && user.Email is { } address)
            await email.SendAsync(address, n.Title, await HtmlAsync(n, user.FullName, ct), ct);
        if (Wants(NotificationChannel.Sms) && user.Phone is { } phone)
            await sms.SendAsync(phone, $"ShopHub: {n.Title}. {n.Body}", ct);
        if (Wants(NotificationChannel.Push))
        {
            var tokens = await db.DeviceTokens.AsNoTracking().Where(d => d.UserId == n.UserId).Select(d => d.Token).ToListAsync(ct);
            if (tokens.Count > 0) await push.SendAsync(tokens, new PushMessage(n.Title, n.Body, n.Link), ct);
        }
        logger.LogDebug("Notification {NotificationId} delivered", n.Id);
    }

    /// <summary>The notification as a small HTML mail (every value HTML-encoded; the link made absolute).</summary>
    private async Task<string> HtmlAsync(Notification n, string name, CancellationToken ct)
    {
        var site = await parameters.GetStringAsync(ParameterKeys.SitePlatformName, ct);
        var baseUrl = (await parameters.GetStringAsync(ParameterKeys.SitePublicUrl, ct)).TrimEnd('/');
        var link = n.Link is null ? null : n.Link.StartsWith('/') ? baseUrl + n.Link : n.Link;
        var e = (string s) => WebUtility.HtmlEncode(s);
        return $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:auto;border:1px solid #eee">
              <div style="background:#ee4d2d;color:#fff;padding:14px 18px;font-size:18px;font-weight:bold">{e(site)}</div>
              <div style="padding:18px">
                <p>Xin chào {e(name)},</p>
                <h2 style="font-size:17px;margin:12px 0">{e(n.Title)}</h2>
                <p>{e(n.Body)}</p>
                {(link is null ? "" : $"<p><a href=\"{e(link)}\" style=\"background:#ee4d2d;color:#fff;padding:9px 18px;text-decoration:none;border-radius:3px\">Xem chi tiết</a></p>")}
                <p style="color:#888;font-size:12px">Bạn nhận thư này vì đã bật thông báo qua email. Có thể tắt trong Tài khoản → Cài đặt thông báo.</p>
              </div>
            </div>
            """;
    }
}
