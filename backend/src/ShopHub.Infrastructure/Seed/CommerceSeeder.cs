using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Shops;
using ShopHub.Infrastructure.Commerce;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Seed;

/// <summary>
/// Shipping channels and their rate table (reference data, always), plus sample vouchers: the three codes the old
/// checkout had hard-coded (SHOPHUB50, FREESHIP, SALE12) and a couple of shop vouchers. Idempotent.
/// </summary>
public sealed class CommerceSeeder(ShopHubDbContext db, IClock clock, ShopHubSettings settings, ILogger<CommerceSeeder> logger)
{
    private sealed record Band(int From, int? To, long Fee, long Extra = 0);

    public async Task SeedAsync(bool sampleData, CancellationToken ct)
    {
        await SeedCarriersAsync(ct);
        await SeedRealCarriersAsync(ct);
        if (sampleData) await SeedVouchersAsync(ct);
    }

    /// <summary>
    /// A shipping channel per real carrier whose keys are set (fees come from the carrier's API, not the rate table).
    /// Without keys the row may stay from an earlier run: no ICarrier serves it, so checkout simply does not offer it.
    /// </summary>
    private async Task SeedRealCarriersAsync(CancellationToken ct)
    {
        var wanted = new List<(string Code, string Name, string Provider, string Description, int Order)>();
        if (settings.Providers.Ghn is not null)
            wanted.Add(("GHN_STD", "Giao Hàng Nhanh", Commerce.Providers.GhnCarrier.ProviderName, "GHN — giao tiêu chuẩn, phí theo bảng giá GHN", 10));
        if (settings.Providers.Ghtk is not null)
            wanted.Add(("GHTK_STD", "Giao Hàng Tiết Kiệm", Commerce.Providers.GhtkCarrier.ProviderName, "GHTK — đường bộ, phí theo bảng giá GHTK", 11));
        var added = 0;
        foreach (var w in wanted)
        {
            if (await db.Carriers.AnyAsync(c => c.Code == w.Code, ct)) continue;
            var c = new Carrier(w.Code, w.Name, w.Provider, w.Order);
            c.Configure(w.Name, w.Description, true, true, false, 1, 2, 4);
            db.Carriers.Add(c);
            added++;
        }
        if (added == 0) return;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} real carrier channel(s)", added);
    }

    private async Task SeedCarriersAsync(CancellationToken ct)
    {
        if (await db.Carriers.AnyAsync(ct)) return;

        Carrier Add(string code, string name, string description, int order, bool sameProvinceOnly, int d1, int d2, int d3,
            Dictionary<ShippingZone, Band[]> table)
        {
            var c = new Carrier(code, name, SimulatedCarrier.ProviderName, order);
            c.Configure(name, description, true, true, sameProvinceOnly, d1, d2, d3);
            db.Carriers.Add(c);
            foreach (var (zone, bands) in table)
                foreach (var b in bands)
                    db.ShippingRates.Add(new ShippingRate(c.Id, zone, b.From, b.To, b.Fee, b.Extra));
            return c;
        }

        Add("SIM_FAST", "Nhanh", "Giao trong 1–3 ngày làm việc (đơn vị vận chuyển giả lập)", 1, false, 1, 2, 3, new()
        {
            [ShippingZone.SameProvince] = [new(0, 500, 18_000), new(500, 1000, 20_000), new(1000, 2000, 25_000), new(2000, null, 25_000, 3_000)],
            [ShippingZone.SameRegion] = [new(0, 500, 25_000), new(500, 1000, 30_000), new(1000, 2000, 35_000), new(2000, null, 35_000, 4_000)],
            [ShippingZone.CrossRegion] = [new(0, 500, 32_000), new(500, 1000, 38_000), new(1000, 2000, 45_000), new(2000, null, 45_000, 5_000)],
        });
        Add("SIM_ECO", "Tiết kiệm", "Giao trong 2–5 ngày làm việc, phí thấp (giả lập)", 2, false, 2, 3, 5, new()
        {
            [ShippingZone.SameProvince] = [new(0, 500, 15_000), new(500, 1000, 16_500), new(1000, 2000, 20_000), new(2000, null, 20_000, 2_500)],
            [ShippingZone.SameRegion] = [new(0, 500, 20_000), new(500, 1000, 24_000), new(1000, 2000, 28_000), new(2000, null, 28_000, 3_000)],
            [ShippingZone.CrossRegion] = [new(0, 500, 25_000), new(500, 1000, 30_000), new(1000, 2000, 36_000), new(2000, null, 36_000, 4_000)],
        });
        Add("SIM_EXPRESS", "Hoả tốc", "Giao trong ngày, chỉ trong cùng tỉnh/thành (giả lập)", 3, true, 0, 0, 0, new()
        {
            [ShippingZone.SameProvince] = [new(0, 2000, 35_000), new(2000, 5000, 50_000), new(5000, null, 50_000, 5_000)],
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded 3 shipping channel(s) with their rate tables");
    }

    private async Task SeedVouchersAsync(CancellationToken ct)
    {
        if (await db.Vouchers.IgnoreQueryFilters().AnyAsync(ct)) return;
        var now = clock.UtcNow;
        var start = now.AddDays(-1);
        var end = now.AddYears(1);

        var shophub50 = new Voucher(VoucherOwner.Platform, null, "SHOPHUB50", "Giảm ₫50.000 cho đơn từ ₫250.000");
        shophub50.Configure(VoucherType.Amount, 50_000, 0, null, 250_000, VoucherAudience.Everyone, [], [], start, end, 10_000, 3, true, VoucherChannel.All);
        var freeship = new Voucher(VoucherOwner.Platform, null, "FREESHIP", "Miễn phí vận chuyển tối đa ₫30.000");
        freeship.Configure(VoucherType.FreeShipping, 0, 0, 30_000, 0, VoucherAudience.Everyone, [], [], start, end, null, 10, true, VoucherChannel.All);
        var sale12 = new Voucher(VoucherOwner.Platform, null, "SALE12", "Giảm 12% tối đa ₫100.000 cho đơn từ ₫500.000");
        sale12.Configure(VoucherType.Percent, 0, 1_200, 100_000, 500_000, VoucherAudience.Everyone, [], [], start, end, 5_000, 2, true, VoucherChannel.All);
        db.Vouchers.AddRange(shophub50, freeship, sale12);

        // Two vouchers for each of the first sample shops (alphabetical, stable codes SHOP01GIAM10K / SHOP01SALE5…)
        var shops = await db.Shops.AsNoTracking().Where(s => s.Status == ShopStatus.Active).OrderBy(s => s.Name).ThenBy(s => s.Id).Take(10).ToListAsync(ct);
        for (var i = 0; i < shops.Count; i++)
        {
            var n = (i + 1).ToString("00");
            var fixedOff = new Voucher(VoucherOwner.Shop, shops[i].Id, $"SHOP{n}GIAM10K", $"{shops[i].Name}: giảm ₫10.000 đơn từ ₫99.000");
            fixedOff.Configure(VoucherType.Amount, 10_000, 0, null, 99_000, VoucherAudience.Everyone, [], [], start, end, 1_000, 2, true, VoucherChannel.All);
            var percentOff = new Voucher(VoucherOwner.Shop, shops[i].Id, $"SHOP{n}SALE5", $"{shops[i].Name}: giảm 5% tối đa ₫30.000");
            percentOff.Configure(VoucherType.Percent, 0, 500, 30_000, 0, VoucherAudience.Everyone, [], [], start, end, 1_000, 1, true, VoucherChannel.All);
            db.Vouchers.AddRange(fixedOff, percentOff);
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} sample voucher(s)", 3 + shops.Count * 2);
    }
}
