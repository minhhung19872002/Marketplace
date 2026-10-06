using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Media;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Common;
using ShopHub.Domain.SystemConfig;

namespace ShopHub.Application.Features.Seller;

/// <summary>
/// Runs one bulk task: claims it with a conditional UPDATE (a task never runs twice), acts as its owner, sends the
/// regular commands row by row (one product = one group of rows), records progress every few rows and the row errors.
/// </summary>
public sealed class BulkTaskRunner(IApplicationDbContext db, ISender sender, ActingUser acting, IProductSheets sheets, IRemoteImageFetcher images,
    IClock clock, ILogger<BulkTaskRunner> logger)
{
    private const int SaveEvery = 10;

    /// <summary>Tasks queued for more than a minute (their Hangfire job was lost): run them now.</summary>
    public async Task<int> RunPendingAsync(CancellationToken ct)
    {
        var stale = clock.UtcNow.AddMinutes(-1);
        var ids = await db.BackgroundTasks.AsNoTracking().Where(t => t.Status == BackgroundTaskStatus.Queued && t.CreatedAt < stale)
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Select(t => t.Id).Take(5).ToListAsync(ct);
        var ran = 0;
        foreach (var id in ids)
            if (await RunAsync(id, ct)) ran++;
        return ran;
    }

    public async Task<bool> RunAsync(Guid taskId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var claimed = await db.BackgroundTasks.Where(t => t.Id == taskId && t.Status == BackgroundTaskStatus.Queued)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, BackgroundTaskStatus.Running).SetProperty(t => t.StartedAt, now), ct);
        if (claimed == 0) return false;

        var task = await db.BackgroundTasks.FirstAsync(t => t.Id == taskId, ct);
        acting.UserId = task.OwnerUserId;
        try
        {
            if (task.Kind == BackgroundTaskKind.ProductImport) await ImportAsync(task, ct);
            else await PriceStockAsync(task, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Bulk task {TaskId} failed", taskId);
            db.ClearTracking();
            task = await db.BackgroundTasks.FirstAsync(t => t.Id == taskId, ct);
            task.Fail("Việc bị dừng do lỗi hệ thống. Các dòng đã xử lý vẫn được giữ; vui lòng tải lại tệp với các dòng còn lại.", clock.UtcNow);
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    // ---------- product import ----------

    private sealed record Group(string Key, List<SheetRow> Rows);

    private async Task ImportAsync(BackgroundTask task, CancellationToken ct)
    {
        var content = sheets.Read(task.Input!, BulkColumns.ImportFirstDataRow);
        var categoryId = Guid.Parse(content.Meta[BulkColumns.MetaCategory]);
        var shopId = task.ShopId!.Value;
        var attributes = await db.CategoryAttributes.AsNoTracking().Where(a => a.CategoryId == categoryId).OrderBy(a => a.SortOrder).ThenBy(a => a.Id)
            .ToListAsync(ct);
        var brands = await db.Brands.AsNoTracking().Select(b => new { b.Id, b.Name }).ToListAsync(ct);

        // Rows sharing a "Mã nhóm" are one product; a row without one is a product by itself
        var groups = new List<Group>();
        foreach (var row in content.Rows)
        {
            var key = row.Cells.GetValueOrDefault(BulkColumns.Group);
            var existing = key is null ? null : groups.FirstOrDefault(g => g.Key == key);
            if (existing is null) groups.Add(new Group(key ?? $"#{row.Row}", [row]));
            else existing.Rows.Add(row);
        }
        task.Begin(groups.Count);
        await db.SaveChangesAsync(ct);

        var progress = new Progress(task.Id);
        var created = 0;
        foreach (var group in groups)
        {
            var errors = new List<TaskRowError>();
            var ok = false;
            try
            {
                var input = await BuildInputAsync(group, categoryId, attributes, brands.Select(b => (b.Id, b.Name)).ToList(), errors, ct);
                if (input is not null)
                {
                    var productId = await sender.Send(new CreateProductCommand(shopId, input), ct);
                    created++;
                    try
                    {
                        await sender.Send(new ChangeProductStatusCommand(shopId, productId, SellerProductAction.Submit), ct);
                    }
                    catch (Exception ex) when (ex is BusinessRuleException or ConflictException or ValidationException)
                    {
                        errors.Add(new TaskRowError(group.Rows[0].Row, null, $"Đã lưu nháp nhưng chưa gửi duyệt được: {ex.Message}"));
                    }
                    ok = errors.Count == 0;
                }
            }
            catch (ValidationException ve)
            {
                errors.AddRange(ve.Errors.Select(e => new TaskRowError(group.Rows[0].Row, ColumnOf(e.PropertyName), e.ErrorMessage)));
            }
            catch (Exception ex) when (ex is BusinessRuleException or ConflictException or NotFoundException or ForbiddenException)
            {
                errors.Add(new TaskRowError(group.Rows[0].Row, null, ex.Message));
            }
            progress.Add(ok, errors);
            if (progress.Processed % SaveEvery == 0) await SaveProgressAsync(progress, ct);
            else db.ClearTracking();
        }
        task = await SaveProgressAsync(progress, ct);
        task.Finish($"Đã tạo {created} / {groups.Count} sản phẩm{(progress.Failed > 0 ? $", {progress.Failed} sản phẩm có lỗi — xem bảng lỗi" : "")}.",
            clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Counters of a running task; written to its row every few rows and at the end.</summary>
    private sealed class Progress(Guid taskId)
    {
        public Guid TaskId { get; } = taskId;
        public int Processed { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public List<TaskRowError> Errors { get; } = [];

        public void Add(bool ok, IEnumerable<TaskRowError> errors)
        {
            Processed++;
            if (ok) Succeeded++;
            else Failed++;
            foreach (var e in errors)
                if (Errors.Count < BackgroundTask.MaxErrors) Errors.Add(e);
        }
    }

    // Each command saved its own work: forget it (memory, half-built entities of a failed row) and write the counters
    private async Task<BackgroundTask> SaveProgressAsync(Progress progress, CancellationToken ct)
    {
        db.ClearTracking();
        var task = await db.BackgroundTasks.FirstAsync(t => t.Id == progress.TaskId, ct);
        task.SetProgress(progress.Processed, progress.Succeeded, progress.Failed, progress.Errors);
        await db.SaveChangesAsync(ct);
        return task;
    }

    private async Task<ProductInput?> BuildInputAsync(Group group, Guid categoryId, IReadOnlyList<CategoryAttribute> attributes,
        IReadOnlyList<(Guid Id, string Name)> brands, List<TaskRowError> errors, CancellationToken ct)
    {
        var first = group.Rows[0];
        string? Cell(SheetRow r, string column) => r.Cells.GetValueOrDefault(column);
        void Error(SheetRow r, string column, string message) => errors.Add(new TaskRowError(r.Row, column, message));
        int? Int(SheetRow r, string column, bool required)
        {
            var v = Cell(r, column);
            if (v is null)
            {
                if (required) Error(r, column, "Bắt buộc.");
                return null;
            }
            if (long.TryParse(v.Replace(".", "").Replace(",", "").Replace(" ", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is >= 0 and <= int.MaxValue)
                return (int)n;
            Error(r, column, $"\"{v}\" không phải số nguyên hợp lệ.");
            return null;
        }
        long? Money(SheetRow r, string column, bool required) => Int(r, column, required);

        var name = Cell(first, BulkColumns.Name);
        if (name is null) Error(first, BulkColumns.Name, "Bắt buộc.");
        var description = Cell(first, BulkColumns.Description);
        if (description is null) Error(first, BulkColumns.Description, "Bắt buộc.");
        var condition = Cell(first, BulkColumns.Condition) switch
        {
            null or "Mới" => ProductCondition.New,
            "Đã sử dụng" => ProductCondition.Used,
            var other => Fail(first, BulkColumns.Condition, $"\"{other}\" — chọn Mới hoặc Đã sử dụng."),
        };
        ProductCondition Fail(SheetRow r, string c, string m) { Error(r, c, m); return ProductCondition.New; }
        Guid? brandId = null;
        if (Cell(first, BulkColumns.Brand) is { } brandName)
        {
            var match = brands.FirstOrDefault(b => string.Equals(b.Name, brandName, StringComparison.OrdinalIgnoreCase));
            if (match.Id == Guid.Empty) Error(first, BulkColumns.Brand, $"Không có thương hiệu \"{brandName}\" trên ShopHub.");
            else brandId = match.Id;
        }
        var weight = Int(first, BulkColumns.Weight, true);
        var length = Int(first, BulkColumns.Length, false) ?? 0;
        var width = Int(first, BulkColumns.Width, false) ?? 0;
        var height = Int(first, BulkColumns.Height, false) ?? 0;
        var maxPerBuyer = Int(first, BulkColumns.MaxPerBuyer, false);

        // Attributes of the category, by their template column
        var values = new List<ProductAttributeValueDto>();
        foreach (var a in attributes)
        {
            var raw = Cell(first, BulkColumns.Attribute(a));
            if (raw is null)
            {
                if (a.IsRequired) Error(first, BulkColumns.Attribute(a), "Thuộc tính bắt buộc của ngành hàng.");
                continue;
            }
            var parts = a.InputType == AttributeInputType.MultiSelect
                ? raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                : [raw];
            values.Add(new ProductAttributeValueDto(a.Id, parts));
        }

        // Variants: tier names from the first row; each row one SKU
        var tier1 = Cell(first, BulkColumns.Tier1);
        var tier2 = Cell(first, BulkColumns.Tier2);
        var options1 = new List<string>();
        var options2 = new List<string>();
        var skus = new List<SkuInput>();
        foreach (var r in group.Rows)
        {
            var o1 = Cell(r, BulkColumns.Option1);
            var o2 = Cell(r, BulkColumns.Option2);
            if (tier1 is null && o1 is not null) Error(r, BulkColumns.Tier1, "Có giá trị phân loại 1 nhưng chưa có tên phân loại 1 (ở dòng đầu của nhóm).");
            if (tier1 is not null && o1 is null) Error(r, BulkColumns.Option1, "Thiếu giá trị phân loại 1.");
            if (tier2 is not null && o2 is null) Error(r, BulkColumns.Option2, "Thiếu giá trị phân loại 2.");
            if (tier2 is null && o2 is not null) Error(r, BulkColumns.Tier2, "Có giá trị phân loại 2 nhưng chưa có tên phân loại 2.");
            if (o1 is not null && !options1.Contains(o1)) options1.Add(o1);
            if (o2 is not null && !options2.Contains(o2)) options2.Add(o2);
            var price = Money(r, BulkColumns.Price, true);
            var original = Money(r, BulkColumns.OriginalPrice, false);
            var stock = Int(r, BulkColumns.Stock, true);
            if (price is not null && stock is not null)
                skus.Add(new SkuInput(tier1 is null ? null : o1, tier2 is null ? null : o2, Cell(r, BulkColumns.SellerSku), price.Value,
                    original ?? price.Value, stock.Value, null));
        }
        if (group.Rows.Count > 1 && tier1 is null)
            Error(first, BulkColumns.Tier1, $"Nhóm {group.Key} có {group.Rows.Count} dòng nên cần tên phân loại.");

        // Images last: nothing is downloaded for a product that is already wrong
        var urls = (Cell(first, BulkColumns.Images) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (urls.Count == 0) Error(first, BulkColumns.Images, "Cần ít nhất 1 ảnh.");
        if (urls.Count > BulkColumns.MaxImagesPerProduct) Error(first, BulkColumns.Images, $"Tối đa {BulkColumns.MaxImagesPerProduct} ảnh.");
        if (errors.Count > 0) return null;

        var media = new List<MediaInput>();
        foreach (var url in urls)
        {
            try
            {
                var bytes = await images.FetchAsync(url, ct);
                var asset = await sender.Send(new UploadMediaCommand(bytes, MediaPurpose.Product), ct);
                media.Add(new MediaInput(asset.Id, null));
            }
            catch (Exception ex) when (ex is BusinessRuleException or ValidationException)
            {
                Error(first, BulkColumns.Images, $"{url}: {(ex is ValidationException ve ? ve.Errors.First().ErrorMessage : ex.Message)}");
            }
        }
        if (errors.Count > 0) return null;

        var tiers = new List<TierInput>();
        if (tier1 is not null) tiers.Add(new TierInput(tier1, options1.Select(v => new OptionInput(v, null)).ToList()));
        if (tier2 is not null) tiers.Add(new TierInput(tier2, options2.Select(v => new OptionInput(v, null)).ToList()));
        return new ProductInput(categoryId, brandId, name!, description!, condition, weight!.Value, length, width, height, false, 0, values, media, tiers, skus,
            maxPerBuyer);
    }

    private static string? ColumnOf(string property)
    {
        var p = property.Contains('.') ? property[(property.LastIndexOf('.') + 1)..] : property;
        p = p.Split('[')[0];
        return p switch
        {
            "Name" => BulkColumns.Name,
            "Description" => BulkColumns.Description,
            "WeightG" => BulkColumns.Weight,
            "Media" => BulkColumns.Images,
            "Price" => BulkColumns.Price,
            "OriginalPrice" => BulkColumns.OriginalPrice,
            "Stock" => BulkColumns.Stock,
            "Tiers" or "Options" => BulkColumns.Tier1,
            "MaxPerBuyer" => BulkColumns.MaxPerBuyer,
            _ => null,
        };
    }

    // ---------- price / stock ----------

    private async Task PriceStockAsync(BackgroundTask task, CancellationToken ct)
    {
        var content = sheets.Read(task.Input!, BulkColumns.PriceStockFirstDataRow);
        var shopId = task.ShopId!.Value;
        task.Begin(content.Rows.Count);
        await db.SaveChangesAsync(ct);
        var progress = new Progress(task.Id);
        var changed = 0;
        foreach (var r in content.Rows)
        {
            var errors = new List<TaskRowError>();
            var ok = false;
            try
            {
                if (!Guid.TryParse(r.Cells.GetValueOrDefault(BulkColumns.SkuId), out var skuId))
                    errors.Add(new TaskRowError(r.Row, BulkColumns.SkuId, "Thiếu hoặc sai ID SKU — không sửa cột này."));
                long? Parse(string column)
                {
                    var v = r.Cells.GetValueOrDefault(column);
                    if (v is null) return null;
                    if (long.TryParse(v.Replace(".", "").Replace(",", "").Replace(" ", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0)
                        return n;
                    errors.Add(new TaskRowError(r.Row, column, $"\"{v}\" không phải số nguyên hợp lệ."));
                    return null;
                }
                var price = Parse(BulkColumns.Price);
                var original = Parse(BulkColumns.OriginalPrice);
                var stock = Parse(BulkColumns.Stock);
                if (errors.Count == 0)
                {
                    var current = await db.Skus.AsNoTracking().Where(s => s.Id == skuId).Select(s => new { s.Price, s.OriginalPrice, s.Stock }).FirstOrDefaultAsync(ct);
                    if (current is not null && current.Price == price && current.OriginalPrice == (original ?? current.OriginalPrice) && current.Stock == stock)
                        ok = true;   // unchanged row
                    else
                    {
                        await sender.Send(new UpdateSkuQuickCommand(shopId, skuId, price, original, stock is null ? null : (int)stock), ct);
                        changed++;
                        ok = true;
                    }
                }
            }
            catch (ValidationException ve)
            {
                errors.AddRange(ve.Errors.Select(e => new TaskRowError(r.Row, ColumnOf(e.PropertyName), e.ErrorMessage)));
            }
            catch (Exception ex) when (ex is BusinessRuleException or ConflictException or NotFoundException or ForbiddenException)
            {
                errors.Add(new TaskRowError(r.Row, null, ex.Message));
            }
            progress.Add(ok && errors.Count == 0, errors);
            if (progress.Processed % (SaveEvery * 5) == 0) await SaveProgressAsync(progress, ct);
            else db.ClearTracking();
        }
        task = await SaveProgressAsync(progress, ct);
        task.Finish($"Đã cập nhật {changed} SKU{(progress.Failed > 0 ? $", {progress.Failed} dòng lỗi — xem bảng lỗi" : "")}.", clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }
}
