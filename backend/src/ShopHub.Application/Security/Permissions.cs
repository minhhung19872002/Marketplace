namespace ShopHub.Application.Security;

// Platform admin permission tree: MODULE.ENTITY.ACTION
public static class Permissions
{
    // Super admin wildcard
    public const string All = "*";

    public const string SystemParameterView = "SYS.PARAMETER.VIEW";
    public const string SystemParameterUpdate = "SYS.PARAMETER.UPDATE";
    public const string AuditLogView = "SYS.AUDIT.VIEW";
    public const string JobDashboardView = "SYS.JOB.VIEW";
    public const string SearchReindex = "SYS.SEARCH.REINDEX";
    public const string JobRun = "SYS.JOB.RUN";

    public const string UserView = "IAM.USER.VIEW";
    public const string UserLock = "IAM.USER.LOCK";
    public const string UserAssignRole = "IAM.USER.ASSIGN_ROLE";
    public const string RoleView = "IAM.ROLE.VIEW";
    public const string RoleManage = "IAM.ROLE.MANAGE";

    public const string CategoryManage = "CATALOG.CATEGORY.MANAGE";
    public const string BrandManage = "CATALOG.BRAND.MANAGE";
    public const string ProductReview = "CATALOG.PRODUCT.REVIEW";
    public const string ProductBan = "CATALOG.PRODUCT.BAN";
    public const string ShopView = "SHOP.SHOP.VIEW";
    public const string ShopReview = "SHOP.SHOP.REVIEW";
    public const string ShopLock = "SHOP.SHOP.LOCK";
    public const string ShopLabel = "SHOP.SHOP.LABEL";

    public const string VoucherManage = "PROMO.VOUCHER.MANAGE";
    public const string CoinGrant = "PROMO.COIN.GRANT";
    public const string OrderView = "SALES.ORDER.VIEW";
    public const string DisputeResolve = "SALES.DISPUTE.RESOLVE";
    public const string ReviewModerate = "CATALOG.REVIEW.MODERATE";

    public const string FinanceLedgerView = "FINANCE.LEDGER.VIEW";
    public const string FinanceFeeManage = "FINANCE.FEE.MANAGE";
    public const string FinanceWithdrawalApprove = "FINANCE.WITHDRAWAL.APPROVE";
    public const string FinanceReconcile = "FINANCE.RECONCILE";
    public const string MarketingManage = "PROMO.MARKETING.MANAGE";

    public const string ClaimType = "perm";
}

public record PermissionDefinition(string Code, string Module, string Name);

public static class PermissionCatalog
{
    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(Permissions.SystemParameterView, "Hệ thống", "Xem tham số hệ thống"),
        new(Permissions.SystemParameterUpdate, "Hệ thống", "Sửa tham số hệ thống"),
        new(Permissions.AuditLogView, "Hệ thống", "Xem nhật ký thao tác"),
        new(Permissions.JobDashboardView, "Hệ thống", "Xem bảng việc nền"),
        new(Permissions.SearchReindex, "Hệ thống", "Lập lại chỉ mục tìm kiếm"),
        new(Permissions.JobRun, "Hệ thống", "Chạy ngay một việc nền"),
        new(Permissions.UserView, "Người dùng", "Xem người dùng"),
        new(Permissions.UserLock, "Người dùng", "Khoá / mở khoá người dùng"),
        new(Permissions.UserAssignRole, "Người dùng", "Gán vai trò quản trị"),
        new(Permissions.RoleView, "Phân quyền", "Xem vai trò"),
        new(Permissions.RoleManage, "Phân quyền", "Tạo / sửa / xoá vai trò"),
        new(Permissions.CategoryManage, "Ngành hàng", "Quản lý danh mục & thuộc tính"),
        new(Permissions.BrandManage, "Ngành hàng", "Quản lý thương hiệu"),
        new(Permissions.ProductReview, "Sản phẩm", "Duyệt sản phẩm"),
        new(Permissions.ProductBan, "Sản phẩm", "Khoá / mở khoá sản phẩm vi phạm"),
        new(Permissions.ShopView, "Shop", "Xem shop & hồ sơ KYC"),
        new(Permissions.ShopReview, "Shop", "Duyệt đăng ký shop"),
        new(Permissions.ShopLock, "Shop", "Khoá / mở khoá shop"),
        new(Permissions.ShopLabel, "Shop", "Cấp nhãn Mall / Yêu thích"),
        new(Permissions.VoucherManage, "Khuyến mãi", "Tạo / sửa / dừng voucher của sàn"),
        new(Permissions.CoinGrant, "Khuyến mãi", "Cộng / trừ ShopHub Xu cho người dùng"),
        new(Permissions.OrderView, "Đơn hàng", "Xem đơn hàng toàn sàn"),
        new(Permissions.DisputeResolve, "Đơn hàng", "Phân xử khiếu nại trả hàng"),
        new(Permissions.ReviewModerate, "Sản phẩm", "Xử lý báo cáo đánh giá vi phạm"),
        new(Permissions.FinanceLedgerView, "Tài chính", "Xem sổ cái và số dư"),
        new(Permissions.FinanceFeeManage, "Tài chính", "Đặt biểu phí theo ngành"),
        new(Permissions.FinanceWithdrawalApprove, "Tài chính", "Duyệt / từ chối rút tiền"),
        new(Permissions.FinanceReconcile, "Tài chính", "Đối soát với cổng thanh toán và đơn vị vận chuyển"),
        new(Permissions.MarketingManage, "Khuyến mãi", "Khung Flash Sale, duyệt đăng ký, banner, chiến dịch"),
    ];
}

public record RoleDefinition(string Code, string Name, string Description, IReadOnlyList<string> Permissions);

// Seeded system roles (spec VI.9). Later phases extend their permission lists.
public static class RoleCatalog
{
    public const string SuperAdmin = "SUPER_ADMIN";

    public static readonly IReadOnlyList<RoleDefinition> All =
    [
        new(SuperAdmin, "Quản trị cao nhất", "Toàn quyền trên sàn.", [Permissions.All]),
        new("OPERATIONS", "Vận hành", "Vận hành đơn hàng, người dùng, shop.",
            [Permissions.UserView, Permissions.UserLock, Permissions.AuditLogView, Permissions.SystemParameterView,
             Permissions.ShopView, Permissions.ShopReview, Permissions.ShopLock, Permissions.ShopLabel, Permissions.OrderView,
             Permissions.JobRun]),
        new("CONTENT_REVIEW", "Duyệt nội dung", "Duyệt sản phẩm, xử lý vi phạm.",
            [Permissions.UserView, Permissions.ProductReview, Permissions.ProductBan, Permissions.ShopView,
             Permissions.CategoryManage, Permissions.BrandManage]),
        new("CUSTOMER_SERVICE", "Chăm sóc khách hàng", "Hỗ trợ người mua và người bán.",
            [Permissions.UserView, Permissions.OrderView, Permissions.DisputeResolve, Permissions.ReviewModerate]),
        new("ACCOUNTING", "Kế toán", "Đối soát, giải ngân, báo cáo tài chính.",
            [Permissions.AuditLogView, Permissions.OrderView, Permissions.FinanceLedgerView, Permissions.FinanceFeeManage,
             Permissions.FinanceWithdrawalApprove, Permissions.FinanceReconcile, Permissions.JobRun]),
        new("MARKETING", "Marketing", "Voucher, Flash Sale, chiến dịch.",
            [Permissions.SystemParameterView, Permissions.VoucherManage, Permissions.CoinGrant, Permissions.MarketingManage]),
    ];
}
