using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Storefront;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Shops;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Persistence;
using ShopHub.Infrastructure.Search;

namespace ShopHub.Infrastructure.Seed;

/// <summary>
/// SH_SEED=perf (spec 6.3, 7): grows the catalogue to SH_PERF_PRODUCTS (default 1.000.000) active products for
/// measuring search and page latency. Rows go in with binary COPY (products, one SKU and one image each) spread over
/// 200 shops and every leaf category, then straight into Meilisearch in batches. Never part of a normal start-up;
/// re-running only adds what is missing.
/// </summary>
public sealed class PerfSeeder(ShopHubDbContext db, MeiliClient meili, ShopHubSettings settings, IClock clock, ILogger<PerfSeeder> logger)
{
    private const string Marker = "Hiệu Năng";
    private const int Shops = 200;
    private const int Batch = 20_000;

    private static readonly string[] Nouns =
    [
        "Áo thun", "Áo sơ mi", "Quần jean", "Váy", "Giày thể thao", "Dép", "Túi xách", "Balo", "Đồng hồ", "Kính mát", "Điện thoại", "Ốp lưng",
        "Tai nghe", "Sạc dự phòng", "Cáp sạc", "Loa bluetooth", "Bàn phím", "Chuột", "Màn hình", "Laptop", "Nồi chiên", "Ấm siêu tốc", "Máy xay",
        "Chảo chống dính", "Bình giữ nhiệt", "Gối", "Chăn", "Đèn ngủ", "Son môi", "Kem chống nắng", "Sữa rửa mặt", "Nước hoa", "Bóng đá",
        "Vợt cầu lông", "Thảm yoga", "Xe đẩy", "Bỉm", "Sữa bột", "Đồ chơi", "Sách",
    ];

    private static readonly string[] Adjectives =
    [
        "cao cấp", "chính hãng", "giá rẻ", "phong cách Hàn", "mini", "siêu nhẹ", "chống nước", "thời trang", "basic", "unisex", "đa năng",
        "bền đẹp", "xịn", "hàng mới", "nhập khẩu", "thông minh", "cotton", "inox 304", "không dây", "sang trọng",
    ];

    private static readonly string[] Colors = ["đen", "trắng", "xanh", "đỏ", "hồng", "be", "xám", "vàng", "nâu", "tím"];

