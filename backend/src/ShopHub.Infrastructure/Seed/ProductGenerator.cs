using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Shops;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Seed;

/// <summary>
/// Tops the sample catalogue up to ~1,000 products (~4,000 SKUs) across every leaf category, deterministically
/// (fixed random seed). Products go through the same domain rules as a seller's (SetVariants, attribute checks,
/// submit → approve); images reuse the already-processed seed photos of the same industry.
/// </summary>
public sealed class ProductGenerator(ShopHubDbContext db, IClock clock, ILogger<ProductGenerator> logger)
{
    public const int Target = 1000;

    private static readonly string[] Qualifiers =
        ["Cao Cấp", "Chính Hãng", "Giá Tốt", "Phong Cách Hàn Quốc", "Mẫu Mới 2026", "Siêu Bền", "Thời Trang", "Tiện Lợi", "Nhập Khẩu", "Bán Chạy", "Phiên Bản Đặc Biệt", "Basic"];

    private static readonly string[] Suffixes = ["", "", "Loại 1", "Size Lớn", "Màu Mới", "Combo 2", "Hàng Có Sẵn", "Freesize"];

    // Price band (VND) per top-level industry
    private static readonly Dictionary<string, (long Min, long Max)> PriceBands = new()
    {
        ["Thời Trang Nam"] = (59_000, 899_000), ["Thời Trang Nữ"] = (69_000, 1_200_000), ["Điện Thoại & Phụ Kiện"] = (29_000, 15_000_000),
        ["Máy Tính & Laptop"] = (49_000, 25_000_000), ["Thiết Bị Điện Tử"] = (79_000, 5_000_000), ["Máy Ảnh & Quay Phim"] = (99_000, 30_000_000),
        ["Đồng Hồ"] = (99_000, 6_000_000), ["Giày Dép Nam"] = (59_000, 1_900_000), ["Giày Dép Nữ"] = (59_000, 1_900_000),
        ["Túi Ví Nữ"] = (79_000, 2_500_000), ["Mẹ & Bé"] = (39_000, 3_000_000), ["Nhà Cửa & Đời Sống"] = (29_000, 4_000_000),
        ["Sắc Đẹp"] = (39_000, 1_500_000), ["Sức Khỏe"] = (59_000, 2_000_000), ["Thể Thao & Du Lịch"] = (49_000, 3_000_000),
        ["Ô Tô & Xe Máy"] = (39_000, 3_500_000), ["Đồ Chơi"] = (29_000, 2_000_000), ["Bách Hóa Online"] = (15_000, 600_000),
    };

    private static readonly Dictionary<string, (string Tier, string[] Options)?> VariantByIndustry = new()
    {
        ["Thời Trang Nam"] = ("Kích Cỡ", ["S", "M", "L", "XL"]), ["Thời Trang Nữ"] = ("Kích Cỡ", ["S", "M", "L"]),
        ["Giày Dép Nam"] = ("Size", ["39", "40", "41", "42", "43"]), ["Giày Dép Nữ"] = ("Size", ["35", "36", "37", "38", "39"]),
        ["Điện Thoại & Phụ Kiện"] = ("Màu Sắc", ["Đen", "Trắng", "Xanh"]), ["Máy Tính & Laptop"] = ("Màu Sắc", ["Đen", "Bạc"]),
        ["Thiết Bị Điện Tử"] = ("Màu Sắc", ["Đen", "Trắng", "Xanh", "Hồng"]), ["Đồng Hồ"] = ("Màu Dây", ["Đen", "Nâu", "Bạc"]),
        ["Túi Ví Nữ"] = ("Màu Sắc", ["Đen", "Be", "Nâu", "Đỏ"]), ["Thể Thao & Du Lịch"] = ("Màu Sắc", ["Đen", "Xanh", "Cam"]),
        ["Sắc Đẹp"] = ("Loại", ["Mini", "Tiêu chuẩn"]), ["Ô Tô & Xe Máy"] = ("Size", ["M", "L", "XL"]),
        ["Máy Ảnh & Quay Phim"] = null, ["Mẹ & Bé"] = null, ["Nhà Cửa & Đời Sống"] = ("Màu Sắc", ["Trắng", "Xám"]),
        ["Sức Khỏe"] = null, ["Đồ Chơi"] = null, ["Bách Hóa Online"] = null,
    };

