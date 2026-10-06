using ShopHub.Domain.Common;

namespace ShopHub.Domain.Logistics;

public enum ShippingZone
{
    SameProvince,  // nội tỉnh
    SameRegion,    // nội miền
    CrossRegion,   // liên miền
}

/// <summary>
/// A shipping channel the buyer picks at checkout ("Nhanh", "Tiết kiệm", "Hoả tốc"…). Fees come from
/// <see cref="ShippingRate"/>, never from hard-coded numbers.
/// </summary>
public class Carrier : Entity
{
    private Carrier() { }

    public Carrier(string code, string name, string provider, int sortOrder)
    {
        Code = code;
        Name = name;
        Provider = provider;
        SortOrder = sortOrder;
        IsActive = true;
        SupportsCod = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    // Which ICarrier implementation serves it (SIMULATED now; GHN/GHTK in Phase 11)
    public string Provider { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public bool SupportsCod { get; private set; }
    // Only within one province (hoả tốc)
    public bool SameProvinceOnly { get; private set; }
    // Days to deliver per zone (calendar working days, Sundays / holidays skipped)
    public int DaysSameProvince { get; private set; }
    public int DaysSameRegion { get; private set; }
    public int DaysCrossRegion { get; private set; }
    public int SortOrder { get; private set; }

    public void Configure(string name, string? description, bool isActive, bool supportsCod, bool sameProvinceOnly,
        int daysSameProvince, int daysSameRegion, int daysCrossRegion)
    {
        if (daysSameProvince < 0 || daysSameRegion < 0 || daysCrossRegion < 0) throw new BusinessRuleException("Số ngày giao không được âm.");
        Name = name;
        Description = description;
        IsActive = isActive;
        SupportsCod = supportsCod;
        SameProvinceOnly = sameProvinceOnly;
        DaysSameProvince = daysSameProvince;
        DaysSameRegion = daysSameRegion;
        DaysCrossRegion = daysCrossRegion;
    }

    public int DaysFor(ShippingZone zone) => zone switch
    {
        ShippingZone.SameProvince => DaysSameProvince,
        ShippingZone.SameRegion => DaysSameRegion,
        _ => DaysCrossRegion,
    };
}

/// <summary>Fee for a zone and a weight band; the last band of a zone is open-ended and adds a fee per extra 500 g.</summary>
public class ShippingRate : Entity
{
    private ShippingRate() { }

    public ShippingRate(Guid carrierId, ShippingZone zone, int weightFromG, int? weightToG, long fee, long extraPer500G)
    {
        if (fee < 0 || extraPer500G < 0) throw new BusinessRuleException("Phí vận chuyển không được âm.");
        if (weightToG is { } to && to <= weightFromG) throw new BusinessRuleException("Khoảng cân nặng không hợp lệ.");
        CarrierId = carrierId;
        Zone = zone;
        WeightFromG = weightFromG;
        WeightToG = weightToG;
        Fee = fee;
        ExtraPer500G = extraPer500G;
    }

    public Guid CarrierId { get; private set; }
    public ShippingZone Zone { get; private set; }
    public int WeightFromG { get; private set; }
    public int? WeightToG { get; private set; }
    public long Fee { get; private set; }
    public long ExtraPer500G { get; private set; }

    public bool Covers(int weightG) => weightG >= WeightFromG && (WeightToG is null || weightG < WeightToG);

    public long FeeFor(int weightG)
    {
        if (WeightToG is not null || weightG <= WeightFromG) return Fee;
        var extraSteps = (weightG - WeightFromG + 499) / 500;
        return checked(Fee + extraSteps * ExtraPer500G);
    }
}
