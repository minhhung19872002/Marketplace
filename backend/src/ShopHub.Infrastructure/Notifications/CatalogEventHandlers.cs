using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.SystemConfig;
using ShopHub.Infrastructure.Outbox;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Notifications;

/// <summary>Tells the shop owner how their application went (SMS when they have a phone, email otherwise).</summary>
public sealed class ShopEventHandler(ShopHubDbContext db, ISmsSender sms, IEmailSender email) : IOutboxHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Type => OutboxTypes.ShopEvent;

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<ShopEventPayload>(payload, Json) ?? throw new InvalidOperationException("Tin outbox rỗng.");
        var target = await (from s in db.Shops.IgnoreQueryFilters()
                            join u in db.Users.IgnoreQueryFilters() on s.OwnerId equals u.Id
                            where s.Id == e.ShopId
                            select new { s.Name, u.Phone, u.Email }).FirstOrDefaultAsync(ct);
        if (target is null) return;

        var text = e.Event switch
        {
            "SUBMITTED" => $"ShopHub: Ho so shop \"{target.Name}\" da duoc gui, san se duyet trong 1-2 ngay lam viec.",
            "APPROVE" => $"ShopHub: Shop \"{target.Name}\" da duoc duyet. Ban co the dang ban ngay tai Kenh Nguoi Ban.",
            "REJECT" => $"ShopHub: Ho so shop \"{target.Name}\" chua duoc duyet. Ly do: {e.Reason}",
            "LOCK" => $"ShopHub: Shop \"{target.Name}\" tam thoi bi khoa. Ly do: {e.Reason}",
            "UNLOCK" => $"ShopHub: Shop \"{target.Name}\" da duoc mo khoa.",
            "PENALTY" => $"ShopHub: Shop \"{target.Name}\" bi ghi diem phat. Ly do: {e.Reason}. Xem tai Kenh Nguoi Ban > Hieu qua hoat dong.",
            _ => null,
        };
        if (text is null) return;
        await NotifyAsync(target.Phone, target.Email, $"Thông báo shop {target.Name}", text, ct);
    }

    internal async Task NotifyAsync(string? phone, string? emailAddress, string subject, string text, CancellationToken ct)
    {
        if (phone is not null) await sms.SendAsync(phone, text, ct);
        else if (emailAddress is not null) await email.SendAsync(emailAddress, subject, $"<p>{System.Net.WebUtility.HtmlEncode(text)}</p>", ct);
    }
}

/// <summary>
/// Review / lock decisions on a product (spec VII "sản phẩm bị khoá"): an in-app notification to the shop owner and the
/// staff who see products, worded by the editable PRODUCT.* templates; email / SMS / push then follow each person's
/// notification settings (NotificationDeliveryHandler).
/// </summary>
public sealed class ProductEventHandler(ShopHubDbContext db, Application.Features.Admin.MessageTemplates templates, Application.Abstractions.IClock clock)
    : IOutboxHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Events = ["APPROVE", "REJECT", "BAN", "UNBAN"];

    public string Type => OutboxTypes.ProductEvent;

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<ProductEventPayload>(payload, Json) ?? throw new InvalidOperationException("Tin outbox rỗng.");
        if (!Events.Contains(e.Event)) return;
        var product = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == e.ProductId)
            .Select(p => new { p.Id, p.Name, p.ShopId }).FirstOrDefaultAsync(ct);
        if (product is null) return;

        var (title, body) = await templates.RenderAsync(Application.Features.Admin.TemplateCatalog.ProductKey(e.Event), Domain.SystemConfig.TemplateChannel.InApp,
            new Dictionary<string, string> { ["product"] = product.Name, ["reason"] = e.Reason ?? "" }, ct);
        var key = $"product:{product.Id}:{e.Event}:{e.EventId:N}";
        var staff = await db.ShopStaff.AsNoTracking().Where(s => s.ShopId == product.ShopId).ToListAsync(ct);
        foreach (var member in staff.Where(s => s.Role == Domain.Shops.ShopStaffRole.Owner || s.Has(Application.Security.ShopPermissions.ProductView)))
        {
            if (await db.Notifications.AnyAsync(n => n.UserId == member.UserId && n.DedupeKey == key, ct)) continue;
            db.Notifications.Add(new Domain.Engage.Notification(member.UserId, Domain.Engage.NotificationCategory.Activity, title ?? "", body,
                $"/seller/san-pham/{product.Id}", "product", product.Id, clock.UtcNow, key));
        }
        await db.SaveChangesAsync(ct);
    }
}
