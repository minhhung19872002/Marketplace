using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Security;
using ShopHub.Domain.Iam;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Seed;

/// <summary>
/// Accounts &amp; reference data. Every part checks its OWN presence so a partially seeded database only gets what it lacks.
/// Generated passwords are printed to the log once (first seed) and never stored in the repository.
/// </summary>
public sealed class IdentitySeeder(ShopHubDbContext db, IPasswordHasher hasher, IClock clock, ILogger<IdentitySeeder> logger)
{
    public const string AdminUsername = "admin";

    // Sample phone ranges (documented in docs/04): buyers 09000000xx, shop owners 09000001xx, shop staff 09000002xx
    private static readonly string[] BuyerNames =
    [
        "Nguyễn Văn An", "Trần Thị Bình", "Lê Hoàng Cường", "Phạm Thu Dung", "Hoàng Minh Đức", "Vũ Thị Hà", "Đặng Quốc Huy",
        "Bùi Ngọc Lan", "Đỗ Thanh Long", "Hồ Thị Mai", "Ngô Gia Nam", "Dương Thảo Nhi", "Lý Văn Phúc", "Trịnh Kim Quyên",
        "Đinh Công Sơn", "Mai Thị Tâm", "Tạ Đức Thắng", "Lâm Bảo Uyên", "Châu Văn Vinh", "Kiều Hải Yến",
    ];

    private static readonly string[] OwnerNames = ["Trương Minh Khoa", "Phan Thị Hạnh", "Võ Quang Tín"];
    private static readonly string[] StaffNames = ["Lưu Thị Ngân", "Quách Văn Tài"];

    public async Task SeedAsync(bool sampleData, CancellationToken ct)
    {
        var newPermissions = await SeedPermissionsAsync(ct);
        await SeedRolesAsync(newPermissions, ct);
        await SeedAdminDivisionsAsync(ct);
        await SeedAdminAsync(ct);
        if (sampleData) await SeedSampleAccountsAsync(ct);
    }

    // Upsert: names/modules follow the code catalog, unknown codes are left for an admin to review.
    // Returns the codes that did not exist before this run.
    private async Task<HashSet<string>> SeedPermissionsAsync(CancellationToken ct)
    {
        var existing = await db.Permissions.ToDictionaryAsync(p => p.Code, ct);
        var added = new HashSet<string>();
        foreach (var def in PermissionCatalog.All)
        {
            if (existing.TryGetValue(def.Code, out var p)) p.Rename(def.Module, def.Name);
            else
            {
                db.Permissions.Add(new Permission(def.Code, def.Module, def.Name));
                added.Add(def.Code);
            }
        }
        await db.SaveChangesAsync(ct);
        return added;
    }

