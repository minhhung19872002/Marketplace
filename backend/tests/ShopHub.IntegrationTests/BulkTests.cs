using System.Net;
using System.Net.Http.Headers;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Features.Seller;
using ShopHub.Application.Security;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Shops;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>III.3: đăng hàng loạt và cập nhật giá / tồn hàng loạt bằng Excel, chạy nền, bảng lỗi theo dòng.</summary>
[Collection(ApiCollection.Name)]
public class BulkTests(ApiFactory factory)
{
    [Fact]
    public async Task An_import_sheet_creates_products_with_variants_and_images_and_lists_bad_rows_and_never_runs_twice()
    {
        var store = await factory.CreateStoreAsync();
        var seller = await StaffAsync(store, ShopPermissions.All);
        var leaf = await factory.WithDbAsync(db => db.Categories.SingleAsync(c => c.Name == "Đèn Bàn" && c.Level == 3));
        var attributes = await factory.WithDbAsync(db => db.CategoryAttributes.Where(a => a.CategoryId == leaf.Id).ToListAsync());

        var template = await seller.GetAsync($"/api/seller/shops/{store.ShopId}/bulk/template?categoryId={leaf.Id}");
        template.StatusCode.Should().Be(HttpStatusCode.OK, await template.Content.ReadAsStringAsync());
        var file = Fill(await template.Content.ReadAsByteArrayAsync(), sheet =>
        {
            var marker = store.Marker;
            Row(sheet, 3, attributes, new()
            {
                [BulkColumns.Group] = "G1", [BulkColumns.Name] = $"Đèn Ngủ Nhập Excel {marker}", [BulkColumns.Description] = "Đèn ngủ ánh sáng vàng",
                [BulkColumns.Weight] = "350", [BulkColumns.Images] = "https://img.test/a.png; https://img.test/b.png", [BulkColumns.Tier1] = "Màu",
                [BulkColumns.Option1] = "Trắng", [BulkColumns.Price] = "150000", [BulkColumns.OriginalPrice] = "180000", [BulkColumns.Stock] = "5",
                [BulkColumns.SellerSku] = "DN-TRANG", [BulkColumns.MaxPerBuyer] = "3",
            });
            Row(sheet, 4, attributes, new() { [BulkColumns.Group] = "G1", [BulkColumns.Option1] = "Đen", [BulkColumns.Price] = "160000", [BulkColumns.Stock] = "3" });
            Row(sheet, 5, attributes, new()
            {
                [BulkColumns.Name] = $"Đèn Thiếu Giá {marker}", [BulkColumns.Description] = "x", [BulkColumns.Weight] = "300",
                [BulkColumns.Images] = "https://img.test/c.png", [BulkColumns.Stock] = "2",
            });
            Row(sheet, 6, attributes, new()
            {
                [BulkColumns.Name] = $"Đèn Ảnh Quá Lớn {marker}", [BulkColumns.Description] = "x", [BulkColumns.Weight] = "300",
                [BulkColumns.Images] = "https://big.test/c.png", [BulkColumns.Price] = "99000", [BulkColumns.Stock] = "2",
            });
            Row(sheet, 7, attributes, new()
            {
                [BulkColumns.Name] = $"Đèn Link Http {marker}", [BulkColumns.Description] = "x", [BulkColumns.Weight] = "300",
                [BulkColumns.Images] = "http://img.test/c.png", [BulkColumns.Price] = "99000", [BulkColumns.Stock] = "2",
            });
        });

        // The upload only queues the work
        var start = await UploadAsync(seller, store.ShopId, "ProductImport", file);
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
        var task = (await start.ReadEnvelopeAsync()).Data;
        task.Str("status").Should().Be("Queued");
        var taskId = Guid.Parse(task.Str("id"));

        (await RunAsync(taskId)).Should().BeTrue();
        (await RunAsync(taskId)).Should().BeFalse("a task is claimed once — running it again creates nothing twice");

        var done = (await (await seller.GetAsync($"/api/seller/shops/{store.ShopId}/bulk/tasks/{taskId}")).ReadEnvelopeAsync()).Data;
        done.Str("status").Should().Be("Done");
        done.GetProperty("total").GetInt32().Should().Be(4);
        done.GetProperty("succeeded").GetInt32().Should().Be(1);
        done.GetProperty("failed").GetInt32().Should().Be(3);
        var errors = done.GetProperty("errors").EnumerateArray().Select(e => (Row: e.GetProperty("row").GetInt32(), Column: e.GetProperty("column").GetString(),
            Message: e.Str("message"))).ToList();
        errors.Should().Contain(e => e.Row == 5 && e.Column == BulkColumns.Price);
        errors.Should().Contain(e => e.Row == 6 && e.Column == BulkColumns.Images && e.Message.Contains("5 MB"));
        errors.Should().Contain(e => e.Row == 7 && e.Column == BulkColumns.Images && e.Message.Contains("https"));

        // The good group became one product with two SKUs, both images, the limit — and went to review
        var product = await factory.WithDbAsync(db => db.Products.Include(p => p.Skus).Include(p => p.Media)
            .SingleAsync(p => p.ShopId == store.ShopId && p.Name == $"Đèn Ngủ Nhập Excel {store.Marker}"));
        product.Status.Should().Be(ProductStatus.PendingReview);
        product.Skus.Select(s => s.Price).Should().BeEquivalentTo([150_000L, 160_000L]);
        product.Media.Should().HaveCount(2);
        product.MaxPerBuyer.Should().Be(3);
        (await factory.WithDbAsync(db => db.Products.CountAsync(p => p.ShopId == store.ShopId && p.Name.Contains("Thiếu Giá")))).Should().Be(0);
    }

