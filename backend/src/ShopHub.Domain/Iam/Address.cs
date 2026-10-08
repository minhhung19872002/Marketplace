using ShopHub.Domain.Common;

namespace ShopHub.Domain.Iam;

public enum AddressType
{
    Home,
    Office,
}

public enum AdminDivisionLevel
{
    Province = 1,
    /// <summary>Quận/huyện — abolished on 2025-07-01; only kept (inactive) for addresses and orders made before.</summary>
    District = 2,
    Ward = 3,
}

/// <summary>
/// Vietnamese administrative units keyed by the official code. Since 2025-07-01 two levels: tỉnh/thành → phường/xã/đặc
/// khu (a ward's parent is its province). Units of the old three-level list that no longer exist stay as inactive rows,
/// so old addresses and order snapshots keep resolving; only active units can be picked.
/// </summary>
public class AdminDivision
{
    private AdminDivision() { }

    public AdminDivision(string code, string name, AdminDivisionLevel level, string? parentCode)
    {
        Code = code;
        Name = name;
        Level = level;
        ParentCode = parentCode;
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public AdminDivisionLevel Level { get; private set; }
    public string? ParentCode { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>Takes the official data for this code (a reused code may now name another, merged unit).</summary>
    public void Sync(string name, AdminDivisionLevel level, string? parentCode)
    {
        Name = name;
        Level = level;
        ParentCode = parentCode;
        IsActive = true;
    }

    public void Retire() => IsActive = false;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) throw new BusinessRuleException("Tên đơn vị hành chính từ 1 đến 100 ký tự.");
        Name = name.Trim();
    }
}

public class Address : AuditableEntity
{
    private Address() { }

    public Address(Guid userId)
    {
        UserId = userId;
    }

    public Guid UserId { get; private set; }
    public string ReceiverName { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string ProvinceCode { get; private set; } = string.Empty;
    /// <summary>Only on addresses saved before the two-level reform; null on every new or edited address.</summary>
    public string? DistrictCode { get; private set; }
    public string WardCode { get; private set; } = string.Empty;
    public string Street { get; private set; } = string.Empty;
    public double? Lat { get; private set; }
    public double? Lng { get; private set; }
    public AddressType Type { get; private set; }
    public bool IsDefault { get; private set; }

    public void Update(string receiverName, string phone, string provinceCode, string wardCode,
        string street, double? lat, double? lng, AddressType type)
    {
        ReceiverName = receiverName.Trim();
        Phone = phone;
        ProvinceCode = provinceCode;
        DistrictCode = null;
        WardCode = wardCode;
        Street = street.Trim();
        Lat = lat;
        Lng = lng;
        Type = type;
    }

    public void SetDefault(bool isDefault) => IsDefault = isDefault;
}