    public async Task SeedAsync(CancellationToken ct)
    {
        var target = settings.PerfProducts;
        if (target <= 0) return;
        var sw = Stopwatch.StartNew();
        var leaves = await db.Categories.AsNoTracking().Where(c => c.Level == 3 && c.IsActive).Select(c => c.Id).ToListAsync(ct);
        var brands = await db.Brands.AsNoTracking().Select(b => new { b.Id, b.Name }).ToListAsync(ct);
        if (leaves.Count == 0) throw new InvalidOperationException("SH_SEED=perf cần cây danh mục (chạy cùng dữ liệu mẫu).");
        var shops = await EnsureShopsAsync(ct);
        var have = await db.Products.CountAsync(p => shops.Select(s => s.Id).Contains(p.ShopId), ct);
        if (have >= target)
        {
            logger.LogInformation("PERF catalogue already has {Count} products", have);
            return;
        }

        var categoryChain = await CategoryChainsAsync(ct);
        var rng = new Random(20261006 + have);
        var now = clock.UtcNow;
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
        var pending = new List<long>();

        for (var done = have; done < target; done += Batch)
        {
            var count = Math.Min(Batch, target - done);
            var rows = new List<ProductSearchDocument>(count);
            var children = new List<(Guid Id, int N, long Price, long Original, int Stock, string Image)>(count);
            // A batch is all or nothing: never products without their SKU
            await using var tx = await connection.BeginTransactionAsync(ct);
            await using (var products = await connection.BeginBinaryImportAsync(
                             "COPY catalog.products (id, shop_id, category_id, brand_id, name, slug, description, status, condition, weight_g, length_mm, width_mm, " +
                             "height_mm, is_preorder, preorder_days, min_price, max_price, sold_count, rating_avg, rating_count, like_count, view_count, published_at, " +
                             "submitted_at, created_at) FROM STDIN (FORMAT BINARY)", ct))
            {
                for (var i = 0; i < count; i++)
                {
                    var n = done + i;
                    var shop = shops[n % shops.Count];
                    var category = leaves[rng.Next(leaves.Count)];
                    var brand = rng.Next(4) == 0 && brands.Count > 0 ? brands[rng.Next(brands.Count)] : null;
                    var name = $"{Nouns[rng.Next(Nouns.Length)]} {Adjectives[rng.Next(Adjectives.Length)]} {Colors[rng.Next(Colors.Length)]}{(brand is null ? "" : $" {brand.Name}")} {Marker} {n:D7}";
                    if (name.Length > 120) name = name[..120];
                    var slug = Slug.From(name);
                    if (slug.Length > 120) slug = slug[..120];
                    var price = (long)rng.Next(2, 2_000) * 1_000;
                    var original = price + (rng.Next(3) == 0 ? price / 5 / 1_000 * 1_000 : 0);
                    var sold = rng.Next(0, 5_000);
                    var ratingCount = rng.Next(0, 400);
                    var rating = ratingCount == 0 ? 0 : Math.Round(3 + rng.NextDouble() * 2, 1);
                    var published = now.AddMinutes(-rng.Next(0, 365 * 24 * 60));
                    var id = Guid.NewGuid();
                    var image = $"https://picsum.photos/seed/sh{n}/600/600";
                    var stock = rng.Next(0, 300);

                    await products.StartRowAsync(ct);
                    await products.WriteAsync(id, NpgsqlDbType.Uuid, ct);
                    await products.WriteAsync(shop.Id, NpgsqlDbType.Uuid, ct);
                    await products.WriteAsync(category, NpgsqlDbType.Uuid, ct);
                    if (brand is null) await products.WriteNullAsync(ct);
                    else await products.WriteAsync(brand.Id, NpgsqlDbType.Uuid, ct);
                    await products.WriteAsync(name, NpgsqlDbType.Varchar, ct);
                    await products.WriteAsync(slug, NpgsqlDbType.Varchar, ct);
                    await products.WriteAsync($"<p>{System.Net.WebUtility.HtmlEncode(name)} — sản phẩm dữ liệu đo hiệu năng.</p>", NpgsqlDbType.Text, ct);
                    await products.WriteAsync("Active", NpgsqlDbType.Varchar, ct);
                    await products.WriteAsync("New", NpgsqlDbType.Varchar, ct);
                    await products.WriteAsync(rng.Next(100, 3_000), NpgsqlDbType.Integer, ct);
                    await products.WriteAsync(0, NpgsqlDbType.Integer, ct);
                    await products.WriteAsync(0, NpgsqlDbType.Integer, ct);
                    await products.WriteAsync(0, NpgsqlDbType.Integer, ct);
                    await products.WriteAsync(false, NpgsqlDbType.Boolean, ct);
                    await products.WriteAsync(0, NpgsqlDbType.Integer, ct);
                    await products.WriteAsync(price, NpgsqlDbType.Bigint, ct);
                    await products.WriteAsync(price, NpgsqlDbType.Bigint, ct);
                    await products.WriteAsync(sold, NpgsqlDbType.Integer, ct);
                    await products.WriteAsync(rating, NpgsqlDbType.Double, ct);
                    await products.WriteAsync(ratingCount, NpgsqlDbType.Integer, ct);
                    await products.WriteAsync(0, NpgsqlDbType.Integer, ct);
                    await products.WriteAsync(0, NpgsqlDbType.Integer, ct);
                    await products.WriteAsync(published, NpgsqlDbType.TimestampTz, ct);
                    await products.WriteAsync(published, NpgsqlDbType.TimestampTz, ct);
                    await products.WriteAsync(published, NpgsqlDbType.TimestampTz, ct);

                    children.Add((id, n, price, original, stock, image));

                    var chain = categoryChain[category];
                    rows.Add(new ProductSearchDocument(id.ToString(), name, Slug.Fold(name), slug, image, price, price, original,
                        ProductCards.DiscountPercent(price, original), rating, (int)Math.Floor(rating), ratingCount, sold, stock > 0,
                        shop.Id.ToString(), shop.Name, Slug.Fold(shop.Name), false, false, shop.ProvinceCode, shop.ProvinceName,
                        category.ToString(), chain.Ids, chain.Folded, brand?.Id.ToString(), brand?.Name, "New", [], published.ToUnixTimeSeconds()));
                }
                await products.CompleteAsync(ct);
            }
            // One COPY at a time per connection (Npgsql): the SKUs and images follow their products
            await using (var skus = await connection.BeginBinaryImportAsync(
                             "COPY catalog.skus (id, product_id, seller_sku, price, original_price, stock, reserved, is_active) FROM STDIN (FORMAT BINARY)", ct))
            {
                foreach (var c in children)
                {
                    await skus.StartRowAsync(ct);
                    await skus.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
                    await skus.WriteAsync(c.Id, NpgsqlDbType.Uuid, ct);
                    await skus.WriteAsync($"PERF-{c.N:D7}", NpgsqlDbType.Varchar, ct);
                    await skus.WriteAsync(c.Price, NpgsqlDbType.Bigint, ct);
                    await skus.WriteAsync(c.Original, NpgsqlDbType.Bigint, ct);
                    await skus.WriteAsync(c.Stock, NpgsqlDbType.Integer, ct);
                    await skus.WriteAsync(0, NpgsqlDbType.Integer, ct);
                    await skus.WriteAsync(true, NpgsqlDbType.Boolean, ct);
                }
                await skus.CompleteAsync(ct);
            }
            await using (var media = await connection.BeginBinaryImportAsync(
                             "COPY catalog.product_media (id, product_id, type, url, sort_order) FROM STDIN (FORMAT BINARY)", ct))
            {
                foreach (var c in children)
                {
                    await media.StartRowAsync(ct);
                    await media.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
                    await media.WriteAsync(c.Id, NpgsqlDbType.Uuid, ct);
                    await media.WriteAsync("Image", NpgsqlDbType.Varchar, ct);
                    await media.WriteAsync(c.Image, NpgsqlDbType.Varchar, ct);
                    await media.WriteAsync(0, NpgsqlDbType.Integer, ct);
                }
                await media.CompleteAsync(ct);
            }
            await tx.CommitAsync(ct);
            pending.Add(await meili.EnqueueAsync(MeiliClient.ProductsIndex, rows, ct));
            logger.LogInformation("PERF {Done}/{Target} products written ({Seconds:N0} s)", done + count, target, sw.Elapsed.TotalSeconds);
        }

        logger.LogInformation("PERF waiting for Meilisearch to index {Batches} batch(es)…", pending.Count);
        await meili.WaitForAsync(pending[^1], TimeSpan.FromHours(3), ct);
        await db.Database.ExecuteSqlRawAsync("ANALYZE catalog.products; ANALYZE catalog.skus;", ct);
        logger.LogInformation("PERF catalogue ready: {Target} products in {Minutes:N1} min", target, sw.Elapsed.TotalMinutes);
    }