    [Fact]
    public async Task The_price_and_stock_sheet_updates_skus_with_stock_history_and_refuses_other_files_and_other_rights()
    {
        var store = await factory.CreateStoreAsync(products: [new("Bàn Phím Giá Tồn", "Đèn Bàn", 300_000, 10, "Việt Nam"), new("Chuột Giá Tồn", "Đèn Bàn", 120_000, 10, "Việt Nam")]);
        var seller = await StaffAsync(store, ShopPermissions.All);
        var export = await seller.GetAsync($"/api/seller/shops/{store.ShopId}/bulk/price-stock");
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        var keyboard = store.Skus["Bàn Phím Giá Tồn"];
        var mouse = store.Skus["Chuột Giá Tồn"];
        var file = Fill(await export.Content.ReadAsByteArrayAsync(), sheet =>
        {
            foreach (var row in sheet.RowsUsed().Skip(1))
            {
                if (row.Cell(1).GetString() == keyboard.ToString())
                {
                    row.Cell(5).Value = 280_000;
                    row.Cell(7).Value = 25;
                }
                if (row.Cell(1).GetString() == mouse.ToString()) row.Cell(7).Value = "nhiều";
            }
        });

        // A price sheet is not an import template (and vice versa), and the right is checked
        (await UploadAsync(seller, store.ShopId, "ProductImport", file)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var noRights = await StaffAsync(store, [ShopPermissions.ProductView]);
        (await UploadAsync(noRights, store.ShopId, "PriceStockUpdate", file)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var other = await factory.CreateStoreAsync();
        var otherSeller = await StaffAsync(other, ShopPermissions.All);
        (await UploadAsync(otherSeller, other.ShopId, "PriceStockUpdate", file)).StatusCode.Should().Be(HttpStatusCode.Conflict, "a sheet of another shop");

        var start = await UploadAsync(seller, store.ShopId, "PriceStockUpdate", file);
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
        var taskId = Guid.Parse((await start.ReadEnvelopeAsync()).Data.Str("id"));
        await RunAsync(taskId);

        var done = (await (await seller.GetAsync($"/api/seller/shops/{store.ShopId}/bulk/tasks/{taskId}")).ReadEnvelopeAsync()).Data;
        done.Str("status").Should().Be("Done");
        done.GetProperty("failed").GetInt32().Should().Be(1);
        done.Str("message").Should().Contain("Đã cập nhật 1 SKU");
        var sku = await factory.WithDbAsync(db => db.Skus.AsNoTracking().SingleAsync(s => s.Id == keyboard));
        sku.Price.Should().Be(280_000);
        sku.Stock.Should().Be(25);
        (await factory.WithDbAsync(db => db.InventoryMovements.CountAsync(m => m.SkuId == keyboard && m.DeltaStock == 15))).Should().Be(1);
        (await factory.WithDbAsync(db => db.Skus.Where(s => s.Id == mouse).Select(s => s.Stock).SingleAsync())).Should().Be(10);
    }

    // ---------- helpers ----------

    private async Task<HttpClient> StaffAsync(TestStore store, IReadOnlyCollection<string> grants)
    {
        var user = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new ShopStaff(store.ShopId, user.Id, ShopStaffRole.Manager, grants));
            await db.SaveChangesAsync();
        });
        return user.Client;
    }

    private async Task<bool> RunAsync(Guid taskId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BulkTaskRunner>().RunAsync(taskId, CancellationToken.None);
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid shopId, string kind, byte[] file)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(content, "file", "tep.xlsx");
        return client.PostAsync($"/api/seller/shops/{shopId}/bulk/{kind}", form);
    }

    private static byte[] Fill(byte[] workbook, Action<IXLWorksheet> fill)
    {
        using var book = new XLWorkbook(new MemoryStream(workbook));
        fill(book.Worksheet("Dữ liệu"));
        using var output = new MemoryStream();
        book.SaveAs(output);
        return output.ToArray();
    }

    // Writes the given cells by header; required attributes of the category get a valid value on rows that start a product
    private static void Row(IXLWorksheet sheet, int row, IReadOnlyList<CategoryAttribute> attributes, Dictionary<string, string> cells)
    {
        var headers = sheet.Row(1).CellsUsed().ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber);
        if (cells.ContainsKey(BulkColumns.Name))
            foreach (var a in attributes.Where(a => a.IsRequired))
                cells.TryAdd(BulkColumns.Attribute(a), a.Name == "Xuất xứ" ? "Việt Nam" : a.Options.FirstOrDefault() ?? "12");
        foreach (var (header, value) in cells) sheet.Cell(row, headers[header]).Value = value;
    }
}
