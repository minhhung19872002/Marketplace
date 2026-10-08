using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Iam;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Seed;

/// <summary>
/// Buyers for the load test (e2e/load, spec 6.3 "1.000 người đồng thời"): SH_SEED_LOAD_USERS accounts with phones
/// 0970000000… and a delivery address in Hà Nội, all with the password given in SH_LOAD_USER_PASSWORD (never logged,
/// never in the repository). Only runs when both variables are set. Idempotent.
/// </summary>
public sealed class LoadUserSeeder(ShopHubDbContext db, ShopHubSettings settings, IPasswordHasher hasher, IClock clock, ILogger<LoadUserSeeder> logger)
{
    public static string PhoneOf(int i) => $"0970{i:000000}";

    public async Task SeedAsync(CancellationToken ct)
    {
        if (settings.LoadUsers <= 0 || settings.LoadUserPassword is null) return;
        var phones = Enumerable.Range(0, settings.LoadUsers).Select(PhoneOf).ToList();
        var existing = (await db.Users.IgnoreQueryFilters().Where(u => u.Phone != null && phones.Contains(u.Phone)).Select(u => u.Phone!).ToListAsync(ct))
            .ToHashSet();
        var missing = phones.Where(p => !existing.Contains(p)).ToList();
        if (missing.Count == 0) return;

        // One hash for every load account: hashing 1.000 times would only slow the start-up down
        var hash = hasher.Hash(settings.LoadUserPassword);
        // Hà Nội, first ward (two-level divisions since 2025-07-01)
        var ward = await db.AdminDivisions.AsNoTracking().Where(d => d.ParentCode == "01" && d.Level == AdminDivisionLevel.Ward && d.IsActive)
            .OrderBy(d => d.Code).Select(d => d.Code).FirstAsync(ct);
        var now = clock.UtcNow;
        foreach (var batch in missing.Chunk(200))
        {
            foreach (var phone in batch)
            {
                var user = User.Register(phone, null, hash, $"Khách tải {phone[^4..]}", now);
                db.Users.Add(user);
                var address = new Address(user.Id);
                address.Update($"Khách tải {phone[^4..]}", phone, "01", ward, "1 Phố Thử Tải", null, null, AddressType.Home);
                address.SetDefault(true);
                db.Addresses.Add(address);
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        logger.LogInformation("SEED load-test buyers: {Count} created", missing.Count);
    }
}
