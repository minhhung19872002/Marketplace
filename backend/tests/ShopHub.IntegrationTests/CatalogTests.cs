using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Security;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Shops;
using ShopHub.IntegrationTests.Infrastructure;
using SkiaSharp;

namespace ShopHub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CatalogTests(ApiFactory factory)
{
    // ---------- helpers ----------

    private static byte[] Image(SKEncodedImageFormat format = SKEncodedImageFormat.Png, int w = 400, int h = 300)
    {
        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(new SKColor((byte)Random.Shared.Next(256), 120, 200));
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(format, 90).ToArray();
    }

    // JPEG with an APP1/Exif segment carrying a fake GPS tag, inserted right after SOI
    private static byte[] JpegWithExif()
    {
        var jpeg = Image(SKEncodedImageFormat.Jpeg);
        var payload = Encoding.ASCII.GetBytes("Exif\0\0MM\0*GPSLatitude=10.7769;GPSLongitude=106.7009");
        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        segment[2] = (byte)((payload.Length + 2) >> 8);
        segment[3] = (byte)((payload.Length + 2) & 0xFF);
        payload.CopyTo(segment, 4);
        return [.. jpeg[..2], .. segment, .. jpeg[2..]];
    }

    private static async Task<JsonElement> UploadAsync(HttpClient client, string purpose, byte[] data, string fileName = "anh.png",
        string contentType = "image/png", HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        var res = await client.PostAsync($"/api/media/{purpose}", form);
        res.StatusCode.Should().Be(expected, await res.Content.ReadAsStringAsync());
        return (await res.ReadEnvelopeAsync()).Data;
    }

    private async Task<Guid> LeafAsync(string name) =>
        await factory.WithDbAsync(db => db.Categories.Where(c => c.Name == name && c.Level == 3).Select(c => c.Id).SingleAsync());

    private async Task<Dictionary<string, Guid>> AttributesAsync(Guid categoryId) =>
        await factory.WithDbAsync(db => db.CategoryAttributes.Where(a => a.CategoryId == categoryId).ToDictionaryAsync(a => a.Name, a => a.Id));

    /// <summary>Owner applies through the API (personal KYC), an admin approves.</summary>
    private async Task<(TestUser Owner, Guid ShopId)> ApprovedShopAsync()
    {
        var owner = await factory.CreateUserAsync();
        var shopId = await RegisterShopAsync(owner, $"Shop Thử {Guid.NewGuid():N}"[..20]);
        var admin = await factory.CreateUserAsync(Permissions.ShopReview);
        (await admin.Client.PostAsync($"/api/admin/shops/{shopId}/approve", null)).EnsureSuccessStatusCode();
        return (owner, shopId);
    }

    private async Task<Guid> RegisterShopAsync(TestUser owner, string name, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var front = (await UploadAsync(owner.Client, "kyc", Image())).Str("id");
        var back = (await UploadAsync(owner.Client, "kyc", Image())).Str("id");
        var res = await owner.Client.PostAsJsonAsync("/api/seller/shops", new
        {
            name,
            type = "Personal",
            description = "Shop thử nghiệm",
            warehouse = new { contactName = "Kho", phone = "0912345678", provinceCode = "01", districtCode = "001", wardCode = "00001", street = "1 Phố Thử" },
            personal = new { legalName = "Nguyễn Văn Thử", idCardNumber = "001200012345", frontAssetId = front, backAssetId = back },
            bank = new { bankCode = "VCB", accountNo = "0011002233445", accountName = "Nguyen Van Thu" },
        });
        res.StatusCode.Should().Be(expected, await res.Content.ReadAsStringAsync());
        return expected == HttpStatusCode.OK ? Guid.Parse((await res.ReadEnvelopeAsync()).Data.GetString()!) : Guid.Empty;
    }

