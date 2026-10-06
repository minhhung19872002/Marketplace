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

    public const string ProductReviewOnEdit = "PRODUCT.REVIEW_ON_EDIT";
    public const string ProductBannedKeywords = "PRODUCT.BANNED_KEYWORDS";
    public const string ShopLowStockThreshold = "SHOP.LOW_STOCK_THRESHOLD";
    public const string MediaMaxImageMb = "MEDIA.MAX_IMAGE_MB";
    public const string MediaMaxVideoMb = "MEDIA.MAX_VIDEO_MB";
    public const string MediaMaxVideoSeconds = "MEDIA.MAX_VIDEO_SECONDS";

    public const string SearchSynonyms = "SEARCH.SYNONYMS";
    public const string SearchHotKeywords = "SEARCH.HOT_KEYWORDS";
    public const string SearchHotKeywordDays = "SEARCH.HOT_KEYWORD_DAYS";
    public const string ProductViewDedupeMinutes = "PRODUCT.VIEW_DEDUPE_MINUTES";
    public const string JobCounterRecomputeCron = "JOB.COUNTER_RECOMPUTE_CRON";

    public const string CartMaxLines = "CART.MAX_LINES";
    public const string PaymentTimeoutMinutes = "PAYMENT.TIMEOUT_MINUTES";
    public const string PaymentCodMaxAmount = "PAYMENT.COD_MAX_AMOUNT";
    public const string CoinMaxPercentBp = "COIN.MAX_PERCENT_BP";
    public const string CoinExpiryDays = "COIN.EXPIRY_DAYS";
    public const string LogisticsHolidays = "LOGISTICS.HOLIDAYS";
    public const string JobPaymentExpiryCron = "JOB.PAYMENT_EXPIRY_CRON";

    public const string OrderAutoCompleteDays = "ORDER.AUTO_COMPLETE_DAYS";
    public const string OrderCancelRequestHours = "ORDER.CANCEL_REQUEST_HOURS";
    public const string OrderShipDeadlineDays = "ORDER.SHIP_DEADLINE_DAYS";
    public const string OrderPickupSlots = "ORDER.PICKUP_SLOTS";
    public const string LogisticsSimStepSeconds = "LOGISTICS.SIM_STEP_SECONDS";
    public const string LogisticsSimFailPercent = "LOGISTICS.SIM_FAIL_PERCENT";
    public const string JobOrderAutomationCron = "JOB.ORDER_AUTOMATION_CRON";
    public const string JobCarrierSimulatorCron = "JOB.CARRIER_SIMULATOR_CRON";
}

public static class ParameterGroups
{
    public const string Site = "SITE";
    public const string Job = "JOB";
    public const string Auth = "AUTH";
    public const string Account = "ACCOUNT";
    public const string Product = "PRODUCT";
    public const string Shop = "SHOP";
    public const string Media = "MEDIA";
    public const string Search = "SEARCH";
    public const string Cart = "CART";
    public const string Payment = "PAYMENT";
    public const string Coin = "COIN";
    public const string Logistics = "LOGISTICS";
    public const string Order = "ORDER";
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

