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

    public const string AuthAccessTokenMinutes = "AUTH.ACCESS_TOKEN_MINUTES";
    public const string AuthRefreshTokenDays = "AUTH.REFRESH_TOKEN_DAYS";
    public const string AuthMaxFailedLogin = "AUTH.MAX_FAILED_LOGIN";
    public const string AuthLockoutMinutes = "AUTH.LOCKOUT_MINUTES";
    public const string AuthOtpTtlSeconds = "AUTH.OTP_TTL_SECONDS";
    public const string AuthOtpMaxAttempts = "AUTH.OTP_MAX_ATTEMPTS";
    public const string AuthOtpResendSeconds = "AUTH.OTP_RESEND_SECONDS";
    public const string AuthOtpMaxPerHour = "AUTH.OTP_MAX_PER_HOUR";

    public const string AccountMaxAddresses = "ACCOUNT.MAX_ADDRESSES";
}

public static class ParameterGroups
{
    public const string Site = "SITE";
    public const string Job = "JOB";
    public const string Auth = "AUTH";
    public const string Account = "ACCOUNT";
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

        new(ParameterKeys.AuthAccessTokenMinutes, "15", ParameterDataType.Int, ParameterGroups.Auth,
            "Hạn access token (phút)", "Thời gian sống của access token."),
        new(ParameterKeys.AuthRefreshTokenDays, "30", ParameterDataType.Int, ParameterGroups.Auth,
            "Hạn refresh token (ngày)", "Sau số ngày này không dùng, phiên đăng nhập hết hạn."),
        new(ParameterKeys.AuthMaxFailedLogin, "5", ParameterDataType.Int, ParameterGroups.Auth,
            "Số lần đăng nhập sai tối đa", "Sai liên tiếp số lần này thì tài khoản bị khoá tạm."),
        new(ParameterKeys.AuthLockoutMinutes, "15", ParameterDataType.Int, ParameterGroups.Auth,
            "Thời gian khoá tạm (phút)", "Thời gian khoá sau khi đăng nhập sai quá số lần cho phép."),
        new(ParameterKeys.AuthOtpTtlSeconds, "300", ParameterDataType.Int, ParameterGroups.Auth,
            "Hạn mã OTP (giây)", "Mã OTP hết hiệu lực sau số giây này."),
        new(ParameterKeys.AuthOtpMaxAttempts, "5", ParameterDataType.Int, ParameterGroups.Auth,
            "Số lần nhập OTP sai tối đa", "Sai quá số lần này mã bị vô hiệu, phải gửi mã mới."),
        new(ParameterKeys.AuthOtpResendSeconds, "60", ParameterDataType.Int, ParameterGroups.Auth,
            "Chờ gửi lại OTP (giây)", "Khoảng cách tối thiểu giữa hai lần gửi mã cho cùng một đích."),
        new(ParameterKeys.AuthOtpMaxPerHour, "5", ParameterDataType.Int, ParameterGroups.Auth,
            "Số OTP tối đa mỗi giờ", "Số mã tối đa gửi tới một số điện thoại/email trong 60 phút."),

        new(ParameterKeys.AccountMaxAddresses, "10", ParameterDataType.Int, ParameterGroups.Account,
            "Số địa chỉ tối đa", "Số địa chỉ nhận hàng tối đa của một tài khoản."),
    ];
}