    private async Task<object> ShirtInputAsync(HttpClient client, string name = "Áo Thun Thử Nghiệm", string[]? sizes = null, string description = "<p>Mô tả</p>")
    {
        var leaf = await LeafAsync("Áo Thun");
        var attrs = await AttributesAsync(leaf);
        var image = (await UploadAsync(client, "product", Image())).Str("id");
        sizes ??= ["S", "M", "L"];
        var colors = new[] { "Đen", "Trắng" };
        return new
        {
            categoryId = leaf,
            brandId = (Guid?)null,
            name,
            description,
            condition = "New",
            weightG = 300,
            lengthMm = 300,
            widthMm = 200,
            heightMm = 30,
            isPreorder = false,
            preorderDays = 0,
            attributes = new[]
            {
                new { attributeId = attrs["Xuất xứ"], values = new[] { "Việt Nam" } },
                new { attributeId = attrs["Chất liệu"], values = new[] { "Cotton" } },
            },
            media = new[] { new { assetId = image, optionValue = (string?)null } },
            tiers = new object[]
            {
                new { name = "Màu", options = colors.Select(c => new { value = c, imageAssetId = (Guid?)null }) },
                new { name = "Size", options = sizes.Select(s => new { value = s, imageAssetId = (Guid?)null }) },
            },
            skus = (from c in colors
                    from s in sizes
                    select new { option1 = c, option2 = s, sellerSku = $"AT-{c}-{s}", price = s == "L" ? 129000L : 99000L, originalPrice = 150000L, stock = 10, weightG = (int?)null })
                .ToArray(),
        };
    }

    // ---------- media ----------

    [Fact]
    public async Task Uploaded_image_is_reencoded_to_three_webp_sizes_and_is_actually_downloadable()
    {
        var user = await factory.CreateUserAsync();

        var asset = await UploadAsync(user.Client, "product", Image());

        using var http = new HttpClient();
        foreach (var url in new[] { asset.Str("url"), asset.Str("thumbnailUrl") })
        {
            var res = await http.GetAsync(url);
            res.StatusCode.Should().Be(HttpStatusCode.OK, url);
            res.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
            var bytes = await res.Content.ReadAsByteArrayAsync();
            Encoding.ASCII.GetString(bytes, 8, 4).Should().Be("WEBP");
        }
    }

    [Fact]
    public async Task Exif_and_gps_metadata_are_removed()
    {
        var user = await factory.CreateUserAsync();

        var asset = await UploadAsync(user.Client, "product", JpegWithExif(), "anh.jpg", "image/jpeg");

        var bytes = await new HttpClient().GetByteArrayAsync(asset.Str("url"));
        Encoding.ASCII.GetString(bytes).Should().NotContain("Exif").And.NotContain("GPSLatitude");
    }

    [Fact]
    public async Task File_type_comes_from_the_bytes_not_the_declared_content_type()
    {
        var user = await factory.CreateUserAsync();

        // A text file pretending to be a JPEG is refused; a real PNG declared as "application/octet-stream" is accepted
        await UploadAsync(user.Client, "product", Encoding.UTF8.GetBytes("<?php echo 1; ?>"), "anh.jpg", "image/jpeg", HttpStatusCode.BadRequest);
        await UploadAsync(user.Client, "product", Image(), "file.bin", "application/octet-stream");
    }