    private async Task SeedRolesAsync(HashSet<string> newPermissions, CancellationToken ct)
    {
        var existing = await db.Roles.IgnoreQueryFilters().Include(r => r.Permissions).ToDictionaryAsync(r => r.Code, ct);
        foreach (var def in RoleCatalog.All)
        {
            if (existing.TryGetValue(def.Code, out var role))
            {
                // Super admin always keeps the wildcard; other system roles keep whatever admins configured
                var codes = role.Permissions.Select(p => p.PermissionCode).ToList();
                if (def.Code == RoleCatalog.SuperAdmin && !codes.Contains(Permissions.All)) codes.Add(Permissions.All);
                // A permission introduced by this release goes to the system roles that declare it;
                // anything an admin removed earlier is not re-added
                codes.AddRange(def.Permissions.Where(newPermissions.Contains));
                if (codes.Count != role.Permissions.Count) role.SetPermissions(codes);
                continue;
            }

            var created = new Role(def.Code, def.Name, def.Description, isSystem: true);
            db.Roles.Add(created);
            created.SetPermissions(def.Permissions);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedAdminDivisionsAsync(CancellationToken ct)
    {
        if (await db.AdminDivisions.AnyAsync(ct)) return;

        await using var stream = typeof(IdentitySeeder).Assembly
            .GetManifestResourceStream("ShopHub.Infrastructure.Seed.Data.admin-divisions-vn.json")
            ?? throw new InvalidOperationException("Thiếu tệp admin-divisions-vn.json trong assembly.");
        var rows = await JsonSerializer.DeserializeAsync<JsonElement[][]>(stream, cancellationToken: ct) ?? [];

        // Parents first so the self-referencing foreign key is satisfied
        var units = rows
            .Select(r => new AdminDivision(r[0].GetString()!, r[1].GetString()!, (AdminDivisionLevel)r[2].GetInt32(),
                r[3].ValueKind == JsonValueKind.Null ? null : r[3].GetString()))
            .OrderBy(u => u.Level)
            .ToList();
        foreach (var level in units.GroupBy(u => u.Level))
        {
            db.AdminDivisions.AddRange(level);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        logger.LogInformation("Seeded {Count} administrative divisions", units.Count);
    }

    private async Task SeedAdminAsync(CancellationToken ct)
    {
        var superAdminRoleId = await db.Roles.Where(r => r.Code == RoleCatalog.SuperAdmin).Select(r => r.Id).SingleAsync(ct);
        if (await db.UserRoles.AnyAsync(ur => ur.RoleId == superAdminRoleId, ct)) return;

        var password = NewPassword();
        var admin = User.Register(null, "admin@shophub.local", hasher.Hash(password), "Quản trị viên", clock.UtcNow);
        admin.SetUsername(AdminUsername);
        admin.RequirePasswordChange();
        db.Users.Add(admin);
        db.UserRoles.Add(new UserRole(admin.Id, superAdminRoleId));
        await db.SaveChangesAsync(ct);

        PrintCredentials($"Tài khoản quản trị mặc định: tên đăng nhập '{AdminUsername}', mật khẩu '{password}' — đổi ngay ở lần đăng nhập đầu tiên");
        logger.LogInformation("Seeded default admin account '{Username}' (credentials printed to stdout once)", AdminUsername);
    }

    private async Task SeedSampleAccountsAsync(CancellationToken ct)
    {
        await SeedGroupAsync("người mua", BuyerNames, i => $"09000000{i + 1:D2}", ct);
        await SeedGroupAsync("chủ shop", OwnerNames, i => $"09000001{i + 1:D2}", ct);
        await SeedGroupAsync("nhân viên shop", StaffNames, i => $"09000002{i + 1:D2}", ct);
    }

    private async Task SeedGroupAsync(string label, string[] names, Func<int, string> phoneOf, CancellationToken ct)
    {
        var phones = names.Select((_, i) => phoneOf(i)).ToList();
        var existing = await db.Users.IgnoreQueryFilters().Where(u => u.Phone != null && phones.Contains(u.Phone)).Select(u => u.Phone!).ToListAsync(ct);
        var created = new List<(string Phone, string Password)>();

        for (var i = 0; i < names.Length; i++)
        {
            if (existing.Contains(phones[i])) continue;
            var password = NewPassword();
            db.Users.Add(User.Register(phones[i], null, hasher.Hash(password), names[i], clock.UtcNow));
            created.Add((phones[i], password));
        }
        if (created.Count == 0) return;

        await db.SaveChangesAsync(ct);
        foreach (var (phone, password) in created)
            PrintCredentials($"Tài khoản mẫu ({label}): {phone} / {password}");
        logger.LogInformation("Seeded {Count} sample account(s) ({Label})", created.Count, label);
    }

    /// <summary>
    /// Generated passwords go to the process stdout only (visible in `docker compose logs api` the first time) —
    /// never through Serilog, so they cannot land in log files or the sys.logs table (spec 6.1).
    /// </summary>
    private static void PrintCredentials(string line) => Console.Out.WriteLine($"[SEED] {line}");

    // 12 characters, letters + digits (satisfies the password policy)
    private static string NewPassword()
    {
        const string letters = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "23456789";
        var chars = Enumerable.Range(0, 9).Select(_ => letters[RandomNumberGenerator.GetInt32(letters.Length)])
            .Concat(Enumerable.Range(0, 3).Select(_ => digits[RandomNumberGenerator.GetInt32(digits.Length)]))
            .ToArray();
        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }
}
