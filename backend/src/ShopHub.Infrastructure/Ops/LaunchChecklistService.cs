using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Admin;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Sales;
using ShopHub.Domain.SystemConfig;
using ShopHub.Infrastructure.Commerce;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Ops;

/// <summary>The launch checklist measured on the running system (G4-D, docs/09).</summary>
public sealed class LaunchChecklistService(
    ShopHubDbContext db,
    ISystemParameters parameters,
    ShopHubSettings settings,
    IEnumerable<ICarrier> carriers,
    IPaymentGatewayRegistry gateways,
    IClock clock) : ILaunchChecklist
{
    // Sample accounts of the seed (buyers 09000000xx, shop owners 09000001xx, staff 09000002xx)
    private const string SamplePhonePrefix = "0900000";
    private static readonly string[] LegalPages = ["dieu-khoan-su-dung", "quy-che-hoat-dong", "chinh-sach-bao-mat"];

    public async Task<LaunchChecklistDto> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var items = new List<LaunchCheckDto>();
        void Add(string id, string group, string title, bool ok, string detail, bool blocking = true, bool warnOnly = false) =>
            items.Add(new LaunchCheckDto(id, group, title, ok ? CheckStatus.Pass : warnOnly ? CheckStatus.Warn : CheckStatus.Fail, detail, blocking && !warnOnly));
        async Task<string> P(string key) => (await parameters.GetStringAsync(key, ct)).Trim();

        // --- Chế độ & địa chỉ
        var demo = await SiteMode.IsDemoAsync(parameters, ct);
        Add("site-mode", "Chế độ", "Chế độ chạy là live (SITE.MODE)", !demo,
            demo ? "Đang là demo: còn băng \"bản trình diễn\" và cổng thanh toán giả lập." : "live");
        var url = await P(ParameterKeys.SitePublicUrl);
        var https = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                    && uri.Host is not ("localhost" or "127.0.0.1") && !uri.Host.EndsWith(".example.vn", StringComparison.OrdinalIgnoreCase);
        Add("public-url", "Chế độ", "Địa chỉ trang người mua là HTTPS thật (SITE.PUBLIC_URL)", https, url.Length == 0 ? "Chưa đặt." : url);

        // --- Pháp lý (Nghị định 52/2013, 85/2021: thông tin pháp nhân + thông báo Bộ Công Thương)
        string[] legal = [await P(ParameterKeys.SiteLegalName), await P(ParameterKeys.SiteLegalAddress), await P(ParameterKeys.SiteTaxCode),
            await P(ParameterKeys.SiteBusinessLicense)];
        var sample = legal.Any(v => v.Length == 0 || v.Contains("mẫu", StringComparison.OrdinalIgnoreCase) || v.Contains("0000000000", StringComparison.Ordinal));
        Add("legal", "Pháp lý", "Thông tin pháp nhân thật (tên, địa chỉ, mã số thuế, giấy phép)", !sample,
            sample ? "Còn giá trị mẫu hoặc trống ở SITE.LEGAL_* / SITE.TAX_CODE / SITE.BUSINESS_LICENSE." : legal[0]);
        var moit = await P(ParameterKeys.SiteMoitUrl);
        Add("moit", "Pháp lý", "Đã thông báo / đăng ký sàn với Bộ Công Thương (SITE.MOIT_URL)",
            moit.StartsWith("https://", StringComparison.OrdinalIgnoreCase), moit.Length == 0 ? "Chưa có đường dẫn online.gov.vn." : moit);
        var pages = await db.CmsPages.AsNoTracking().Where(p => LegalPages.Contains(p.Slug)).Select(p => p.Slug).ToListAsync(ct);
        Add("legal-pages", "Pháp lý", "Đủ trang Điều khoản, Quy chế hoạt động, Chính sách bảo mật", pages.Count == LegalPages.Length,
            pages.Count == LegalPages.Length ? "Có đủ — nhờ luật sư rà nội dung trước khi mở bán." : $"Thiếu: {string.Join(", ", LegalPages.Except(pages))}");

        // --- Thanh toán & vận chuyển
        var realGateways = (await gateways.EnabledAsync(ct)).Where(g => g.Method != PaymentMethod.Simulated).Select(g => g.DisplayName).ToList();
        Add("payment", "Thanh toán & vận chuyển", "Có cổng thanh toán online thật (VNPay / MoMo / ZaloPay)", realGateways.Count > 0,
            realGateways.Count > 0 ? string.Join(", ", realGateways) : "Chỉ có COD / ví — vẫn bán được, nhưng không nhận trả trước.", warnOnly: true);
        var configured = carriers.Select(c => c.Provider).Where(p => !string.Equals(p, SimulatedCarrier.ProviderName, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var realCarriers = await db.Carriers.AsNoTracking().Where(c => c.IsActive).Select(c => new { c.Name, c.Provider }).ToListAsync(ct);
        var live = realCarriers.Where(c => configured.Contains(c.Provider)).Select(c => c.Name).ToList();
        var simActive = realCarriers.Any(c => string.Equals(c.Provider, SimulatedCarrier.ProviderName, StringComparison.OrdinalIgnoreCase));
        Add("carrier", "Thanh toán & vận chuyển", "Có hãng vận chuyển thật đang bật (GHN / GHTK)", live.Count > 0,
            live.Count > 0 ? string.Join(", ", live) : "Chỉ có hãng giả lập — đơn sẽ \"tự giao\" mà không có kiện hàng thật.");
        Add("carrier-sim", "Thanh toán & vận chuyển", "Đã tắt các kênh vận chuyển giả lập", !simActive,
            simActive ? "Quản trị → Vận chuyển & cổng thanh toán: tắt kênh giả lập." : "Đã tắt.");

        // --- Thông báo
        Add("sms", "Thông báo", "Gửi SMS thật cho mã OTP (SH_SMS_PROVIDER)", !string.Equals(settings.SmsProvider, "simulated", StringComparison.OrdinalIgnoreCase),
            settings.SmsProvider);
        var smtpDev = settings.SmtpHost is "localhost" or "127.0.0.1" or "mailpit";
        Add("smtp", "Thông báo", "Gửi email qua máy chủ SMTP thật (SH_SMTP_HOST)", !smtpDev, smtpDev ? $"{settings.SmtpHost} (hộp thư thử)" : settings.SmtpHost);

        // --- Tài khoản & dữ liệu
        var admin = await db.Users.AsNoTracking().Where(u => u.Username == "admin").Select(u => new { u.MustChangePassword }).FirstOrDefaultAsync(ct);
        Add("admin-password", "Tài khoản & dữ liệu", "Đã đổi mật khẩu tài khoản admin ban đầu", admin is { MustChangePassword: false },
            admin is null ? "Không thấy tài khoản admin." : admin.MustChangePassword ? "Chưa đổi (mật khẩu in ra log lần đầu)." : "Đã đổi.");
        var sampleUsers = await db.Users.AsNoTracking().CountAsync(u => u.Phone != null && u.Phone.StartsWith(SamplePhonePrefix), ct);
        Add("sample-data", "Tài khoản & dữ liệu", "Không còn tài khoản / shop / đơn mẫu", sampleUsers == 0,
            sampleUsers == 0 ? "Sạch." : $"{sampleUsers} tài khoản mẫu ({SamplePhonePrefix}…) — cài lại không có SH_SEED_SAMPLE.");
        var fees = await db.FeeRules.AsNoTracking().AnyAsync(f => f.ValidFrom <= now && (f.ValidTo == null || f.ValidTo > now), ct);
        Add("fees", "Tài khoản & dữ liệu", "Có biểu phí sàn đang hiệu lực", fees, fees ? "Có." : "Quản trị → Tài chính → Biểu phí.");

        // --- Vận hành
        var lastBackup = await db.BackupRuns.AsNoTracking().Where(b => b.Status == BackupStatus.Succeeded)
            .OrderByDescending(b => b.StartedAt).Select(b => (DateTimeOffset?)b.StartedAt).FirstOrDefaultAsync(ct);
        Add("backup", "Vận hành", "Sao lưu tự động thành công trong 26 giờ qua", lastBackup is { } at && now - at < TimeSpan.FromHours(26),
            lastBackup is null ? "Chưa có bản sao lưu thành công nào." : $"Lần gần nhất: {Application.Common.VietnamTime.Format(lastBackup.Value)}");

        var blocking = items.Where(i => i.Blocking).ToList();
        return new LaunchChecklistDto(blocking.All(i => i.Status == CheckStatus.Pass), items.Count(i => i.Status == CheckStatus.Pass), items.Count, items, now);
    }
}
