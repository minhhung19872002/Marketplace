namespace ShopHub.Application.Security;

// Permissions inside a shop (shop_staff.permissions). Owners hold all of them implicitly.
public static class ShopPermissions
{
    public const string ProductView = "PRODUCT.VIEW";
    public const string ProductManage = "PRODUCT.MANAGE";
    public const string InventoryManage = "INVENTORY.MANAGE";
    public const string SettingsManage = "SETTINGS.MANAGE";
    public const string StaffManage = "STAFF.MANAGE";

    public static readonly IReadOnlyList<string> All = [ProductView, ProductManage, InventoryManage, SettingsManage, StaffManage];
}
