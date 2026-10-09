using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Finance;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Seed;

/// <summary>
/// Fee schedule (reference data, always): the fixed fee of each top-level category taken from its current rate, and a
/// payment fee for every order, valid from a year before the day the data is loaded (covers the 90 days of sample orders;
/// never a date written in the code, L082). Also marks the bank accounts of approved shops as verified —
/// the admin checked them with the KYC file when approving. Idempotent.
/// </summary>
public sealed class FinanceSeeder(ShopHubDbContext db, IClock clock, ILogger<FinanceSeeder> logger)
{
    public const int DefaultPaymentFeeBp = 200;
    public const int DefaultFixedFeeBp = 400;
    public const int DefaultFreeshipXtraBp = 500;
    public const int DefaultVoucherXtraBp = 300;
    public const int HistoryDays = 365;

    public async Task SeedAsync(CancellationToken ct)
    {
        // 00:00 Vietnam time, HistoryDays before today
        var From = new DateTimeOffset(Application.Common.VietnamTime.Today(clock.UtcNow).AddDays(-HistoryDays).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7))
            .ToUniversalTime();
        if (!await db.FeeRules.AnyAsync(ct))
        {
            var now = clock.UtcNow;
            db.FeeRules.Add(new FeeRule(null, FeeType.Payment, DefaultPaymentFeeBp, From, "Phí thanh toán mặc định", now));
            db.FeeRules.Add(new FeeRule(null, FeeType.Fixed, DefaultFixedFeeBp, From, "Phí cố định mặc định", now));
            var roots = await db.Categories.AsNoTracking().IgnoreQueryFilters().Where(c => c.ParentId == null && c.CommissionRateBp > 0).ToListAsync(ct);
            foreach (var c in roots.Where(c => c.CommissionRateBp != DefaultFixedFeeBp))
                db.FeeRules.Add(new FeeRule(c.Id, FeeType.Fixed, c.CommissionRateBp, From, $"Phí cố định ngành {c.Name}", now));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("SEED fee schedule: {Count} rules", 2 + roots.Count(c => c.CommissionRateBp != DefaultFixedFeeBp));
        }

        // Service programmes (Freeship Xtra / Voucher Xtra): a platform-wide rate each, seeded on their own
        foreach (var (type, rate, note) in new[]
                 {
                     (FeeType.FreeshipXtra, DefaultFreeshipXtraBp, "Phí dịch vụ Freeship+"),
                     (FeeType.VoucherXtra, DefaultVoucherXtraBp, "Phí dịch vụ Voucher Plus"),
                 })
        {
            if (await db.FeeRules.AnyAsync(r => r.FeeType == type, ct)) continue;
            db.FeeRules.Add(new FeeRule(null, type, rate, From, note, clock.UtcNow));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("SEED fee rule {Type} {Rate} bp", type, rate);
        }

        var verified = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE shop.shop_bank_accounts b SET verified_at = {clock.UtcNow}
            FROM shop.shops s
            WHERE b.shop_id = s.id AND b.verified_at IS NULL AND s.approved_at IS NOT NULL
            """, ct);
        if (verified > 0) logger.LogInformation("SEED verified {Count} bank accounts of approved shops", verified);
    }
}