    [Fact]
    public async Task Avatar_is_limited_to_one_megabyte()
    {
        var user = await factory.CreateUserAsync();
        // A big noisy PNG (> 1 MB)
        using var bmp = new SKBitmap(1200, 1200);
        for (var y = 0; y < 1200; y++)
            for (var x = 0; x < 1200; x++) bmp.SetPixel(x, y, new SKColor((byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256)));
        var big = SKImage.FromBitmap(bmp).Encode(SKEncodedImageFormat.Png, 100).ToArray();
        big.Length.Should().BeGreaterThan(1024 * 1024);

        await UploadAsync(user.Client, "avatar", big, expected: HttpStatusCode.BadRequest);
        var ok = await UploadAsync(user.Client, "avatar", Image());
        var set = await user.Client.PutAsJsonAsync("/api/account/avatar", new { assetId = ok.Str("id") });

        set.StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await user.Client.GetAsync("/api/account/me")).ReadEnvelopeAsync()).Data.Str("avatarUrl").Should().EndWith("_600.webp");
    }

    [Fact]
    public async Task Kyc_documents_have_no_public_url_and_the_bucket_refuses_anonymous_reads()
    {
        var user = await factory.CreateUserAsync();

        var asset = await UploadAsync(user.Client, "kyc", Image());
        var key = await factory.WithDbAsync(db => db.MediaAssets.Where(a => a.Id == Guid.Parse(asset.Str("id"))).Select(a => a.ObjectKey).SingleAsync());
        var anonymous = await new HttpClient().GetAsync($"{factory.MediaBaseUrl}/sh-kyc/{key}_1600.webp");

        asset.GetProperty("url").ValueKind.Should().Be(JsonValueKind.Null);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Someone_elses_upload_cannot_be_attached()
    {
        var a = await factory.CreateUserAsync();
        var b = await factory.CreateUserAsync();
        var asset = await UploadAsync(a.Client, "avatar", Image());

        var steal = await b.Client.PutAsJsonAsync("/api/account/avatar", new { assetId = asset.Str("id") });

        steal.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------- shop registration & review ----------

    [Fact]
    public async Task Shop_application_kyc_review_and_owner_notification()
    {
        var owner = await factory.CreateUserAsync();
        var shopId = await RegisterShopAsync(owner, $"Shop Duyệt {Random.Shared.Next(100000)}");
        var mine = (await (await owner.Client.GetAsync("/api/seller/shops")).ReadEnvelopeAsync()).Data;
        var admin = await factory.CreateUserAsync(Permissions.ShopView, Permissions.ShopReview);

        var detail = (await (await admin.Client.GetAsync($"/api/admin/shops/{shopId}")).ReadEnvelopeAsync()).Data;
        var kyc = detail.GetProperty("kyc");
        var signed = await new HttpClient().GetAsync(kyc.Str("idCardFrontUrl"));
        (await admin.Client.PostAsync($"/api/admin/shops/{shopId}/approve", null)).EnsureSuccessStatusCode();
        await factory.DispatchOutboxAsync();

        mine.EnumerateArray().Should().ContainSingle(s => s.Str("id") == shopId.ToString() && s.Str("status") == "PendingReview");
        kyc.Str("idCardNumberMasked").Should().Be("********2345", "số CCCD chỉ hiện 4 số cuối");
        signed.StatusCode.Should().Be(HttpStatusCode.OK, "URL ký có hạn mở được ảnh trong bucket riêng tư");
        (await factory.WithDbAsync(db => db.Shops.Where(s => s.Id == shopId).Select(s => s.Status).SingleAsync())).Should().Be(ShopStatus.Active);
        var bank = await factory.WithDbAsync(db => db.ShopBankAccounts.SingleAsync(b => b.ShopId == shopId));
        bank.AccountNoEncrypted.Should().StartWith("v1:").And.NotContain("0011002233445");
        bank.AccountNoLast4.Should().Be("3445");
        (await factory.WithDbAsync(db => db.SimulatedSms.AnyAsync(s => s.To == owner.Phone && s.Content.Contains("da duoc duyet")))).Should().BeTrue();
    }

    [Fact]
    public async Task Shop_names_are_unique_and_rejection_needs_a_reason()
    {
        var a = await factory.CreateUserAsync();
        var b = await factory.CreateUserAsync();
        var name = $"Shop Trùng {Random.Shared.Next(100000)}";
        var shopId = await RegisterShopAsync(a, name);
        await RegisterShopAsync(b, name, HttpStatusCode.Conflict);
        var admin = await factory.CreateUserAsync(Permissions.ShopReview);

        var noReason = await admin.Client.PostAsJsonAsync($"/api/admin/shops/{shopId}/reject", new { reason = "" });
        var reject = await admin.Client.PostAsJsonAsync($"/api/admin/shops/{shopId}/reject", new { reason = "Ảnh CCCD mờ" });

        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        reject.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------- products ----------

    [Fact]
    public async Task Two_tier_product_lifecycle_from_draft_to_selling()
    {
        var (owner, shopId) = await ApprovedShopAsync();
        var input = await ShirtInputAsync(owner.Client);

        var created = await owner.Client.PostAsJsonAsync($"/api/seller/shops/{shopId}/products", input);
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var productId = Guid.Parse((await created.ReadEnvelopeAsync()).Data.GetString()!);
        var detail = (await (await owner.Client.GetAsync($"/api/seller/shops/{shopId}/products/{productId}")).ReadEnvelopeAsync()).Data;
        (await owner.Client.PostAsync($"/api/seller/shops/{shopId}/products/{productId}/actions/submit", null)).EnsureSuccessStatusCode();
        var reviewer = await factory.CreateUserAsync(Permissions.ProductReview);
        var queue = (await (await reviewer.Client.GetAsync("/api/admin/products?status=PendingReview&pageSize=100")).ReadEnvelopeAsync()).Data;
        (await reviewer.Client.PostAsync($"/api/admin/products/{productId}/approve", null)).EnsureSuccessStatusCode();

        detail.Str("status").Should().Be("Draft");
        detail.GetProperty("skus").GetArrayLength().Should().Be(6, "2 màu × 3 size");
        detail.GetProperty("categoryPath").EnumerateArray().Select(e => e.GetString()).Should().Equal("Thời Trang Nam", "Áo", "Áo Thun");
        queue.GetProperty("items").EnumerateArray().Should().Contain(r => r.Str("id") == productId.ToString());
        var product = await factory.WithDbAsync(db => db.Products.SingleAsync(p => p.Id == productId));
        product.Status.Should().Be(ProductStatus.Active);
        product.MinPrice.Should().Be(99000);
        product.MaxPrice.Should().Be(129000);
        (await factory.WithDbAsync(db => db.InventoryMovements.CountAsync(m => m.RefId == productId))).Should().Be(6);
    }

    [Fact]
    public async Task Required_attributes_and_leaf_category_are_enforced()
    {
        var (owner, shopId) = await ApprovedShopAsync();
        var leaf = await LeafAsync("Áo Thun");
        var image = (await UploadAsync(owner.Client, "product", Image())).Str("id");
        var nonLeaf = await factory.WithDbAsync(db => db.Categories.Where(c => c.Name == "Thời Trang Nam").Select(c => c.Id).SingleAsync());

        object Body(Guid categoryId) => new
        {
            categoryId, name = "Thiếu thuộc tính", description = "", condition = "New", weightG = 100, lengthMm = 0, widthMm = 0, heightMm = 0,
            isPreorder = false, preorderDays = 0, attributes = Array.Empty<object>(),
            media = new[] { new { assetId = image, optionValue = (string?)null } }, tiers = Array.Empty<object>(),
            skus = new[] { new { option1 = (string?)null, option2 = (string?)null, price = 10000L, originalPrice = 10000L, stock = 1 } },
        };

        var missing = await owner.Client.PostAsJsonAsync($"/api/seller/shops/{shopId}/products", Body(leaf));
        var notLeaf = await owner.Client.PostAsJsonAsync($"/api/seller/shops/{shopId}/products", Body(nonLeaf));

        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missing.ReadEnvelopeAsync()).Errors.Select(e => e.Message).Should().Contain(["Vui lòng nhập Xuất xứ.", "Vui lòng nhập Chất liệu."]);
        notLeaf.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await notLeaf.ReadEnvelopeAsync()).Errors.Should().Contain(e => e.Message == "Vui lòng chọn danh mục cấp cuối.");
    }

    [Fact]
    public async Task Description_html_is_sanitised()
    {
        var (owner, shopId) = await ApprovedShopAsync();
        var input = await ShirtInputAsync(owner.Client, description: "<p onclick=\"alert(1)\">Đẹp</p><script>alert('x')</script><img src=x onerror=alert(2)>");

        var id = (await (await owner.Client.PostAsJsonAsync($"/api/seller/shops/{shopId}/products", input)).ReadEnvelopeAsync()).Data.GetString();
        var stored = await factory.WithDbAsync(db => db.Products.Where(p => p.Id == Guid.Parse(id!)).Select(p => p.Description).SingleAsync());

        stored.Should().Contain("Đẹp").And.NotContain("script").And.NotContain("onclick").And.NotContain("onerror");
    }

    [Fact]
    public async Task Editing_keeps_sku_identity_and_sensitive_edits_need_re_review()
    {
        var (owner, shopId) = await ApprovedShopAsync();
        var input = await ShirtInputAsync(owner.Client);
        var productId = Guid.Parse((await (await owner.Client.PostAsJsonAsync($"/api/seller/shops/{shopId}/products", input)).ReadEnvelopeAsync()).Data.GetString()!);
        await factory.WithDbAsync(async db =>
        {
            var p = await db.Products.SingleAsync(x => x.Id == productId);
            p.SubmitForReview(DateTimeOffset.UtcNow, null);
            p.Approve(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        });
        var before = await factory.WithDbAsync(db => db.Skus.Where(s => s.ProductId == productId).Select(s => s.Id).ToListAsync());

        // Drop size S, add XL, rename the product
        var edited = await ShirtInputAsync(owner.Client, name: "Áo Thun Thử Nghiệm Bản Mới", sizes: ["M", "L", "XL"]);
        var res = await owner.Client.PutAsJsonAsync($"/api/seller/shops/{shopId}/products/{productId}", new { input = edited });

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var skus = await factory.WithDbAsync(db => db.Skus.Where(s => s.ProductId == productId).ToListAsync());
        skus.Should().HaveCount(8, "6 cũ (2 bị tắt) + 2 mới");
        skus.Count(s => s.IsActive).Should().Be(6);
        skus.Where(s => before.Contains(s.Id)).Should().HaveCount(6, "SKU cũ giữ nguyên định danh");
        (await factory.WithDbAsync(db => db.Products.Where(p => p.Id == productId).Select(p => p.Status).SingleAsync()))
            .Should().Be(ProductStatus.PendingReview, "đổi tên sản phẩm đã duyệt phải duyệt lại");
    }

    [Fact]
    public async Task Banned_keywords_flag_the_product_for_review()
    {
        var (owner, shopId) = await ApprovedShopAsync();
        var input = await ShirtInputAsync(owner.Client, name: "Áo Thun Hàng Fake Loại 1");
        var id = (await (await owner.Client.PostAsJsonAsync($"/api/seller/shops/{shopId}/products", input)).ReadEnvelopeAsync()).Data.GetString();

        (await owner.Client.PostAsync($"/api/seller/shops/{shopId}/products/{id}/actions/submit", null)).EnsureSuccessStatusCode();

        (await factory.WithDbAsync(db => db.Products.Where(p => p.Id == Guid.Parse(id!)).Select(p => p.Flags).SingleAsync()))
            .Should().Contain("hàng fake");
    }

    [Fact]
    public async Task Other_shops_products_are_404_and_missing_staff_permission_is_403()
    {
        var (ownerA, shopA) = await ApprovedShopAsync();
        var (ownerB, shopB) = await ApprovedShopAsync();
        var input = await ShirtInputAsync(ownerA.Client);
        var id = (await (await ownerA.Client.PostAsJsonAsync($"/api/seller/shops/{shopA}/products", input)).ReadEnvelopeAsync()).Data.GetString();

        var readOther = await ownerB.Client.GetAsync($"/api/seller/shops/{shopA}/products/{id}");
        var viaOwnShop = await ownerB.Client.GetAsync($"/api/seller/shops/{shopB}/products/{id}");
        var editOther = await ownerB.Client.PutAsJsonAsync($"/api/seller/shops/{shopA}/products/{id}", new { input });

        // A staff member of shop A who may only view
        var viewer = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new ShopStaff(shopA, viewer.Id, ShopStaffRole.CustomerService, [ShopPermissions.ProductView]));
            await db.SaveChangesAsync();
        });
        var viewerRead = await viewer.Client.GetAsync($"/api/seller/shops/{shopA}/products/{id}");
        var viewerCreate = await viewer.Client.PostAsJsonAsync($"/api/seller/shops/{shopA}/products", await ShirtInputAsync(viewer.Client));

        readOther.StatusCode.Should().Be(HttpStatusCode.NotFound);
        viaOwnShop.StatusCode.Should().Be(HttpStatusCode.NotFound);
        editOther.StatusCode.Should().Be(HttpStatusCode.NotFound);
        viewerRead.StatusCode.Should().Be(HttpStatusCode.OK);
        viewerCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------- inventory ----------

    private async Task<(TestUser Owner, Guid ShopId, Guid SkuId)> SkuWithStockAsync(int stock)
    {
        var (owner, shopId) = await ApprovedShopAsync();
        var input = await ShirtInputAsync(owner.Client);
        var productId = Guid.Parse((await (await owner.Client.PostAsJsonAsync($"/api/seller/shops/{shopId}/products", input)).ReadEnvelopeAsync()).Data.GetString()!);
        var skuId = await factory.WithDbAsync(db => db.Skus.Where(s => s.ProductId == productId).Select(s => s.Id).FirstAsync());
        await factory.WithDbAsync(db => db.Skus.Where(s => s.Id == skuId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Stock, stock)));
        return (owner, shopId, skuId);
    }

    [Fact]
    public async Task Parallel_stock_decrements_never_go_below_zero()
    {
        var (owner, shopId, skuId) = await SkuWithStockAsync(10);

        var results = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ =>
            factory.Authorized(owner.AccessToken).PostAsJsonAsync($"/api/seller/shops/{shopId}/skus/{skuId}/stock-adjustments", new { delta = -1, note = "Hỏng" })));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(10);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(15);
        (await factory.WithDbAsync(db => db.Skus.Where(s => s.Id == skuId).Select(s => s.Stock).SingleAsync())).Should().Be(0);
        (await factory.WithDbAsync(db => db.InventoryMovements.CountAsync(m => m.SkuId == skuId && m.Reason == InventoryReason.SellerAdjust))).Should().Be(10);
    }

    [Fact]
    public async Task A_stock_change_whose_history_row_cannot_be_saved_leaves_the_stock_unchanged()
    {
        var (owner, shopId, skuId) = await SkuWithStockAsync(10);
        // Make the movement insert fail for this SKU only (stands in for any failure after the stock UPDATE)
        var fn = $"test_fail_movement_{skuId:N}";
        // Identifiers cannot be parameters; both come from a Guid
        var create = $"""
            CREATE FUNCTION catalog.{fn}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.sku_id = '{skuId}' THEN RAISE EXCEPTION 'movement insert refused'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER {fn} BEFORE INSERT ON catalog.inventory_movements FOR EACH ROW EXECUTE FUNCTION catalog.{fn}();
            """;
        var drop = $"DROP TRIGGER {fn} ON catalog.inventory_movements; DROP FUNCTION catalog.{fn}();";
        await factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync(create));
        try
        {
            var res = await owner.Client.PostAsJsonAsync($"/api/seller/shops/{shopId}/skus/{skuId}/stock-adjustments", new { delta = 5, note = "Nhập hàng" });
            res.IsSuccessStatusCode.Should().BeFalse();
            (await factory.WithDbAsync(db => db.Skus.Where(s => s.Id == skuId).Select(s => s.Stock).SingleAsync()))
                .Should().Be(10, "tồn kho và dòng lịch sử phải cùng một giao dịch: không có dấu vết thì không được đổi");
        }
        finally
        {
            await factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync(drop));
        }
    }

    [Fact]
    public async Task Stock_cannot_drop_below_reserved_even_by_direct_sql()
    {
        var (owner, shopId, skuId) = await SkuWithStockAsync(10);
        await factory.WithDbAsync(db => db.Skus.Where(s => s.Id == skuId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Reserved, 6)));

        var api = await owner.Client.PostAsJsonAsync($"/api/seller/shops/{shopId}/skus/{skuId}/stock-adjustments", new { delta = -5 });
        var direct = async () => await factory.WithDbAsync(db => db.Skus.Where(s => s.Id == skuId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Stock, 3)));

        api.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await direct.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("ck_skus_stock");
    }

    [Fact]
    public async Task Quick_edit_validates_through_the_domain()
    {
        var (owner, shopId, skuId) = await SkuWithStockAsync(10);

        var bad = await owner.Client.PutAsJsonAsync($"/api/seller/shops/{shopId}/skus/{skuId}", new { price = 200000L, originalPrice = 100000L });
        var ok = await owner.Client.PutAsJsonAsync($"/api/seller/shops/{shopId}/skus/{skuId}", new { price = 80000L, stock = 25 });
        var history = (await (await owner.Client.GetAsync($"/api/seller/shops/{shopId}/skus/{skuId}/movements")).ReadEnvelopeAsync()).Data;

        bad.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await bad.ReadEnvelopeAsync()).Message.Should().Be("Giá gốc không được thấp hơn giá bán.");
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        history.GetProperty("items").EnumerateArray().Should().Contain(m => m.GetProperty("deltaStock").GetInt32() == 15);
    }

    // ---------- categories ----------

    [Fact]
    public async Task Category_tree_is_public_and_admin_cannot_create_cycles_or_a_fourth_level()
    {
        var tree = (await (await factory.CreateClient().GetAsync("/api/categories")).ReadEnvelopeAsync()).Data;
        var admin = await factory.CreateUserAsync(Permissions.CategoryManage);
        var top = await factory.WithDbAsync(db => db.Categories.Where(c => c.Name == "Đồ Chơi").Select(c => c.Id).SingleAsync());
        var leaf = await LeafAsync("Lego & Xếp Hình");

        var fourth = await admin.Client.PostAsJsonAsync("/api/admin/categories", new { parentId = leaf, name = "Cấp 4", sortOrder = 0, commissionRateBp = 500, isActive = true });
        var cycle = await admin.Client.PostAsJsonAsync("/api/admin/categories",
            new { id = top, parentId = leaf, name = "Đồ Chơi", sortOrder = 0, commissionRateBp = 600, isActive = true });

        tree.GetArrayLength().Should().Be(18);
        fourth.StatusCode.Should().Be(HttpStatusCode.Conflict);
        cycle.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Reject_needs_a_reason_and_ban_unban_round_trip()
    {
        var (owner, shopId) = await ApprovedShopAsync();
        var input = await ShirtInputAsync(owner.Client);
        var id = (await (await owner.Client.PostAsJsonAsync($"/api/seller/shops/{shopId}/products", input)).ReadEnvelopeAsync()).Data.GetString();
        (await owner.Client.PostAsync($"/api/seller/shops/{shopId}/products/{id}/actions/submit", null)).EnsureSuccessStatusCode();
        var admin = await factory.CreateUserAsync(Permissions.ProductReview, Permissions.ProductBan);

        var noReason = await admin.Client.PostAsJsonAsync($"/api/admin/products/{id}/reject", new { reason = "" });
        var reject = await admin.Client.PostAsJsonAsync($"/api/admin/products/{id}/reject", new { reason = "Ảnh bìa có chữ" });
        var ban = await admin.Client.PostAsJsonAsync($"/api/admin/products/{id}/ban", new { reason = "Hàng giả" });
        var sellerEdit = await owner.Client.PutAsJsonAsync($"/api/seller/shops/{shopId}/products/{id}", new { input });
        var unban = await admin.Client.PostAsync($"/api/admin/products/{id}/unban", null);

        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        reject.StatusCode.Should().Be(HttpStatusCode.OK);
        ban.StatusCode.Should().Be(HttpStatusCode.OK);
        sellerEdit.StatusCode.Should().Be(HttpStatusCode.Conflict, "sản phẩm bị khoá thì người bán không sửa được");
        (await unban.ReadEnvelopeAsync()).Data.GetString().Should().Be("Hidden");
    }

    [Fact]
    public async Task Registration_takes_the_chosen_carriers_and_a_bank_from_the_server_catalogue()
    {
        var banks = (await (await factory.CreateClient().GetAsync("/api/site/banks")).ReadEnvelopeAsync()).Data.EnumerateArray().Select(b => b.Str("code")).ToList();
        banks.Should().Contain(["VCB", "TCB", "BIDV"], "danh mục ngân hàng lấy từ máy chủ");
        var active = await factory.WithDbAsync(db => db.Carriers.Where(c => c.IsActive).OrderBy(c => c.SortOrder).Select(c => c.Code).ToListAsync());
        active.Count.Should().BeGreaterThan(1);
        var carriers = (await (await factory.CreateClient().GetAsync("/api/site/carriers")).ReadEnvelopeAsync()).Data.EnumerateArray().Select(c => c.Str("code")).ToList();
        carriers.Should().Equal(active);

        var owner = await factory.CreateUserAsync();
        var front = (await UploadAsync(owner.Client, "kyc", Image())).Str("id");
        var back = (await UploadAsync(owner.Client, "kyc", Image())).Str("id");
        object Body(string name, string bankCode, string[]? carrierCodes) => new
        {
            name, type = "Personal", description = "Shop thử nghiệm",
            warehouse = new { contactName = "Kho", phone = "0912345678", provinceCode = "01", districtCode = "001", wardCode = "00001", street = "1 Phố Thử" },
            personal = new { legalName = "Nguyễn Văn Thử", idCardNumber = "001200012345", frontAssetId = front, backAssetId = back },
            bank = new { bankCode, accountNo = "0011002233445", accountName = "Nguyen Van Thu" },
            carrierCodes,
        };
        var name = $"Shop Chọn Hãng {Guid.NewGuid():N}"[..24];
        (await owner.Client.PostAsJsonAsync("/api/seller/shops", Body(name, "XYZ", [active[0]]))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "ngân hàng ngoài danh mục");
        (await owner.Client.PostAsJsonAsync("/api/seller/shops", Body(name, "VCB", ["KHONG_CO"]))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "hãng không có / đang tắt");
        (await owner.Client.PostAsJsonAsync("/api/seller/shops", Body(name, "VCB", []))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "cần ít nhất một hãng");

        var ok = await owner.Client.PostAsJsonAsync("/api/seller/shops", Body(name, "VCB", [active[0]]));
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var shopId = Guid.Parse((await ok.ReadEnvelopeAsync()).Data.GetString()!);
        var channels = await factory.WithDbAsync(db => db.ShopShippingChannels.Where(c => c.ShopId == shopId).ToDictionaryAsync(c => c.CarrierCode, c => c.IsEnabled));
        channels.Should().Equal(active.ToDictionary(c => c, c => c == active[0]), "hãng được chọn bật, các hãng khác tắt");
    }
}
