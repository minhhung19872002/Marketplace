using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Media;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Media;
using ShopHub.Domain.Shops;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Seed;

/// <summary>
/// The sample catalogue: ~1,000 listings (~2,500 SKUs) built from Seed/Data/product-models.json — 188 real models, each
/// with its own studio photos, placed in the leaf category it shows (never another item's photo). A model is sold by
/// several shops of its industry, like on a real marketplace: each listing gets a natural name (the model's name, plus
/// one shop wording that fits the industry), its own price, discount and stock. Deterministic (fixed random seed);
/// products go through the same domain rules as a seller's (SetVariants, attribute checks, submit → approve).
/// </summary>
public sealed class ProductGenerator(ShopHubDbContext db, IObjectStorage storage, IImageProcessor images, IClock clock, ILogger<ProductGenerator> logger)
{
    public const int Target = 1000;

    private const string Resource = "ShopHub.Infrastructure.Seed.Data.";

    // Wording a shop adds to a model's name, per industry — only words that make sense for that kind of item.
    // "*" marks a second-hand listing (condition Used).
    private static readonly Dictionary<string, string[]> Wordings = new()
    {
        ["Điện Thoại & Phụ Kiện"] = ["Chính Hãng", "Bảo Hành 12 Tháng", "Nguyên Seal", "*Đã Qua Sử Dụng 99%", "Bản Quốc Tế"],
        ["Máy Tính & Laptop"] = ["Chính Hãng", "Bảo Hành 12 Tháng", "*Hàng Trưng Bày 99%", "Nguyên Seal"],
        ["Thiết Bị Điện Tử"] = ["Chính Hãng", "Bảo Hành 12 Tháng", "Fullbox", "*Đã Qua Sử Dụng"],
        ["Đồng Hồ"] = ["Fullbox", "Chính Hãng", "Bảo Hành 2 Năm", "*Đã Qua Sử Dụng"],
        ["Thời Trang Nam"] = ["Form Rộng", "Vải Mềm Mát", "Phong Cách Hàn Quốc", "Hàng Thiết Kế", "Mẫu Mới"],
        ["Thời Trang Nữ"] = ["Phong Cách Hàn Quốc", "Hàng Thiết Kế", "Mẫu Mới", "Vải Mềm Mát"],
        ["Giày Dép Nam"] = ["Đế Êm", "Full Size", "Mẫu Mới", "Hàng Có Sẵn"],
        ["Giày Dép Nữ"] = ["Đế Êm", "Full Size", "Mẫu Mới", "Hàng Có Sẵn"],
        ["Túi Ví Nữ"] = ["Hàng Thiết Kế", "Mẫu Mới", "Hàng Có Sẵn"],
        ["Nhà Cửa & Đời Sống"] = ["Hàng Có Sẵn", "Cao Cấp", "Loại 1", "Giao Nhanh"],
        ["Sắc Đẹp"] = ["Chính Hãng", "Hàng Có Sẵn", "Date Mới"],
        ["Sức Khỏe"] = ["Chính Hãng", "Date Mới"],
        ["Thể Thao & Du Lịch"] = ["Chính Hãng", "Loại Tốt", "Hàng Có Sẵn"],
        ["Ô Tô & Xe Máy"] = ["Hàng Có Sẵn", "Giao Toàn Quốc"],
        ["Bách Hóa Online"] = ["Loại 1", "Hàng Mới Về", "Giao Nhanh"],
        ["Thú Cưng"] = ["Hàng Mới Về", "Date Mới"],
    };

    // How likely buyers browse an industry: more listings where marketplaces have many sellers
    private static readonly Dictionary<string, int> Popularity = new()
    {
        ["Điện Thoại & Phụ Kiện"] = 6, ["Thời Trang Nam"] = 6, ["Thời Trang Nữ"] = 6, ["Sắc Đẹp"] = 5, ["Nhà Cửa & Đời Sống"] = 3,
        ["Giày Dép Nam"] = 6, ["Giày Dép Nữ"] = 6, ["Túi Ví Nữ"] = 6, ["Máy Tính & Laptop"] = 5, ["Thiết Bị Điện Tử"] = 6,
        ["Đồng Hồ"] = 4, ["Thể Thao & Du Lịch"] = 3, ["Bách Hóa Online"] = 3, ["Ô Tô & Xe Máy"] = 2, ["Sức Khỏe"] = 4, ["Thú Cưng"] = 4,
    };

