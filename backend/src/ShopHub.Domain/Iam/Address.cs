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
    District = 2,
    Ward = 3,
}

// Vietnamese administrative units (tỉnh → quận/huyện → phường/xã), keyed by the official code
public class AdminDivision
{
    private AdminDivision() { }

    public AdminDivision(string code, string name, AdminDivisionLevel level, string? parentCode)
    {
        Code = code;
        Name = name;
        Level = level;
        ParentCode = parentCode;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public AdminDivisionLevel Level { get; private set; }
    public string? ParentCode { get; private set; }

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
    public string DistrictCode { get; private set; } = string.Empty;
    public string WardCode { get; private set; } = string.Empty;
    public string Street { get; private set; } = string.Empty;
    public double? Lat { get; private set; }
    public double? Lng { get; private set; }
    public AddressType Type { get; private set; }
    public bool IsDefault { get; private set; }

    public void Update(string receiverName, string phone, string provinceCode, string districtCode, string wardCode,
        string street, double? lat, double? lng, AddressType type)
    {
        ReceiverName = receiverName.Trim();
        Phone = phone;
        ProvinceCode = provinceCode;
        DistrictCode = districtCode;
        WardCode = wardCode;
        Street = street.Trim();
        Lat = lat;
        Lng = lng;
        Type = type;
    }

    public void SetDefault(bool isDefault) => IsDefault = isDefault;
}
