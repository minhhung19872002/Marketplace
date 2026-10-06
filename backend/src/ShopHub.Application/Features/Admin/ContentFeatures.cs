using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Audit;
using ShopHub.Application.Features.Reports;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Common;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Sales;
using ShopHub.Domain.SystemConfig;

namespace ShopHub.Application.Features.Admin;

// =====================================================================================================================
// VI.8 static pages & help center
// =====================================================================================================================

public record CmsPageDto(Guid Id, CmsKind Kind, string Slug, string Title, string Content, string? Topic, int SortOrder, bool IsPublished, DateTimeOffset UpdatedAt);

public record CmsSummaryDto(string Slug, string Title, string? Topic, CmsKind Kind);

public record AdminCmsPagesQuery(CmsKind? Kind) : IRequest<IReadOnlyList<CmsPageDto>>;

public sealed class AdminCmsPagesHandler(IApplicationDbContext db) : IRequestHandler<AdminCmsPagesQuery, IReadOnlyList<CmsPageDto>>
{
    public async Task<IReadOnlyList<CmsPageDto>> Handle(AdminCmsPagesQuery request, CancellationToken ct) =>
        await db.CmsPages.AsNoTracking().Where(p => request.Kind == null || p.Kind == request.Kind)
            .OrderBy(p => p.Kind).ThenBy(p => p.Topic).ThenBy(p => p.SortOrder).ThenBy(p => p.Slug)
            .Select(p => new CmsPageDto(p.Id, p.Kind, p.Slug, p.Title, p.Content, p.Topic, p.SortOrder, p.IsPublished, p.UpdatedAt)).ToListAsync(ct);
}

public record SaveCmsPageCommand(Guid? Id, CmsKind Kind, string Slug, string Title, string Content, string? Topic, int SortOrder, bool IsPublished) : IRequest<Guid>;

public sealed class SaveCmsPageValidator : AbstractValidator<SaveCmsPageCommand>
{
    public SaveCmsPageValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().Matches("^[a-z0-9]+(-[a-z0-9]+)*$").WithMessage("Đường dẫn chỉ gồm chữ thường không dấu, số và gạch ngang.").MaximumLength(120);
        RuleFor(x => x.Title).NotEmpty().WithMessage("Vui lòng nhập tiêu đề.").MaximumLength(200);
        RuleFor(x => x.Content).NotEmpty().WithMessage("Vui lòng nhập nội dung.").MaximumLength(200_000);
        RuleFor(x => x.Topic).NotEmpty().When(x => x.Kind == CmsKind.Help).WithMessage("Bài trợ giúp cần chủ đề.");
    }
}

/// <summary>HTML is sanitised on the way in (spec 6.1); the slug is unique across pages and help articles.</summary>
public sealed class SaveCmsPageHandler(IApplicationDbContext db, IHtmlSanitizer sanitizer, IClock clock) : IRequestHandler<SaveCmsPageCommand, Guid>
{
    public async Task<Guid> Handle(SaveCmsPageCommand request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var html = sanitizer.Sanitize(request.Content);
        if (await db.CmsPages.AnyAsync(p => p.Slug == request.Slug && p.Id != request.Id, ct)) throw new ConflictException("Đường dẫn này đã được dùng.", "SLUG_TAKEN");
        CmsPage page;
        if (request.Id is { } id)
        {
            page = await db.CmsPages.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Không tìm thấy trang.");
            page.Update(request.Title, html, request.Topic, request.SortOrder, request.IsPublished, now);
        }
        else
        {
            page = new CmsPage(request.Kind, request.Slug, request.Title, html, request.Topic, request.SortOrder, request.IsPublished, now);
            db.CmsPages.Add(page);
        }
        await db.SaveChangesAsync(ct);
        return page.Id;
    }
}

public record CmsPageBySlugQuery(string Slug) : IRequest<CmsPageDto>;

public sealed class CmsPageBySlugHandler(IApplicationDbContext db) : IRequestHandler<CmsPageBySlugQuery, CmsPageDto>
{
    public async Task<CmsPageDto> Handle(CmsPageBySlugQuery request, CancellationToken ct) =>
        await db.CmsPages.AsNoTracking().Where(p => p.Slug == request.Slug && p.IsPublished)
            .Select(p => new CmsPageDto(p.Id, p.Kind, p.Slug, p.Title, p.Content, p.Topic, p.SortOrder, p.IsPublished, p.UpdatedAt)).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Không tìm thấy trang.");
}

