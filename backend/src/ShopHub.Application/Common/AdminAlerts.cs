using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Security;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Iam;

namespace ShopHub.Application.Common;

/// <summary>
/// In-app alert to every active platform admin holding a permission (or full rights), so a failed check or job is
/// seen by someone who can act on it — not only written to the log. One alert per <paramref name="dedupeKey"/> per admin.
/// </summary>
public sealed class AdminAlerts(IApplicationDbContext db, Features.Admin.MessageTemplates templates, IClock clock)
{
    /// <summary>The alert's text is the in-app template <paramref name="templateKey"/> filled with <paramref name="values"/> (F7).</summary>
    public async Task<int> RaiseAsync(string permission, string templateKey, IReadOnlyDictionary<string, string> values, string? link, string dedupeKey,
        CancellationToken ct)
    {
        var (title, body) = await templates.NoticeAsync(templateKey, values, ct);
        var holders = await (from ur in db.UserRoles
                             join r in db.Roles on ur.RoleId equals r.Id
                             join u in db.Users on ur.UserId equals u.Id
                             where u.Status == UserStatus.Active
                                   && r.Permissions.Any(p => p.PermissionCode == permission || p.PermissionCode == Permissions.All)
                             select ur.UserId).Distinct().ToListAsync(ct);
        var already = await db.Notifications.Where(n => holders.Contains(n.UserId) && n.DedupeKey == dedupeKey).Select(n => n.UserId).ToListAsync(ct);
        var now = clock.UtcNow;
        var fresh = holders.Except(already).ToList();
        foreach (var userId in fresh)
            db.Notifications.Add(new Notification(userId, NotificationCategory.Activity, title, body, link, "admin-alert", null, now, dedupeKey));
        return fresh.Count;
    }
}