    private sealed record PerfShop(Guid Id, string Name, string ProvinceCode, string? ProvinceName);

    private sealed record Chain(IReadOnlyList<string> Ids, string Folded);

    private async Task<Dictionary<Guid, Chain>> CategoryChainsAsync(CancellationToken ct)
    {
        var all = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId, c.Name }).ToDictionaryAsync(c => c.Id, ct);
        return all.Keys.ToDictionary(id => id, id =>
        {
            var ids = new List<string>();
            var names = new List<string>();
            for (var cur = all.GetValueOrDefault(id); cur is not null; cur = cur.ParentId is { } p ? all.GetValueOrDefault(p) : null)
            {
                ids.Add(cur.Id.ToString());
                names.Add(cur.Name);
            }
            return new Chain(ids, Slug.Fold(string.Join(' ', names)));
        });
    }

    private async Task<List<PerfShop>> EnsureShopsAsync(CancellationToken ct)
    {
        var provinces = await db.AdminDivisions.AsNoTracking().Where(d => d.ParentCode == null).OrderBy(d => d.Code).Select(d => new { d.Code, d.Name }).ToListAsync(ct);
        var existing = await db.Shops.AsNoTracking().Where(s => s.Name.StartsWith($"Shop {Marker}")).CountAsync(ct);
        var now = clock.UtcNow;
        for (var i = existing; i < Shops; i++)
        {
            var owner = User.Register(null, $"perf-owner-{i:D3}@shophub.local", "!perf-no-login", $"Chủ Shop {Marker} {i:D3}", now);
            db.Users.Add(owner);
            var name = $"Shop {Marker} {i:D3}";
            var shop = new Shop(owner.Id, name, Slug.From(name), ShopType.Business);
            shop.Approve(now);
            db.Shops.Add(shop);
            db.ShopStaff.Add(new ShopStaff(shop.Id, owner.Id, ShopStaffRole.Owner, Application.Security.ShopPermissions.All));
            var province = provinces[i % provinces.Count];
            var district = await db.AdminDivisions.AsNoTracking().Where(d => d.ParentCode == province.Code).OrderBy(d => d.Code).Select(d => d.Code).FirstAsync(ct);
            var ward = await db.AdminDivisions.AsNoTracking().Where(d => d.ParentCode == district).OrderBy(d => d.Code).Select(d => d.Code).FirstAsync(ct);
            var w = new ShopWarehouse(shop.Id);
            w.Update("Kho", "Kho", "0900000000", province.Code, district, ward, "1 Đường Hiệu Năng", true, true);
            db.ShopWarehouses.Add(w);
        }
        await db.SaveChangesAsync(ct);
        var shops = await (from s in db.Shops.AsNoTracking()
                           where s.Name.StartsWith($"Shop {Marker}")
                           join w in db.ShopWarehouses.AsNoTracking() on s.Id equals w.ShopId
                           join d in db.AdminDivisions.AsNoTracking() on w.ProvinceCode equals d.Code
                           orderby s.Name
                           select new PerfShop(s.Id, s.Name, w.ProvinceCode, d.Name)).ToListAsync(ct);
        return shops.Select(s => s with { ProvinceName = ProductCards.ShortProvince(s.ProvinceName) }).ToList();
    }
}