public record HelpCenterQuery(string? Q) : IRequest<IReadOnlyList<CmsSummaryDto>>;

/// <summary>Trung tâm trợ giúp: published articles by topic; the search matches title or content without tones.</summary>
public sealed class HelpCenterHandler(IApplicationDbContext db) : IRequestHandler<HelpCenterQuery, IReadOnlyList<CmsSummaryDto>>
{
    public async Task<IReadOnlyList<CmsSummaryDto>> Handle(HelpCenterQuery request, CancellationToken ct)
    {
        var all = await db.CmsPages.AsNoTracking().Where(p => p.IsPublished).OrderBy(p => p.Kind).ThenBy(p => p.Topic).ThenBy(p => p.SortOrder).ThenBy(p => p.Slug)
            .Select(p => new { p.Slug, p.Title, p.Topic, p.Kind, p.Content }).ToListAsync(ct);
        if (string.IsNullOrWhiteSpace(request.Q)) return all.Select(p => new CmsSummaryDto(p.Slug, p.Title, p.Topic, p.Kind)).ToList();
        var words = Slug.Fold(request.Q).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return all.Where(p =>
            {
                var text = Slug.Fold(p.Title + " " + System.Text.RegularExpressions.Regex.Replace(p.Content, "<[^>]+>", " "));
                return words.All(text.Contains);
            })
            .Select(p => new CmsSummaryDto(p.Slug, p.Title, p.Topic, p.Kind)).ToList();
    }
}

// =====================================================================================================================
// VI.8 message templates
// =====================================================================================================================

public record TemplateDefinition(string Key, TemplateChannel Channel, string Name, string? Subject, string Body, string Placeholders);

/// <summary>The messages whose text admins can edit, with their built-in text (also the fallback when a row is missing).</summary>
public static class TemplateCatalog
{
    public const string Otp = "OTP";
    public const string Notification = "NOTIFICATION";
    public const string OrderPlaceholders = "code,total,shop,note,deadline";

    /// <summary>In-app notification of an order event for one side: ORDER.{EVENT}.{BUYER|SHOP} ("PLACED_COD" for a COD order placed).</summary>
    public static string OrderKey(string orderEvent, string side) => $"ORDER.{orderEvent}.{side}";

