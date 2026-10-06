using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Common;
using ShopHub.Application.Security;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Shops;

namespace ShopHub.IntegrationTests.Infrastructure;

public record SeedProduct(string Name, string Leaf, long Price, int Stock, string Origin, string? Material = null, int WeightG = 300);

/// <param name="Products">product name → product id</param>
/// <param name="Skus">product name → its (single) SKU id</param>
public record TestStore(Guid ShopId, Guid OwnerId, string Marker, Dictionary<string, Guid> Products, Dictionary<string, Guid> Skus);

public static class CommerceFixtures
{
    /// <summary>A fresh approved shop in the given province with active single-SKU products, pushed to the search index.</summary>
    public static async Task<TestStore> CreateStoreAsync(this ApiFactory factory, string province = "01", bool mall = false, params SeedProduct[] products)
    {
        var marker = $"Q{Guid.NewGuid():N}"[..8];
        var ids = new Dictionary<string, Guid>();
        var skus = new Dictionary<string, Guid>();
        Guid shopId = Guid.Empty, ownerId = Guid.Empty;
        await factory.WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var owner = User.Register(ApiFactory.NewPhone(), null, "x", "Chủ shop thử", now);
            db.Users.Add(owner);
            var shop = new Shop(owner.Id, $"Shop {marker}", Slug.From($"Shop {marker}"), ShopType.Business);
            shop.Approve(now);
            if (mall) shop.SetLabels(true, true);
            db.Shops.Add(shop);
            db.ShopStaff.Add(new ShopStaff(shop.Id, owner.Id, ShopStaffRole.Owner, ShopPermissions.All));
            var district = await db.AdminDivisions.Where(d => d.ParentCode == province).OrderBy(d => d.Code).FirstAsync();
            var ward = await db.AdminDivisions.Where(d => d.ParentCode == district.Code).OrderBy(d => d.Code).FirstAsync();
            var w = new ShopWarehouse(shop.Id);
            w.Update("Kho", "Kho", "0912345678", province, district.Code, ward.Code, "1 Đường Thử", true, true);
            db.ShopWarehouses.Add(w);

            foreach (var spec in products)
            {
                var leaf = await db.Categories.SingleAsync(c => c.Name == spec.Leaf && c.Level == 3);
                var attrs = await db.CategoryAttributes.Where(a => a.CategoryId == leaf.Id).ToListAsync();
                var p = new Product(shop.Id);
                var name = $"{spec.Name} {marker}";
                p.SetInfo(leaf.Id, null, name, Slug.From(name), "<p>Mô tả</p>", ProductCondition.New, spec.WeightG, 0, 0, 0, false, 0);
                var values = new List<(Guid, IReadOnlyList<string>)> { (attrs.Single(a => a.Name == "Xuất xứ").Id, [spec.Origin]) };
                foreach (var a in attrs.Where(a => a.IsRequired && a.Name != "Xuất xứ"))
                    values.Add((a.Id, a.Name == "Chất liệu" && spec.Material is not null ? [spec.Material] : [a.Options.FirstOrDefault() ?? "12"]));
                p.SetAttributes(values);
                p.SetVariants([], [new SkuSpec(null, null, null, spec.Price, spec.Price + 10_000, spec.Stock, null, true)]);
                p.SetMedia([new MediaSpec(MediaType.Image, null, "https://example.invalid/a.webp", null)]);
                p.SubmitForReview(now, null);
                p.Approve(now);
                db.Products.Add(p);
                ids[spec.Name] = p.Id;
                skus[spec.Name] = p.Skus.Single().Id;
            }
            await db.SaveChangesAsync();
            shopId = shop.Id;
            ownerId = owner.Id;
        });
        await factory.DispatchOutboxAsync();
        return new TestStore(shopId, ownerId, marker, ids, skus);
    }

    /// <summary>A default delivery address for the user in the given province.</summary>
    public static Task<Guid> AddAddressAsync(this ApiFactory factory, Guid userId, string province = "79") =>
        factory.WithDbAsync(async db =>
        {
            var existing = await db.Addresses.Where(a => a.UserId == userId && a.ProvinceCode == province).Select(a => (Guid?)a.Id).FirstOrDefaultAsync();
            if (existing is { } id) return id;
            var district = await db.AdminDivisions.Where(d => d.ParentCode == province).OrderBy(d => d.Code).FirstAsync();
            var ward = await db.AdminDivisions.Where(d => d.ParentCode == district.Code).OrderBy(d => d.Code).FirstAsync();
            var a = new Address(userId);
            a.Update("Người Nhận Thử", "0901234567", province, district.Code, ward.Code, "12 Đường Nhận", null, null, AddressType.Home);
            a.SetDefault(!await db.Addresses.AnyAsync(x => x.UserId == userId));
            db.Addresses.Add(a);
            await db.SaveChangesAsync();
            return a.Id;
        });

    public static Task<Voucher> CreateVoucherAsync(this ApiFactory factory, VoucherOwner owner, Guid? shopId, VoucherType type,
        long value = 0, int percentBp = 0, long? max = null, long minOrder = 0, int? quota = null, int perUser = 1, string? code = null) =>
        factory.WithDbAsync(async db =>
        {
            var v = new Voucher(owner, shopId, code ?? $"T{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Voucher thử");
            v.Configure(type, value, percentBp, max, minOrder, VoucherAudience.Everyone, [], [], DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddDays(10), quota, perUser, true, VoucherChannel.All);
            db.Vouchers.Add(v);
            await db.SaveChangesAsync();
            return v;
        });

    public static Task GrantCoinsAsync(this ApiFactory factory, Guid userId, long coins) =>
        factory.WithDbAsync(async db =>
        {
            db.CoinLedger.Add(new CoinEntry(userId, coins, CoinReason.AdminGrant, "test", null, DateTimeOffset.UtcNow.AddDays(30), "Xu thử", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });
}