        new(ParameterKeys.ProductReviewOnEdit, "true", ParameterDataType.Bool, ParameterGroups.Product,
            "Duyệt lại khi sửa", "Sửa tên, ảnh hoặc danh mục của sản phẩm đã duyệt thì phải duyệt lại."),
        new(ParameterKeys.ProductBannedKeywords, "[\"hàng giả\",\"hàng fake\",\"replica\",\"super fake\",\"vũ khí\",\"ma túy\"]",
            ParameterDataType.Json, ParameterGroups.Product,
            "Từ khoá cấm", "Sản phẩm có từ khoá này (không phân biệt dấu) bị gắn cờ khi gửi duyệt."),
        new(ParameterKeys.ShopLowStockThreshold, "10", ParameterDataType.Int, ParameterGroups.Shop,
            "Ngưỡng sắp hết hàng", "Tồn kho khả dụng từ ngưỡng này trở xuống được cảnh báo sắp hết hàng."),
        new(ParameterKeys.MediaMaxImageMb, "5", ParameterDataType.Int, ParameterGroups.Media,
            "Dung lượng ảnh tối đa (MB)", "Ảnh tải lên lớn hơn bị từ chối (ảnh đại diện: 1 MB)."),
        new(ParameterKeys.MediaMaxVideoMb, "30", ParameterDataType.Int, ParameterGroups.Media,
            "Dung lượng video tối đa (MB)", "Video sản phẩm lớn hơn bị từ chối."),
        new(ParameterKeys.MediaMaxVideoSeconds, "30", ParameterDataType.Int, ParameterGroups.Media,
            "Độ dài video tối đa (giây)", "Video sản phẩm dài hơn bị từ chối."),

        new(ParameterKeys.SearchSynonyms,
            "{\"dt\":[\"dien thoai\"],\"dien thoai\":[\"dt\",\"smartphone\"],\"laptop\":[\"may tinh xach tay\"],\"tai nghe\":[\"headphone\",\"earphone\"],\"ao khoac\":[\"hoodie\"]}",
            ParameterDataType.Json, ParameterGroups.Search, "Từ đồng nghĩa",
            "Từ (không dấu) → các từ tương đương khi tìm kiếm. Ví dụ \"dt\" = \"dien thoai\"."),
        new(ParameterKeys.SearchHotKeywords, "[\"Áo thun\",\"Điện thoại\",\"Tai nghe\",\"Giày sneaker\",\"Nồi chiên không dầu\"]",
            ParameterDataType.Json, ParameterGroups.Search, "Từ khoá hot thủ công",
            "Dùng khi nhật ký tìm kiếm chưa đủ dữ liệu; từ khoá thật lấy từ lượt tìm 7 ngày gần nhất."),
        new(ParameterKeys.SearchHotKeywordDays, "7", ParameterDataType.Int, ParameterGroups.Search,
            "Số ngày tính từ khoá hot", "Khoảng thời gian (ngày) để xếp hạng từ khoá được tìm nhiều."),
        new(ParameterKeys.ProductViewDedupeMinutes, "30", ParameterDataType.Int, ParameterGroups.Product,
            "Chống đếm trùng lượt xem (phút)", "Cùng người xem cùng sản phẩm trong khoảng này chỉ tính một lượt."),
        new(ParameterKeys.JobCounterRecomputeCron, "30 18 * * *", ParameterDataType.Cron, ParameterGroups.Job,
            "Lịch tính lại chỉ số", "Cron tính lại lượt thích, theo dõi, số sản phẩm, lượt xem từ dữ liệu gốc (01:30 giờ VN)."),