    public static readonly IReadOnlyList<TemplateDefinition> All =
    [
        new(Otp, TemplateChannel.Sms, "Mã OTP qua SMS", null,
            "ShopHub: Ma {{code}} de {{action}}. Hieu luc {{minutes}} phut. KHONG chia se ma nay cho bat ky ai.", "code,action,minutes"),
        new(Otp, TemplateChannel.Email, "Mã OTP qua email", "Mã xác thực ShopHub: {{code}}",
            "<p>Mã xác thực để {{action}} của bạn là <strong>{{code}}</strong>.</p><p>Mã có hiệu lực trong {{minutes}} phút. Không chia sẻ mã này cho bất kỳ ai.</p>",
            "code,action,minutes"),
        new(Notification, TemplateChannel.Email, "Thư thông báo (đơn hàng, ví, khuyến mãi…)", "{{title}}",
            "<p>{{body}}</p><p><a href=\"{{link}}\">Xem chi tiết trên ShopHub</a></p><p style=\"color:#888;font-size:12px\">Bạn nhận thư này vì đã bật thông báo qua email. Tắt tại {{settings}}.</p>",
            "title,body,link,settings"),
        new(OrderKey("PLACED_COD", "BUYER"), TemplateChannel.InApp, "Thông báo đơn hàng: Người mua — đặt hàng COD thành công", "Đặt hàng thành công",
            "Đơn {{code}} ({{total}}) đã được đặt, đang chờ {{shop}} xác nhận.", OrderPlaceholders),
        new(OrderKey("PLACED", "BUYER"), TemplateChannel.InApp, "Thông báo đơn hàng: Người mua — đơn chờ thanh toán", "Đơn hàng chờ thanh toán",
            "Vui lòng thanh toán đơn {{code}} ({{total}}){{deadline}}, quá hạn đơn sẽ tự huỷ.", OrderPlaceholders),
        new(OrderKey("PAID", "BUYER"), TemplateChannel.InApp, "Thông báo đơn hàng: Người mua — thanh toán thành công", "Thanh toán thành công",
            "Đã nhận thanh toán {{total}} cho đơn {{code}}.", OrderPlaceholders),
        new(OrderKey("CONFIRMED", "BUYER"), TemplateChannel.InApp, "Thông báo đơn hàng: Người mua — shop xác nhận đơn", "Shop đã xác nhận đơn hàng",
            "{{shop}} đang chuẩn bị đơn {{code}}. Mã vận đơn: {{note}}.", OrderPlaceholders),
        new(OrderKey("SHIPPED", "BUYER"), TemplateChannel.InApp, "Thông báo đơn hàng: Người mua — đang giao", "Đơn hàng đang được giao",
            "Đơn {{code}} đã được giao cho đơn vị vận chuyển.", OrderPlaceholders),
        new(OrderKey("DELIVERED", "BUYER"), TemplateChannel.InApp, "Thông báo đơn hàng: Người mua — giao thành công", "Giao hàng thành công",
            "Đơn {{code}} đã được giao. Vui lòng kiểm tra và bấm \"Đã nhận được hàng\"; đơn sẽ tự hoàn thành sau vài ngày.", OrderPlaceholders),
        new(OrderKey("DELIVERY_FAILED", "BUYER"), TemplateChannel.InApp, "Thông báo đơn hàng: Người mua — giao không thành công", "Giao hàng không thành công",
            "Đơn {{code}}: {{note}}. Đơn vị vận chuyển sẽ liên hệ giao lại.", OrderPlaceholders),
        new(OrderKey("COMPLETED", "BUYER"), TemplateChannel.InApp, "Thông báo đơn hàng: Người mua — đơn hoàn thành", "Đơn hàng đã hoàn thành",
            "Cảm ơn bạn đã mua sắm tại {{shop}}. Hãy đánh giá sản phẩm của đơn {{code}}.", OrderPlaceholders),
        new(OrderKey("CANCELLED", "BUYER"), TemplateChannel.InApp, "Thông báo đơn hàng: Người mua — đơn bị huỷ", "Đơn hàng đã huỷ",
            "Đơn {{code}} đã huỷ. Lý do: {{note}}.", OrderPlaceholders),
        new(OrderKey("RETURNED", "BUYER"), TemplateChannel.InApp, "Thông báo đơn hàng: Người mua — đơn hoàn về shop", "Đơn hàng đã hoàn về shop",
            "Đơn {{code}} không giao được và đã hoàn về {{shop}}.", OrderPlaceholders),
        new(OrderKey("CANCEL_REJECTED", "BUYER"), TemplateChannel.InApp, "Thông báo đơn hàng: Người mua — shop từ chối huỷ", "Shop từ chối yêu cầu huỷ",
            "Đơn {{code}} vẫn được giao. Lý do: {{note}}.", OrderPlaceholders),
        new(OrderKey("REFUNDED", "BUYER"), TemplateChannel.InApp, "Thông báo đơn hàng: Người mua — đã hoàn tiền", "Đã hoàn tiền",
            "Đã hoàn {{total}} của đơn {{code}} về phương thức thanh toán ban đầu.", OrderPlaceholders),
        new(OrderKey("RETURN_UPDATED", "BUYER"), TemplateChannel.InApp, "Thông báo đơn hàng: Người mua — cập nhật trả hàng", "Cập nhật yêu cầu trả hàng",
            "Yêu cầu {{note}} của đơn {{code}} vừa được cập nhật.", OrderPlaceholders),
        new(OrderKey("RETURN_REFUNDED", "BUYER"), TemplateChannel.InApp, "Thông báo đơn hàng: Người mua — hoàn tiền trả hàng", "Hoàn tiền trả hàng",
            "Yêu cầu {{note}} của đơn {{code}} đã được hoàn tiền.", OrderPlaceholders),
        new(OrderKey("DISPUTE_DECIDED", "BUYER"), TemplateChannel.InApp, "Thông báo đơn hàng: Người mua — kết quả khiếu nại", "Kết quả khiếu nại",
            "Sàn đã phân xử khiếu nại của đơn {{code}}.", OrderPlaceholders),
        new(OrderKey("PLACED_COD", "SHOP"), TemplateChannel.InApp, "Thông báo đơn hàng: Shop — đơn COD mới", "Đơn hàng mới",
            "Đơn {{code}} ({{total}}, COD) đang chờ xác nhận.", OrderPlaceholders),
        new(OrderKey("PAID", "SHOP"), TemplateChannel.InApp, "Thông báo đơn hàng: Shop — đơn đã thanh toán", "Đơn hàng mới",
            "Đơn {{code}} ({{total}}) đã thanh toán, đang chờ xác nhận.", OrderPlaceholders),
        new(OrderKey("CANCEL_REQUESTED", "SHOP"), TemplateChannel.InApp, "Thông báo đơn hàng: Shop — người mua yêu cầu huỷ", "Yêu cầu huỷ đơn",
            "Người mua muốn huỷ đơn {{code}}. Lý do: {{note}}. Vui lòng phản hồi trong 24 giờ.", OrderPlaceholders),
        new(OrderKey("CANCELLED", "SHOP"), TemplateChannel.InApp, "Thông báo đơn hàng: Shop — đơn bị huỷ", "Đơn hàng đã huỷ",
            "Đơn {{code}} đã huỷ. Lý do: {{note}}.", OrderPlaceholders),
        new(OrderKey("COMPLETED", "SHOP"), TemplateChannel.InApp, "Thông báo đơn hàng: Shop — đơn hoàn thành", "Đơn hàng hoàn thành",
            "Đơn {{code}} ({{total}}) đã hoàn thành.", OrderPlaceholders),
        new(OrderKey("RETURNED", "SHOP"), TemplateChannel.InApp, "Thông báo đơn hàng: Shop — đơn hoàn về", "Đơn hàng hoàn về",
            "Đơn {{code}} giao không thành công đã hoàn về kho.", OrderPlaceholders),
        new(OrderKey("RETURN_REQUESTED", "SHOP"), TemplateChannel.InApp, "Thông báo đơn hàng: Shop — yêu cầu trả hàng mới", "Yêu cầu trả hàng mới",
            "Người mua gửi yêu cầu trả hàng {{note}} cho đơn {{code}}. Vui lòng phản hồi trong 2 ngày.", OrderPlaceholders),
        new(OrderKey("DISPUTE_OPENED", "SHOP"), TemplateChannel.InApp, "Thông báo đơn hàng: Shop — người mua khiếu nại", "Người mua khiếu nại",
            "Yêu cầu trả hàng {{note}} (đơn {{code}}) đã được chuyển lên sàn phân xử.", OrderPlaceholders),
        new(OrderKey("DISPUTE_DECIDED", "SHOP"), TemplateChannel.InApp, "Thông báo đơn hàng: Shop — kết quả khiếu nại", "Kết quả khiếu nại",
            "Sàn đã phân xử khiếu nại của đơn {{code}}.", OrderPlaceholders),
    ];
}

