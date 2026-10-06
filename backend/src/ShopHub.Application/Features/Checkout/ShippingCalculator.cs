using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Logistics;

namespace ShopHub.Application.Features.Checkout;

public record ShippingOption(string Code, string Name, string? Description, long Fee, int Days, DateOnly ExpectedDate, bool SupportsCod);

public record ParcelItem(int WeightG, int LengthMm, int WidthMm, int HeightMm, int Quantity);

/// <summary>Shipping options for one shop's parcel to the buyer's province (spec V).</summary>
public sealed class ShippingCalculator(IApplicationDbContext db, IEnumerable<ICarrier> carriers, ISystemParameters parameters, IClock clock)
{
    /// <summary>
    /// Region by the province code (Tổng cục Thống kê numbering): 01–37 Bắc, 38–68 Trung &amp; Tây Nguyên, 70+ Nam.
    /// </summary>
    public static int RegionOf(string provinceCode) => int.TryParse(provinceCode, out var code) ? code <= 37 ? 1 : code <= 68 ? 2 : 3 : 0;

    public static ShippingZone ZoneOf(string fromProvince, string toProvince) =>
        fromProvince == toProvince ? ShippingZone.SameProvince
        : RegionOf(fromProvince) == RegionOf(toProvince) ? ShippingZone.SameRegion
        : ShippingZone.CrossRegion;

    /// <summary>Chargeable weight = max(actual, volumetric L×W×H / 6000) for the whole parcel, at least 100 g.</summary>
    public static int ChargeableWeightG(IEnumerable<ParcelItem> items)
    {
        long actual = 0, volumetric = 0;
        foreach (var i in items)
        {
            actual += (long)i.WeightG * i.Quantity;
            // mm³ / 6000 = grams (cm³ / 6000 = kg)
            volumetric += (long)i.LengthMm * i.WidthMm * i.HeightMm / 6000 * i.Quantity;
        }
        return (int)Math.Clamp(Math.Max(Math.Max(actual, volumetric), 100), 100, int.MaxValue);
    }

    /// <summary>Options for one parcel; parcelValue (goods value) is what real carriers insure.</summary>
    public async Task<IReadOnlyList<ShippingOption>> QuoteAsync(RoutePoint from, RoutePoint to, int chargeableWeightG, long parcelValue, CancellationToken ct)
    {
        var zone = ZoneOf(from.ProvinceCode, to.ProvinceCode);
        var quote = new CarrierQuote(zone, chargeableWeightG, from, to, parcelValue);
        var holidays = await HolidaysAsync(ct);
        var today = VietnamTime.Today(clock.UtcNow);
        var options = new List<ShippingOption>();
        foreach (var carrier in await db.Carriers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync(ct))
        {
            if (carrier.SameProvinceOnly && zone != ShippingZone.SameProvince) continue;
            var provider = carriers.FirstOrDefault(c => c.Provider == carrier.Provider);
            if (provider is null) continue;
            long? fee;
            try
            {
                fee = await provider.QuoteFeeAsync(carrier, quote, ct);
            }
            catch (CarrierUnavailableException)
            {
                // A real carrier being down must not block checkout: its option is just not offered this time
                continue;
            }
            if (fee is null) continue;
            var days = carrier.DaysFor(zone);
            options.Add(new ShippingOption(carrier.Code, carrier.Name, carrier.Description, fee.Value, days,
                VietnamTime.AddWorkingDays(today, days, holidays), carrier.SupportsCod));
        }
        return options;
    }

    private async Task<IReadOnlySet<DateOnly>> HolidaysAsync(CancellationToken ct)
    {
        try
        {
            var raw = await parameters.GetStringAsync(ParameterKeys.LogisticsHolidays, ct);
            return (JsonSerializer.Deserialize<List<string>>(raw) ?? [])
                .Select(d => DateOnly.TryParse(d, out var date) ? date : (DateOnly?)null)
                .Where(d => d is not null).Select(d => d!.Value).ToHashSet();
        }
        catch (JsonException)
        {
            return new HashSet<DateOnly>();
        }
    }
}