    /// <param name="shopSells">Shop name → the top-level industries it sells (from catalog-seed.json).</param>
    public async Task GenerateAsync(IReadOnlyDictionary<string, IReadOnlyList<string>> shopSells, CancellationToken ct)
    {
        var existing = await db.Products.IgnoreQueryFilters().CountAsync(ct);
        if (existing >= Target) return;
        var rng = new Random(2026 + existing);
        var now = clock.UtcNow;

        var models = await ReadAsync<List<ModelSeed>>("product-models.json", ct);
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var shops = await db.Shops.AsNoTracking().Where(s => s.Status == ShopStatus.Active).OrderBy(s => s.Name).ToListAsync(ct);
        if (shops.Count == 0) return;
        var brands = await db.Brands.AsNoTracking().ToDictionaryAsync(b => b.Name, b => b.Id, ct);
        var attributes = await db.CategoryAttributes.AsNoTracking().ToListAsync(ct);
        var usedNames = await db.Products.IgnoreQueryFilters().Select(p => new { p.ShopId, p.Name }).ToListAsync(ct);
        var taken = usedNames.Select(x => (x.ShopId, x.Name)).ToHashSet();

        // Photos: processed once per model (3 WebP sizes in MinIO) and shared by every listing of the model
        var owner = shops[0].OwnerId;
        var photos = new Dictionary<int, List<(Guid Asset, string Url)>>();
        foreach (var model in models)
        {
            var list = new List<(Guid, string)>();
            foreach (var file in model.Images)
            {
                var key = $"product/seed/{Path.GetFileNameWithoutExtension(file)}";
                var bytes = await ReadBytesAsync($"ProductImages.{file}", ct);
                var processed = images.Process(bytes, ImageSizes.Public);
                foreach (var v in processed.Variants)
                    await storage.PutAsync(Buckets.Products, ImageSizes.Key(key, v.MaxSide), v.WebP, "image/webp", ct);
                var asset = new MediaAsset(owner, MediaKind.Image, "product", Buckets.Products, key, "image/webp",
                    processed.Variants.Sum(v => (long)v.WebP.Length), processed.Width, processed.Height, null, now);
                db.MediaAssets.Add(asset);
                list.Add((asset.Id, storage.PublicUrl(Buckets.Products, ImageSizes.Key(key, ImageSizes.Large))));
            }
            photos[model.Key] = list;
            // ~450 photos decode / resize / encode in a row: flush and collect every few models so the seed stays well
            // under the API container's memory limit (Skia's native buffers do not press the GC on their own)
            if (photos.Count % 20 == 0)
            {
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                GC.Collect();
            }
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // Every model once (a Mall shop of its industry first), then more listings weighted by industry popularity
        var plan = new List<(ModelSeed Model, int Copy)>();
        var copies = models.ToDictionary(m => m.Key, _ => 0);
        foreach (var model in models) plan.Add((model, copies[model.Key]++));
        var weighted = models.SelectMany(m => Enumerable.Repeat(m, Popularity.GetValueOrDefault(Top(m), 3))).ToList();
        while (plan.Count < Target - existing)
        {
            var model = weighted[rng.Next(weighted.Count)];
            if (copies[model.Key] >= 8) continue;
            plan.Add((model, copies[model.Key]++));
        }

        var created = 0;
        foreach (var (model, copy) in plan)
        {
            var top = Top(model);
            var leaf = ResolvePath(categories, model.Category);
            var sellers = shops.Where(s => shopSells.GetValueOrDefault(s.Name)?.Contains(top) == true).ToList();
            if (sellers.Count == 0) sellers = shops;
            var malls = sellers.Where(s => s.Type == ShopType.Mall).ToList();
            var shop = copy == 0 && malls.Count > 0 ? malls[rng.Next(malls.Count)] : sellers[rng.Next(sellers.Count)];

            var wordings = Wordings.GetValueOrDefault(top) ?? [];
            string name;
            var used = false;
            var attempt = 0;
            do
            {
                var wording = copy == 0 && attempt == 0 || wordings.Length == 0 ? null : wordings[rng.Next(wordings.Length)];
                used = wording?.StartsWith('*') == true;
                name = wording is null ? model.Name : $"{model.Name} - {wording.TrimStart('*')}";
                if (name.Length > Product.MaxNameLength) name = name[..Product.MaxNameLength].TrimEnd();
                attempt++;
            } while (taken.Contains((shop.Id, name)) && attempt < 12);
            if (taken.Contains((shop.Id, name))) continue;
            taken.Add((shop.Id, name));

            var product = new Product(shop.Id);
            var brandId = shop.Type == ShopType.Mall && shop.Name.StartsWith("Mall ", StringComparison.Ordinal)
                ? brands.GetValueOrDefault(shop.Name["Mall ".Length..]) : (Guid?)null;
            product.SetInfo(leaf.Id, brandId == Guid.Empty ? null : brandId, name, Slug.From(name),
                Description(model, top, shop.Name, used), used ? ProductCondition.Used : ProductCondition.New, model.WeightG, 200, 150, 80, false, 0);

            // The model's attributes; every other required attribute of the leaf gets a valid value
            var values = new List<(Guid, IReadOnlyList<string>)>();
            foreach (var a in attributes.Where(a => a.CategoryId == leaf.Id))
            {
                IReadOnlyList<string>? v = a.Name == "Xuất xứ" ? [model.Origin] : model.Attributes.GetValueOrDefault(a.Name);
                if (v is null)
                {
                    if (!a.IsRequired) continue;
                    v = a.InputType switch
                    {
                        AttributeInputType.SingleSelect or AttributeInputType.MultiSelect => [a.Options.Contains("Khác") ? "Khác" : a.Options[0]],
                        AttributeInputType.Number => ["12"],
                        _ => ["Khác"],
                    };
                }
                values.Add((a.Id, v));
            }
            product.SetAttributes(values);

            // Price around the model's list price; most listings run a discount
            var price = RoundPrice((long)(model.Price * (0.9 + rng.NextDouble() * 0.22)));
            var discount = rng.Next(10) < 6 ? 5 + rng.Next(36) : 0;
            if (used) price = RoundPrice(price * 7 / 10);
            long Original(long p) => discount == 0 ? p : RoundPrice(p * 100 / (100 - discount));
            int Stock() => 10 + rng.Next(390);
            var soldOut = rng.Next(25) == 0;
            var tiers = new List<TierSpec>();
            var skus = new List<SkuSpec>();
            if (model.Variant is { } variant)
            {
                tiers.Add(new TierSpec(variant.Tier, variant.Options.Select(o => new OptionSpec(o.Name, null)).ToList()));
                foreach (var o in variant.Options)
                {
                    var p = o.Delta is { } delta ? RoundPrice(price + delta) : RoundPrice(price * 2 * 95 / 100);
                    skus.Add(new SkuSpec(o.Name, null, null, p, Original(p), soldOut ? 0 : Stock(), null, true));
                }
            }
            else
            {
                skus.Add(new SkuSpec(null, null, null, price, Original(price), soldOut ? 0 : Stock(), null, true));
            }
            var deltas = product.SetVariants(tiers, skus);

            // Covers differ between listings of a model: the photo order turns with the copy number
            var shots = photos[model.Key];
            var media = shots.Skip(copy % shots.Count).Concat(shots.Take(copy % shots.Count))
                .Select(p => new MediaSpec(MediaType.Image, p.Asset, p.Url, null)).ToList();
            product.SetMedia(media);
            product.SubmitForReview(now, null);
            product.Approve(now.AddMinutes(-rng.Next(60 * 24 * 90)));

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
        logger.LogInformation("Generated {Count} sample product(s) from {Models} models", created, models.Count);
    }

    private static string Top(ModelSeed m) => m.Category.Split('/')[0];

    private static string Description(ModelSeed m, string top, string shop, bool used)
    {
        var lines = new List<string>
        {
            $"<p><strong>{m.Name}</strong> — {(used ? "hàng đã qua sử dụng, ngoại hình đẹp, đã kiểm tra kỹ trước khi giao" : "hàng mới, đóng gói nguyên vẹn")}.</p>",
            "<ul>",
        };
        foreach (var (k, v) in m.Attributes) lines.Add($"<li>{k}: {string.Join(", ", v)}{(k is "Khối lượng" ? " g" : k is "Hạn sử dụng" ? " ngày" : "")}</li>");
        lines.Add($"<li>Xuất xứ: {m.Origin}</li>");
        if (m.Variant is { } v2) lines.Add($"<li>{v2.Tier}: {string.Join(" / ", v2.Options.Select(o => o.Name))}</li>");
        lines.Add("</ul>");
        lines.Add(top switch
        {
            "Bách Hóa Online" or "Thú Cưng" => "<p>Bảo quản nơi khô ráo, thoáng mát. Hàng tươi giao trong ngày tại nội thành.</p>",
            "Thời Trang Nam" or "Thời Trang Nữ" => "<p>Giặt tay hoặc giặt máy ở chế độ nhẹ, không dùng chất tẩy. Bảng size chi tiết trong ảnh, inbox shop để được tư vấn.</p>",
            "Điện Thoại & Phụ Kiện" or "Máy Tính & Laptop" or "Thiết Bị Điện Tử" => "<p>Kiểm tra máy khi nhận hàng. Lỗi phần cứng do nhà sản xuất được đổi mới trong 7 ngày.</p>",
            _ => "<p>Đổi trả trong 7 ngày nếu sản phẩm lỗi do nhà sản xuất.</p>",
        });
        lines.Add($"<p>{shop} cảm ơn bạn đã tin chọn!</p>");
        return string.Join("", lines);
    }

    private static Category ResolvePath(IReadOnlyList<Category> all, string path)
    {
        Category? current = null;
        foreach (var name in path.Split('/'))
            current = all.FirstOrDefault(c => c.Name == name && c.ParentId == current?.Id)
                ?? throw new InvalidOperationException($"Không tìm thấy danh mục \"{path}\" trong dữ liệu gieo.");
        return current!;
    }

    private static long RoundPrice(long price) => price >= 1_000_000 ? Math.Max(10_000, (price + 5_000) / 10_000 * 10_000) : Math.Max(1_000, (price + 500) / 1_000 * 1_000);

    private static async Task<T> ReadAsync<T>(string name, CancellationToken ct)
    {
        await using var stream = typeof(ProductGenerator).Assembly.GetManifestResourceStream(Resource + name)
            ?? throw new InvalidOperationException($"Thiếu tài nguyên {name}.");
        return await JsonSerializer.DeserializeAsync<T>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct)
               ?? throw new InvalidOperationException($"Tài nguyên {name} rỗng.");
    }

    private static async Task<byte[]> ReadBytesAsync(string name, CancellationToken ct)
    {
        await using var stream = typeof(ProductGenerator).Assembly.GetManifestResourceStream(Resource + name)
            ?? throw new InvalidOperationException($"Thiếu tài nguyên {name}.");
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    private sealed record ModelSeed(int Key, string Category, string Name, long Price, VariantSeed? Variant,
        Dictionary<string, List<string>> Attributes, string Origin, int WeightG, List<string> Images);

    private sealed record VariantSeed(string Tier, List<VariantOptionSeed> Options);

    private sealed record VariantOptionSeed(string Name, long? Delta);
}