public sealed class MessageTemplates(IApplicationDbContext db)
{
    /// <summary>Subject + body for a message; placeholders are filled with the values (HTML-encoded for email bodies by the caller).</summary>
    public async Task<(string? Subject, string Body)> RenderAsync(string key, TemplateChannel channel, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        var row = await db.MessageTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Key == key && t.Channel == channel, ct);
        if (row is not null) return row.Render(values);
        var def = TemplateCatalog.All.Single(t => t.Key == key && t.Channel == channel);
        return new MessageTemplate(def.Key, def.Channel, def.Name, def.Subject, def.Body, def.Placeholders, DateTimeOffset.UtcNow).Render(values);
    }
}

public record MessageTemplateDto(Guid Id, string Key, TemplateChannel Channel, string Name, string? Subject, string Body, string Placeholders, DateTimeOffset UpdatedAt);

public record MessageTemplatesQuery : IRequest<IReadOnlyList<MessageTemplateDto>>;

public sealed class MessageTemplatesHandler(IApplicationDbContext db) : IRequestHandler<MessageTemplatesQuery, IReadOnlyList<MessageTemplateDto>>
{
    public async Task<IReadOnlyList<MessageTemplateDto>> Handle(MessageTemplatesQuery request, CancellationToken ct) =>
        await db.MessageTemplates.AsNoTracking().OrderBy(t => t.Key).ThenBy(t => t.Channel)
            .Select(t => new MessageTemplateDto(t.Id, t.Key, t.Channel, t.Name, t.Subject, t.Body, t.Placeholders, t.UpdatedAt)).ToListAsync(ct);
}

public record UpdateMessageTemplateCommand(Guid Id, string? Subject, string Body) : IRequest<Unit>;

