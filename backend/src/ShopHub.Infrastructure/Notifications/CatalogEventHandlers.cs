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

public sealed class ProductEventHandler(ShopHubDbContext db, ISmsSender sms, IEmailSender email) : IOutboxHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Type => OutboxTypes.ProductEvent;

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<ProductEventPayload>(payload, Json) ?? throw new InvalidOperationException("Tin outbox rỗng.");
        var target = await (from p in db.Products.IgnoreQueryFilters()
                            join s in db.Shops.IgnoreQueryFilters() on p.ShopId equals s.Id
                            join u in db.Users.IgnoreQueryFilters() on s.OwnerId equals u.Id
                            where p.Id == e.ProductId
                            select new { p.Name, u.Phone, u.Email }).FirstOrDefaultAsync(ct);
        if (target is null) return;

        var name = target.Name.Length > 40 ? target.Name[..40] + "…" : target.Name;
        var text = e.Event switch
        {
            "APPROVE" => $"ShopHub: San pham \"{name}\" da duoc duyet va dang ban.",
            "REJECT" => $"ShopHub: San pham \"{name}\" can chinh sua truoc khi duyet: {e.Reason}",
            "BAN" => $"ShopHub: San pham \"{name}\" bi khoa do vi pham: {e.Reason}",
            "UNBAN" => $"ShopHub: San pham \"{name}\" da duoc mo khoa (dang an, ban co the hien lai).",
            _ => null,
        };
        if (text is null) return;
        if (target.Phone is not null) await sms.SendAsync(target.Phone, text, ct);
        else if (target.Email is not null)
            await email.SendAsync(target.Email, "Thông báo sản phẩm", $"<p>{System.Net.WebUtility.HtmlEncode(text)}</p>", ct);
    }
}
