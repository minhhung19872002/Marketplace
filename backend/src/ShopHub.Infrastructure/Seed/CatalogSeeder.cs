using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Media;
using ShopHub.Application.Security;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Media;
using ShopHub.Domain.Shops;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Seed;

/// <summary>
/// Category tree + industry attributes + brands (reference data, always), and — with sample data on — 30 shops and the
/// 40 legacy products as real rows. Product images go through the real pipeline (decode → 3 WebP sizes → MinIO) and
/// products through the real lifecycle (submit → approve). Every part checks its own presence.
/// </summary>
public sealed class CatalogSeeder(
    ShopHubDbContext db,
    IObjectStorage storage,
    IImageProcessor images,
    IDataEncryptor encryptor,
    ProductGenerator generator,
    IClock clock,
    ILogger<CatalogSeeder> logger)
{
    private const string Resource = "ShopHub.Infrastructure.Seed.Data.";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task SeedAsync(bool sampleData, CancellationToken ct)
    {
        var seed = await ReadAsync<CatalogSeed>("catalog-seed.json", ct);
        await SeedCategoriesAsync(seed, ct);
        await SeedBrandsAsync(seed, ct);
        if (!sampleData) return;
        await SeedShopsAsync(seed, ct);
        await SeedStaffAsync(ct);
        await SeedProductsAsync(seed, ct);
        await generator.GenerateAsync(ct);
    }

    private async Task SeedCategoriesAsync(CatalogSeed seed, CancellationToken ct)
    {
        if (await db.Categories.IgnoreQueryFilters().AnyAsync(ct)) return;

        var sort = 0;
        foreach (var top in seed.Categories)
        {
            var root = new Category(null, 1, top.Name, Slug.From(top.Name), top.Icon, sort++, top.CommissionBp);
            db.Categories.Add(root);
            var s2 = 0;
            foreach (var l2 in top.Children)
            {
                var mid = new Category(root.Id, 2, l2.Name, Slug.From(l2.Name), null, s2++, top.CommissionBp);
                db.Categories.Add(mid);
                var s3 = 0;
                foreach (var l3 in l2.Children)
                {
                    var leaf = new Category(mid.Id, 3, l3.Name, Slug.From(l3.Name), null, s3++, top.CommissionBp);
                    db.Categories.Add(leaf);
                    // Every leaf: origin (required) + the industry set of its top-level category
                    db.CategoryAttributes.Add(new CategoryAttribute(leaf.Id, "Xuất xứ", AttributeInputType.SingleSelect, null, true, true,
                        seed.OriginOptions, 0));
                    var a = 1;
                    foreach (var def in seed.AttributeSets[top.AttributeSet])
                        db.CategoryAttributes.Add(new CategoryAttribute(leaf.Id, def.Name, def.Type, def.Unit, def.Required, def.Filterable,
                            def.Options ?? [], a++));
                }
            }
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded category tree ({Count} top-level)", seed.Categories.Count);
    }

    private async Task SeedBrandsAsync(CatalogSeed seed, CancellationToken ct)
    {
        var existing = await db.Brands.IgnoreQueryFilters().Select(b => b.Slug).ToListAsync(ct);
        foreach (var name in seed.Brands.Where(b => !existing.Contains(Slug.From(b))))
            db.Brands.Add(new Brand(name, Slug.From(name), null, isVerified: true));
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedShopsAsync(CatalogSeed seed, CancellationToken ct)
    {
        var owners = await db.Users.Where(u => u.Phone != null && u.Phone.StartsWith("09000001")).OrderBy(u => u.Phone)
            .Select(u => u.Id).ToListAsync(ct);
        if (owners.Count == 0)
        {
            logger.LogWarning("No sample shop owners found; skipping sample shops");
            return;
        }

        var existing = await db.Shops.IgnoreQueryFilters().Select(s => s.Slug).ToListAsync(ct);
        var now = clock.UtcNow;
        var created = 0;
        foreach (var s in seed.Shops.Where(s => !existing.Contains(Slug.From(s.Name))))
        {
            var ownerId = owners[s.Owner % owners.Count];
            var shop = new Shop(ownerId, s.Name, Slug.From(s.Name), s.Type);
            shop.UpdateProfile(s.Description, null, null);
            shop.Approve(now);
            shop.SetLabels(s.Mall, s.Preferred);
            db.Shops.Add(shop);
            db.ShopStaff.Add(new ShopStaff(shop.Id, ownerId, ShopStaffRole.Owner, ShopPermissions.All));

            var warehouse = new ShopWarehouse(shop.Id);
            warehouse.Update("Kho chính", "Bộ phận kho", "0900000999", s.Warehouse.Province, s.Warehouse.District, s.Warehouse.Ward,
                $"Số {created + 1} Đường Mẫu", isPickupDefault: true, isReturnDefault: true);
            db.ShopWarehouses.Add(warehouse);

            var kyc = new ShopKyc(shop.Id);
            kyc.SetBusiness($"Công ty TNHH {s.Name} (dữ liệu mẫu)", $"{3700000000 + created}", "seed/no-file.pdf");
            kyc.Review(true, ownerId, null, now);
            db.ShopKycs.Add(kyc);

            var account = $"{1023000000L + created}";
            db.ShopBankAccounts.Add(new ShopBankAccount(shop.Id, "VCB", encryptor.Encrypt(account), account[^4..],
                "CONG TY MAU", isDefault: true));
            created++;
        }
        await db.SaveChangesAsync(ct);
        if (created > 0) logger.LogInformation("Seeded {Count} sample shop(s)", created);
    }

    // The 2 sample staff accounts (09000002xx) work for the first shop of the first sample owner: a manager and a CSKH
    private async Task SeedStaffAsync(CancellationToken ct)
    {
        var staffUsers = await db.Users.Where(u => u.Phone != null && u.Phone.StartsWith("09000002")).OrderBy(u => u.Phone)
            .Select(u => u.Id).ToListAsync(ct);
        var owner = await db.Users.Where(u => u.Phone != null && u.Phone.StartsWith("09000001")).OrderBy(u => u.Phone)
            .Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (staffUsers.Count == 0 || owner is null) return;
        if (await db.ShopStaff.IgnoreQueryFilters().AnyAsync(s => staffUsers.Contains(s.UserId), ct)) return;
        var shopId = await db.Shops.Where(s => s.OwnerId == owner).OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
            .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        if (shopId is null) return;
        ShopStaffRole[] roles = [ShopStaffRole.Manager, ShopStaffRole.CustomerService];
        for (var i = 0; i < staffUsers.Count && i < roles.Length; i++)
            db.ShopStaff.Add(new ShopStaff(shopId.Value, staffUsers[i], roles[i], ShopPermissions.DefaultsFor(roles[i])));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} sample shop staff", Math.Min(staffUsers.Count, roles.Length));
    }

    private async Task SeedProductsAsync(CatalogSeed seed, CancellationToken ct)
    {
        var manifest = await ReadAsync<Dictionary<string, List<string>>>("product-images.manifest.json", ct);
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var shops = await db.Shops.AsNoTracking().ToDictionaryAsync(s => s.Name, ct);
        var now = clock.UtcNow;
        var created = 0;

        foreach (var p in seed.Products)
        {
            if (!shops.TryGetValue(p.Shop, out var shop)) continue;
            if (await db.Products.IgnoreQueryFilters().AnyAsync(x => x.ShopId == shop.Id && x.Name == p.Name, ct)) continue;

            var leaf = ResolvePath(categories, p.Category);
            var definitions = await db.CategoryAttributes.AsNoTracking().Where(a => a.CategoryId == leaf.Id).ToListAsync(ct);
            var attributes = definitions.Where(d => p.Attributes.ContainsKey(d.Name)).Select(d => (d.Id, (IReadOnlyList<string>)p.Attributes[d.Name])).ToList();
            foreach (var d in definitions)
                if (d.Check(p.Attributes.GetValueOrDefault(d.Name) ?? []) is { } error)
                    throw new InvalidOperationException($"Dữ liệu gieo sai ở \"{p.Name}\": {error}");

            var media = new List<MediaSpec>();
            foreach (var file in manifest[p.LegacyId.ToString()])
            {
                var bytes = await ReadBytesAsync($"ProductImages.{file}", ct);
                var processed = images.Process(bytes, ImageSizes.Public);
                var key = $"product/seed/{p.LegacyId:D2}-{Path.GetFileNameWithoutExtension(file)}";
                foreach (var v in processed.Variants)
                    await storage.PutAsync(Buckets.Products, ImageSizes.Key(key, v.MaxSide), v.WebP, "image/webp", ct);
                var asset = new MediaAsset(shop.OwnerId, MediaKind.Image, "product", Buckets.Products, key, "image/webp",
                    processed.Variants.Sum(v => (long)v.WebP.Length), processed.Width, processed.Height, null, now);
                db.MediaAssets.Add(asset);
                media.Add(new MediaSpec(MediaType.Image, asset.Id, storage.PublicUrl(Buckets.Products, ImageSizes.Key(key, ImageSizes.Large)), null));
            }

            var product = new Product(shop.Id);
            product.SetInfo(leaf.Id, null, p.Name, Slug.From(p.Name), p.Description, ProductCondition.New, p.WeightG, 250, 200, 100, false, 0);
            product.SetAttributes(attributes);
            var tiers = p.Variant is null ? new List<TierSpec>() : [new TierSpec(p.Variant.Tier, p.Variant.Options.Select(o => new OptionSpec(o, null)).ToList())];
            var deltas = product.SetVariants(tiers, p.Skus.Select(s => new SkuSpec(s.Option, null, null, s.Price, s.OriginalPrice, s.Stock, null, true)).ToList());
            product.SetMedia(media);
            product.SubmitForReview(now, null);
            product.Approve(now);
            db.Products.Add(product);
            foreach (var (sku, delta) in deltas)
                db.InventoryMovements.Add(new InventoryMovement(sku.Id, delta, 0, InventoryReason.Seed, "seed", product.Id, null, "Dữ liệu gieo", now));
            created++;
        }
        await db.SaveChangesAsync(ct);
        if (created > 0) logger.LogInformation("Seeded {Count} sample product(s)", created);
    }

    private static Category ResolvePath(IReadOnlyList<Category> all, string path)
    {
        Category? current = null;
        foreach (var name in path.Split('/'))
            current = all.FirstOrDefault(c => c.Name == name && c.ParentId == current?.Id)
                ?? throw new InvalidOperationException($"Không tìm thấy danh mục \"{path}\" trong dữ liệu gieo.");
        return current!;
    }

    private static async Task<T> ReadAsync<T>(string name, CancellationToken ct)
    {
        await using var stream = typeof(CatalogSeeder).Assembly.GetManifestResourceStream(Resource + name)
            ?? throw new InvalidOperationException($"Thiếu tài nguyên {name}.");
        return await JsonSerializer.DeserializeAsync<T>(stream, Json, ct) ?? throw new InvalidOperationException($"Tài nguyên {name} rỗng.");
    }

    private static async Task<byte[]> ReadBytesAsync(string name, CancellationToken ct)
    {
        await using var stream = typeof(CatalogSeeder).Assembly.GetManifestResourceStream(Resource + name)
            ?? throw new InvalidOperationException($"Thiếu tài nguyên {name}.");
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    // ---------- Seed file shape ----------

    private sealed record CatalogSeed(
        List<string> OriginOptions,
        Dictionary<string, List<AttributeSeed>> AttributeSets,
        List<CategorySeed> Categories,
        List<string> Brands,
        List<ShopSeed> Shops,
        List<ProductSeed> Products);

    private sealed record AttributeSeed(string Name, AttributeInputType Type, List<string>? Options, string? Unit, bool Required, bool Filterable);

    private sealed record CategorySeed(string Name, string? Icon, int CommissionBp, string AttributeSet, List<CategoryChildSeed> Children);

    private sealed record CategoryChildSeed(string Name, List<CategoryLeafSeed> Children);

    private sealed record CategoryLeafSeed(string Name);

    private sealed record ShopSeed(string Name, ShopType Type, bool Mall, bool Preferred, int Owner, string Description, WarehouseSeed Warehouse);

    private sealed record WarehouseSeed(string Province, string District, string Ward);

    private sealed record ProductSeed(int LegacyId, string Name, string Category, string Shop, string Description, int WeightG,
        Dictionary<string, List<string>> Attributes, VariantSeed? Variant, List<SkuSeed> Skus);

    private sealed record VariantSeed(string Tier, List<string> Options);

    private sealed record SkuSeed(string? Option, long Price, long OriginalPrice, int Stock);
}