    public async Task GenerateAsync(CancellationToken ct)
    {
        var existing = await db.Products.IgnoreQueryFilters().CountAsync(ct);
        if (existing >= Target) return;
        var rng = new Random(2026 + existing);
        var now = clock.UtcNow;

        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var parents = categories.Select(c => c.ParentId).Where(p => p is not null).ToHashSet();
        var leaves = categories.Where(c => !parents.Contains(c.Id) && c.Level == 3).OrderBy(c => c.Name).ToList();
        string TopOf(Category c)
        {
            var cur = c;
            while (cur.ParentId is { } pid) cur = categories.First(x => x.Id == pid);
            return cur.Name;
        }

        var shops = await db.Shops.AsNoTracking().Where(s => s.Status == ShopStatus.Active).OrderBy(s => s.Name).ToListAsync(ct);
        if (shops.Count == 0 || leaves.Count == 0) return;
        var brands = await db.Brands.AsNoTracking().ToDictionaryAsync(b => b.Name, b => b.Id, ct);
        var attributes = await db.CategoryAttributes.AsNoTracking().ToListAsync(ct);

        // Seed photos grouped by industry, to reuse on generated products of the same industry
        var photos = await (from p in db.Products.AsNoTracking()
                            from m in p.Media
                            where m.Type == MediaType.Image && m.SortOrder == 0
                            select new { p.CategoryId, m.Url, m.AssetId }).ToListAsync(ct);
        var photosByTop = photos.GroupBy(x => TopOf(categories.First(c => c.Id == x.CategoryId))).ToDictionary(g => g.Key, g => g.ToList());
        var anyPhotos = photos.ToList();

        var toCreate = Target - existing;
        var created = 0;
        for (var i = 0; created < toCreate; i++)
        {
            var leaf = leaves[i % leaves.Count];
            var top = TopOf(leaf);
            var shop = shops[rng.Next(shops.Count)];
            var name = $"{leaf.Name} {Qualifiers[rng.Next(Qualifiers.Length)]} {Suffixes[rng.Next(Suffixes.Length)]}".Trim();
            if (name.Length > Product.MaxNameLength) name = name[..Product.MaxNameLength];

            var product = new Product(shop.Id);
            var brandId = shop.Type == ShopType.Mall && shop.Name.StartsWith("Mall ", StringComparison.Ordinal)
                ? brands.GetValueOrDefault(shop.Name["Mall ".Length..]) : (Guid?)null;
            product.SetInfo(leaf.Id, brandId == Guid.Empty ? null : brandId, name, Slug.From(name),
                $"<p><strong>{name}</strong> — hàng mới về tại {shop.Name}.</p><ul><li>Đổi trả trong 7 ngày nếu lỗi do nhà sản xuất.</li><li>Hỗ trợ thanh toán khi nhận hàng.</li></ul>",
                rng.Next(10) == 0 ? ProductCondition.Used : ProductCondition.New, 100 + rng.Next(3000), 200, 150, 80, false, 0);

            // Valid values for every attribute of the leaf (required ones always filled)
            var values = new List<(Guid, IReadOnlyList<string>)>();
            foreach (var a in attributes.Where(a => a.CategoryId == leaf.Id))
            {
                if (!a.IsRequired && rng.Next(3) == 0) continue;
                IReadOnlyList<string> v = a.InputType switch
                {
                    AttributeInputType.SingleSelect => [a.Options[rng.Next(a.Options.Count)]],
                    AttributeInputType.MultiSelect => a.Options.OrderBy(_ => rng.Next()).Take(1 + rng.Next(Math.Min(2, a.Options.Count))).ToList(),
                    AttributeInputType.Number => [(1 + rng.Next(48)).ToString()],
                    _ => ["Khác"],
                };
                values.Add((a.Id, v));
            }
            product.SetAttributes(values);

            var (lo, hi) = PriceBands.GetValueOrDefault(top, (49_000L, 999_000L));
            var basePrice = RoundPrice(lo + (long)(rng.NextDouble() * rng.NextDouble() * (hi - lo)));
            var discount = rng.Next(4) == 0 ? 0 : 5 + rng.Next(50);
            var original = RoundPrice(basePrice * 100 / (100 - discount));
            var variant = VariantByIndustry.GetValueOrDefault(top);
            var tiers = new List<TierSpec>();
            var skus = new List<SkuSpec>();
            int Stock() => rng.Next(20) == 0 ? 0 : 5 + rng.Next(300);
            if (variant is { } v2 && rng.Next(5) > 0)
            {
                tiers.Add(new TierSpec(v2.Tier, v2.Options.Select(o => new OptionSpec(o, null)).ToList()));
                foreach (var o in v2.Options)
                    skus.Add(new SkuSpec(o, null, null, basePrice, Math.Max(original, basePrice), Stock(), null, true));
            }
            else
            {
                skus.Add(new SkuSpec(null, null, null, basePrice, Math.Max(original, basePrice), Stock(), null, true));
            }
            var deltas = product.SetVariants(tiers, skus);

            var pool = photosByTop.GetValueOrDefault(top) ?? anyPhotos;
            var photo = pool[rng.Next(pool.Count)];
            product.SetMedia([new MediaSpec(MediaType.Image, photo.AssetId, photo.Url, null)]);
            product.SubmitForReview(now, null);
            product.Approve(now.AddMinutes(-rng.Next(60 * 24 * 60)));

            db.Products.Add(product);
            foreach (var (sku, delta) in deltas)
                db.InventoryMovements.Add(new InventoryMovement(sku.Id, delta, 0, InventoryReason.Seed, "seed", product.Id, null, "Dữ liệu gieo", now));
            created++;

            if (created % 100 == 0)
            {
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        logger.LogInformation("Generated {Count} sample product(s)", created);
    }

    private static long RoundPrice(long price) => Math.Max(1000, (price + 500) / 1000 * 1000);
}
