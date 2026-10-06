using ShopHub.Domain.SystemConfig;

namespace ShopHub.Application.SystemConfig;

// Every key seeded here MUST be read somewhere in code (SystemParameterReadersTests) — a stored switch nobody
// reads is a dead switch.
public static class ParameterKeys
{
    public const string SitePlatformName = "SITE.PLATFORM_NAME";
    public const string SiteHotline = "SITE.HOTLINE";
    public const string SiteSupportEmail = "SITE.SUPPORT_EMAIL";
    public const string SiteLegalName = "SITE.LEGAL_NAME";
    public const string SiteLegalAddress = "SITE.LEGAL_ADDRESS";
    public const string SiteTaxCode = "SITE.TAX_CODE";
    public const string SiteBusinessLicense = "SITE.BUSINESS_LICENSE";

    public const string JobOutboxDispatchCron = "JOB.OUTBOX_DISPATCH_CRON";
    public const string JobOutboxBatchSize = "JOB.OUTBOX_BATCH_SIZE";
    public const string JobOutboxMaxAttempts = "JOB.OUTBOX_MAX_ATTEMPTS";
    public const string JobOutboxCleanupCron = "JOB.OUTBOX_CLEANUP_CRON";
    public const string JobOutboxRetentionDays = "JOB.OUTBOX_RETENTION_DAYS";
}

public static class ParameterGroups
{
    public const string Site = "SITE";
    public const string Job = "JOB";
}

public record ParameterDefinition(
    string Key, string DefaultValue, ParameterDataType DataType, string Group, string Name, string Description);

// Default values inserted by the seeder when a key is missing (existing values are never overwritten)
public static class ParameterCatalog
{
    public static readonly IReadOnlyList<ParameterDefinition> All =
    [
        new(ParameterKeys.SitePlatformName, "ShopHub", ParameterDataType.String, ParameterGroups.Site,
            "Tên sàn", "Tên hiển thị của sàn trên mọi trang và thư."),
        new(ParameterKeys.SiteHotline, "1900 6000", ParameterDataType.String, ParameterGroups.Site,
            "Hotline", "Số điện thoại chăm sóc khách hàng (dữ liệu mẫu)."),
        new(ParameterKeys.SiteSupportEmail, "hotro@shophub.local", ParameterDataType.String, ParameterGroups.Site,
            "Thư hỗ trợ", "Địa chỉ thư nhận yêu cầu hỗ trợ (dữ liệu mẫu)."),
        new(ParameterKeys.SiteLegalName, "Công ty TNHH ShopHub (dữ liệu mẫu)", ParameterDataType.String, ParameterGroups.Site,
            "Tên pháp nhân", "Tên doanh nghiệp vận hành sàn, hiện ở chân trang."),
        new(ParameterKeys.SiteLegalAddress, "Tầng 1, Toà nhà Mẫu, Quận 1, TP. Hồ Chí Minh", ParameterDataType.String,
            ParameterGroups.Site, "Địa chỉ pháp nhân", "Địa chỉ trụ sở, hiện ở chân trang."),
        new(ParameterKeys.SiteTaxCode, "0000000000", ParameterDataType.String, ParameterGroups.Site,
            "Mã số thuế", "Mã số doanh nghiệp (dữ liệu mẫu)."),
        new(ParameterKeys.SiteBusinessLicense, "GCN ĐKDN số 0000000000 do Sở KH&ĐT TP.HCM cấp (dữ liệu mẫu)",
            ParameterDataType.String, ParameterGroups.Site, "Giấy phép kinh doanh", "Thông tin đăng ký kinh doanh ở chân trang."),

        new(ParameterKeys.JobOutboxDispatchCron, "* * * * *", ParameterDataType.Cron, ParameterGroups.Job,
            "Lịch gửi outbox", "Cron chạy việc gửi các tin outbox đang chờ (giờ UTC)."),
        new(ParameterKeys.JobOutboxBatchSize, "100", ParameterDataType.Int, ParameterGroups.Job,
            "Số tin outbox mỗi lượt", "Số tin tối đa lấy ra trong một lượt gửi."),
        new(ParameterKeys.JobOutboxMaxAttempts, "10", ParameterDataType.Int, ParameterGroups.Job,
            "Số lần thử tối đa", "Quá số lần này tin outbox bị bỏ qua và cần xử lý tay."),
        new(ParameterKeys.JobOutboxCleanupCron, "0 19 * * *", ParameterDataType.Cron, ParameterGroups.Job,
            "Lịch dọn outbox", "Cron dọn tin outbox đã gửi (mặc định 02:00 giờ Việt Nam = 19:00 UTC)."),
        new(ParameterKeys.JobOutboxRetentionDays, "14", ParameterDataType.Int, ParameterGroups.Job,
            "Số ngày giữ outbox đã gửi", "Tin đã gửi cũ hơn số ngày này sẽ bị xoá."),
    ];
}
