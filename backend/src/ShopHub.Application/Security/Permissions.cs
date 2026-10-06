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

    public const string ClaimType = "perm";
}
