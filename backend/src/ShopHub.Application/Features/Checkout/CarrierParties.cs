using System.Text.Json;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Checkout;

/// <summary>Sender / receiver of a parcel from the shop's warehouse and the address snapshot taken at checkout.</summary>
public static class CarrierParties
{
    public static CarrierAddress FromWarehouse(ShopWarehouse w) =>
        new(w.ContactName, w.Phone, w.Street, new RoutePoint(w.ProvinceCode, w.DistrictCode, w.WardCode));

    /// <summary>The receiver frozen in the checkout (later edits of the address book do not move a placed order).</summary>
    public static CarrierAddress? FromSnapshot(string snapshot)
    {
        try
        {
            var root = JsonDocument.Parse(snapshot).RootElement;
            string Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var province = Str("provinceCode");
            return province.Length == 0 ? null
                : new CarrierAddress(Str("receiverName"), Str("phone"), Str("street"), new RoutePoint(province, Str("districtCode"), Str("wardCode")));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