public sealed class UpdateMessageTemplateHandler(IApplicationDbContext db, IHtmlSanitizer sanitizer, IClock clock) : IRequestHandler<UpdateMessageTemplateCommand, Unit>
{
    public async Task<Unit> Handle(UpdateMessageTemplateCommand request, CancellationToken ct)
    {
        var t = await db.MessageTemplates.FirstOrDefaultAsync(x => x.Id == request.Id, ct) ?? throw new NotFoundException("Không tìm thấy mẫu.");
        if (t.Channel == TemplateChannel.Sms && request.Body.Length > 320) throw new BusinessRuleException("Mẫu SMS tối đa 320 ký tự.");
        var body = t.Channel == TemplateChannel.Email ? sanitizer.Sanitize(request.Body) : request.Body;
        t.Update(request.Subject, body, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// =====================================================================================================================
// VI.9 audit export
// =====================================================================================================================

public record AuditLogsExportQuery(Guid? UserId, string? Action, string? Entity, string? EntityId, DateTimeOffset? From, DateTimeOffset? To) : IRequest<ReportFile>;

/// <summary>Nhật ký thao tác ra Excel with the same filters as the screen (capped at 5.000 newest rows).</summary>
public sealed class AuditLogsExportHandler(ISender sender, IReportDocuments documents) : IRequestHandler<AuditLogsExportQuery, ReportFile>
{
    public const int MaxRows = 5_000;

    public async Task<ReportFile> Handle(AuditLogsExportQuery q, CancellationToken ct)
    {
        if (q.From is { } f && q.To is { } t && f > t) throw new BusinessRuleException("Khoảng ngày không hợp lệ: ngày bắt đầu sau ngày kết thúc.");
        var rows = new List<AuditLogDto>();
        for (var page = 1; rows.Count < MaxRows; page++)
        {
            var batch = await sender.Send(new ListAuditLogsQuery(page, PagingLimits.MaxPageSize, q.UserId, q.Action, q.Entity, q.EntityId, q.From, q.To), ct);
            rows.AddRange(batch.Items);
            if (batch.Items.Count < PagingLimits.MaxPageSize) break;
        }
        var table = new ReportTable("Nhật ký thao tác", $"{Math.Min(rows.Count, MaxRows)} dòng mới nhất theo bộ lọc",
            [new("Thời điểm", ReportCellKind.Text), new("Người làm", ReportCellKind.Text), new("IP", ReportCellKind.Text), new("Hành động", ReportCellKind.Text),
                new("Đối tượng", ReportCellKind.Text), new("Mã", ReportCellKind.Text), new("Giá trị cũ", ReportCellKind.Text), new("Giá trị mới", ReportCellKind.Text)],
            rows.Take(MaxRows).Select(r => (IReadOnlyList<object>)[VietnamTime.Format(r.OccurredAt), r.UserName ?? "Hệ thống", r.Ip ?? "", r.Action, r.Entity,
                r.EntityId ?? "", Trim(r.OldValue), Trim(r.NewValue)]).ToList());
        return ReportFiles.Of(table, ExportFormat.Xlsx, $"nhat-ky-thao-tac-{DateTime.UtcNow:yyyyMMddHHmm}", documents);
    }

    // An Excel cell holds 32.767 characters
    private static string Trim(string? v) => v is null ? "" : v.Length > 32_000 ? v[..32_000] + "…" : v;
}

// =====================================================================================================================
// VI.8 carriers & payment gateways switches
// =====================================================================================================================

public record AdminCarrierDto(Guid Id, string Code, string Name, string Provider, bool ProviderConfigured, bool IsActive, bool SupportsCod, bool SameProvinceOnly,
    int DaysSameProvince, int DaysSameRegion, int DaysCrossRegion);

public record AdminGatewayDto(PaymentMethod Method, string Name, string Provider, bool Enabled);

public record ProvidersDto(IReadOnlyList<AdminCarrierDto> Carriers, IReadOnlyList<AdminGatewayDto> Gateways);

public record ProvidersQuery : IRequest<ProvidersDto>;

/// <summary>"Configured" = its keys are set in .env (spec Phase 11); an admin can switch a configured one off, never a missing one on.</summary>
public sealed class ProvidersHandler(IApplicationDbContext db, IEnumerable<ICarrier> carriers, IPaymentGatewayRegistry gateways) : IRequestHandler<ProvidersQuery, ProvidersDto>
{
    public async Task<ProvidersDto> Handle(ProvidersQuery request, CancellationToken ct)
    {
        var providers = carriers.Select(c => c.Provider).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = await db.Carriers.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Code).ToListAsync(ct);
        var enabled = (await gateways.EnabledAsync(ct)).Select(g => g.Method).ToHashSet();
        return new ProvidersDto(
            rows.Select(c => new AdminCarrierDto(c.Id, c.Code, c.Name, c.Provider, providers.Contains(c.Provider), c.IsActive, c.SupportsCod, c.SameProvinceOnly,
                c.DaysSameProvince, c.DaysSameRegion, c.DaysCrossRegion)).ToList(),
            gateways.Online.Select(g => new AdminGatewayDto(g.Method, g.DisplayName, g.Provider, enabled.Contains(g.Method))).ToList());
    }
}

public record UpdateCarrierCommand(Guid Id, string Name, string? Description, bool IsActive, bool SupportsCod, int DaysSameProvince, int DaysSameRegion,
    int DaysCrossRegion) : IRequest<Unit>;

public sealed class UpdateCarrierValidator : AbstractValidator<UpdateCarrierCommand>
{
    public UpdateCarrierValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => new[] { x.DaysSameProvince, x.DaysSameRegion, x.DaysCrossRegion }).Must(d => d.All(v => v is >= 0 and <= 30))
            .WithMessage("Số ngày giao từ 0 đến 30.");
    }
}