        new(ParameterKeys.CartMaxLines, "100", ParameterDataType.Int, ParameterGroups.Cart,
            "Số dòng tối đa trong giỏ", "Giỏ hàng có nhiều nhất bấy nhiêu sản phẩm (phân loại) khác nhau."),
        new(ParameterKeys.PaymentTimeoutMinutes, "15", ParameterDataType.Int, ParameterGroups.Payment,
            "Hạn thanh toán online (phút)", "Quá thời gian này chưa trả tiền thì đơn tự huỷ, hàng giữ và voucher, xu được trả lại."),
        new(ParameterKeys.PaymentCodMaxAmount, "20000000", ParameterDataType.Int, ParameterGroups.Payment,
            "Ngưỡng tối đa COD (₫)", "Đơn có tổng thanh toán lớn hơn ngưỡng này không được chọn thanh toán khi nhận hàng."),
        new(ParameterKeys.CoinMaxPercentBp, "5000", ParameterDataType.Int, ParameterGroups.Coin,
            "Xu dùng tối đa (phần vạn)", "Xu trừ tối đa bấy nhiêu phần vạn giá trị hàng sau giảm giá (5000 = 50%). 1 xu = ₫1."),
        new(ParameterKeys.CoinExpiryDays, "180", ParameterDataType.Int, ParameterGroups.Coin,
            "Hạn dùng xu (ngày)", "Xu được cộng có hạn dùng bấy nhiêu ngày."),
        new(ParameterKeys.LogisticsHolidays, "[\"2026-01-01\",\"2026-02-16\",\"2026-02-17\",\"2026-02-18\",\"2026-02-19\",\"2026-02-20\",\"2026-04-26\",\"2026-04-30\",\"2026-05-01\",\"2026-09-02\"]",
            ParameterDataType.Json, ParameterGroups.Logistics,
            "Ngày nghỉ lễ", "Ngày (yyyy-MM-dd, giờ Việt Nam) không tính vào thời gian giao dự kiến; Chủ nhật luôn được bỏ qua."),
        new(ParameterKeys.JobPaymentExpiryCron, "* * * * *", ParameterDataType.Cron, ParameterGroups.Job,
            "Lịch xử lý đơn quá hạn thanh toán", "Cron đối chiếu giao dịch treo với cổng và huỷ đơn quá hạn, nhả kho."),

        new(ParameterKeys.OrderAutoCompleteDays, "3", ParameterDataType.Int, ParameterGroups.Order,
            "Tự hoàn thành sau khi giao (ngày)", "Người mua không bấm \"Đã nhận được hàng\" thì đơn tự hoàn thành sau số ngày này kể từ lúc giao thành công."),
        new(ParameterKeys.OrderCancelRequestHours, "24", ParameterDataType.Int, ParameterGroups.Order,
            "Hạn shop xử lý yêu cầu huỷ (giờ)", "Shop không chấp thuận / từ chối trong thời gian này thì yêu cầu huỷ được tự chấp thuận."),
        new(ParameterKeys.OrderShipDeadlineDays, "2", ParameterDataType.Int, ParameterGroups.Order,
            "Hạn chuẩn bị hàng (ngày làm việc)", "Shop không xác nhận / giao cho đơn vị vận chuyển trong hạn này thì đơn tự huỷ và shop bị ghi 1 điểm phạt."),
        new(ParameterKeys.OrderPickupSlots, "[\"08:00 - 12:00\",\"13:00 - 17:00\",\"18:00 - 21:00\"]", ParameterDataType.Json, ParameterGroups.Order,
            "Khung giờ lấy hàng", "Các khung giờ shop chọn khi hẹn đơn vị vận chuyển đến lấy hàng."),
        new(ParameterKeys.LogisticsSimStepSeconds, "120", ParameterDataType.Int, ParameterGroups.Logistics,
            "Hãng giả lập: giây mỗi bước", "Đơn vị vận chuyển giả lập chuyển sang trạng thái kế tiếp sau bấy nhiêu giây (đã lấy → trung chuyển → đang giao → đã giao)."),
        new(ParameterKeys.LogisticsSimFailPercent, "0", ParameterDataType.Int, ParameterGroups.Logistics,
            "Hãng giả lập: tỉ lệ giao thất bại (%)", "Phần trăm kiện giao thất bại rồi hoàn về (để thử luồng hoàn hàng)."),
        new(ParameterKeys.JobOrderAutomationCron, "*/5 * * * *", ParameterDataType.Cron, ParameterGroups.Job,
            "Lịch tự động hoá đơn hàng", "Cron tự hoàn thành đơn đã giao, tự chấp thuận yêu cầu huỷ quá hạn, tự huỷ đơn shop chậm chuẩn bị."),
        new(ParameterKeys.JobCarrierSimulatorCron, "* * * * *", ParameterDataType.Cron, ParameterGroups.Job,
            "Lịch hãng vận chuyển giả lập", "Cron đẩy trạng thái các vận đơn của đơn vị vận chuyển giả lập."),
    ];
}
