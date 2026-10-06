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

    public const string UserView = "IAM.USER.VIEW";
    public const string UserLock = "IAM.USER.LOCK";
    public const string UserAssignRole = "IAM.USER.ASSIGN_ROLE";
    public const string RoleView = "IAM.ROLE.VIEW";
    public const string RoleManage = "IAM.ROLE.MANAGE";

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
        new(Permissions.UserView, "Người dùng", "Xem người dùng"),
        new(Permissions.UserLock, "Người dùng", "Khoá / mở khoá người dùng"),
        new(Permissions.UserAssignRole, "Người dùng", "Gán vai trò quản trị"),
        new(Permissions.RoleView, "Phân quyền", "Xem vai trò"),
        new(Permissions.RoleManage, "Phân quyền", "Tạo / sửa / xoá vai trò"),
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
            [Permissions.UserView, Permissions.UserLock, Permissions.AuditLogView, Permissions.SystemParameterView]),
        new("CONTENT_REVIEW", "Duyệt nội dung", "Duyệt sản phẩm, xử lý vi phạm.", [Permissions.UserView]),
        new("CUSTOMER_SERVICE", "Chăm sóc khách hàng", "Hỗ trợ người mua và người bán.", [Permissions.UserView]),
        new("ACCOUNTING", "Kế toán", "Đối soát, giải ngân, báo cáo tài chính.", [Permissions.AuditLogView]),
        new("MARKETING", "Marketing", "Voucher, Flash Sale, chiến dịch.", [Permissions.SystemParameterView]),
    ];
}