public sealed class UpdateCarrierHandler(IApplicationDbContext db) : IRequestHandler<UpdateCarrierCommand, Unit>
{
    public async Task<Unit> Handle(UpdateCarrierCommand request, CancellationToken ct)
    {
        var c = await db.Carriers.FirstOrDefaultAsync(x => x.Id == request.Id, ct) ?? throw new NotFoundException("Không tìm thấy đơn vị vận chuyển.");
        c.Configure(request.Name.Trim(), request.Description, request.IsActive, request.SupportsCod, c.SameProvinceOnly,
            request.DaysSameProvince, request.DaysSameRegion, request.DaysCrossRegion);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public record SetGatewayEnabledCommand(PaymentMethod Method, bool Enabled) : IRequest<Unit>;

/// <summary>Writes PAYMENT.DISABLED_METHODS through the parameter command, so the change is audited and every instance reloads it.</summary>
public sealed class SetGatewayEnabledHandler(ISender sender, ISystemParameters parameters, IPaymentGatewayRegistry gateways) : IRequestHandler<SetGatewayEnabledCommand, Unit>
{
    public async Task<Unit> Handle(SetGatewayEnabledCommand request, CancellationToken ct)
    {
        if (!request.Method.IsOnline()) throw new BusinessRuleException("Chỉ bật / tắt được cổng thanh toán online.");
        if (!gateways.Online.Any(g => g.Method == request.Method)) throw new ConflictException("Cổng này chưa được cấu hình khoá nên không bật được.", "NOT_CONFIGURED");
        var raw = await parameters.GetStringAsync(ParameterKeys.PaymentDisabledMethods, ct);
        var off = (System.Text.Json.JsonSerializer.Deserialize<List<string>>(raw) ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (request.Enabled) off.Remove(request.Method.ToString());
        else off.Add(request.Method.ToString());
        await sender.Send(new Features.SystemConfig.UpdateSystemParameterCommand(ParameterKeys.PaymentDisabledMethods,
            System.Text.Json.JsonSerializer.Serialize(off.Order().ToList()), null), ct);
        return Unit.Value;
    }
}

// =====================================================================================================================
// II.12 site identity for the footer (legal entity from parameters, never hard-coded)
// =====================================================================================================================

public record SiteInfoDto(string PlatformName, string Hotline, string SupportEmail, string LegalName, string LegalAddress, string TaxCode, string BusinessLicense);

public record SiteInfoQuery : IRequest<SiteInfoDto>;

public sealed class SiteInfoHandler(ISystemParameters parameters) : IRequestHandler<SiteInfoQuery, SiteInfoDto>
{
    public async Task<SiteInfoDto> Handle(SiteInfoQuery request, CancellationToken ct) => new(
        await parameters.GetStringAsync(ParameterKeys.SitePlatformName, ct),
        await parameters.GetStringAsync(ParameterKeys.SiteHotline, ct),
        await parameters.GetStringAsync(ParameterKeys.SiteSupportEmail, ct),
        await parameters.GetStringAsync(ParameterKeys.SiteLegalName, ct),
        await parameters.GetStringAsync(ParameterKeys.SiteLegalAddress, ct),
        await parameters.GetStringAsync(ParameterKeys.SiteTaxCode, ct),
        await parameters.GetStringAsync(ParameterKeys.SiteBusinessLicense, ct));
}
