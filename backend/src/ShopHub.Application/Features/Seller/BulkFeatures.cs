using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Media;
using ShopHub.Application.Security;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Common;
using ShopHub.Domain.SystemConfig;

namespace ShopHub.Application.Features.Seller;

// Đăng hàng loạt & cập nhật giá / tồn hàng loạt bằng Excel (III.3). Uploads become background tasks; each row goes
// through the same commands as the product editor and the quick edit, acting as the person who uploaded the file.

public static class BulkColumns
{
    public const string Group = "Mã nhóm";
    public const string Name = "Tên sản phẩm*";
    public const string Description = "Mô tả*";
    public const string Condition = "Tình trạng";
    public const string Brand = "Thương hiệu";
    public const string Weight = "Cân nặng (g)*";
    public const string Length = "Dài (mm)";
    public const string Width = "Rộng (mm)";
    public const string Height = "Cao (mm)";
    public const string Images = "Ảnh (URL)*";
    public const string Tier1 = "Tên phân loại 1";
    public const string Option1 = "Phân loại 1";
    public const string Tier2 = "Tên phân loại 2";
    public const string Option2 = "Phân loại 2";
    public const string Price = "Giá*";
    public const string OriginalPrice = "Giá gốc";
    public const string Stock = "Tồn kho*";
    public const string SellerSku = "Mã SKU";
    public const string MaxPerBuyer = "Giới hạn mua mỗi người";
    public const string AttributePrefix = "Thuộc tính: ";

    public const string SkuId = "ID SKU (không sửa)";
    public const string Product = "Sản phẩm";
    public const string Variant = "Phân loại";

    public const string MetaKind = "kind";
    public const string MetaCategory = "categoryId";
    public const string MetaShop = "shopId";

    public const int ImportFirstDataRow = 3;   // row 2 holds the hints
    public const int PriceStockFirstDataRow = 2;
    public const int MaxImportRows = 1000;
    public const int MaxPriceStockRows = 5000;
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxImagesPerProduct = 9;

    public static string Attribute(CategoryAttribute a) => $"{AttributePrefix}{a.Name}{(a.Unit is { Length: > 0 } u ? $" ({u})" : "")}{(a.IsRequired ? "*" : "")}";
}

public record BackgroundTaskDto(Guid Id, BackgroundTaskKind Kind, BackgroundTaskStatus Status, string FileName, int Total, int Processed, int Succeeded,
    int Failed, IReadOnlyList<TaskRowError> Errors, string? Message, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);

internal static class BulkMapping
{
    public static BackgroundTaskDto ToDto(BackgroundTask t) => new(t.Id, t.Kind, t.Status, t.FileName, t.Total, t.Processed, t.Succeeded, t.FailedCount,
        t.Errors, t.Message, t.CreatedAt, t.FinishedAt);
}

// ---------- template / export ----------

public record ImportTemplateQuery(Guid ShopId, Guid CategoryId) : IRequest<(string FileName, byte[] Content)>;

public sealed class ImportTemplateHandler(IApplicationDbContext db, SellerAccess access, IProductSheets sheets)
    : IRequestHandler<ImportTemplateQuery, (string FileName, byte[] Content)>
{
    public async Task<(string FileName, byte[] Content)> Handle(ImportTemplateQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.ProductManage, ct);
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CategoryId && c.IsActive, ct)
                       ?? throw new NotFoundException("Không tìm thấy ngành hàng.");
        if (await db.Categories.AnyAsync(c => c.ParentId == category.Id, ct))
            throw new BusinessRuleException("Vui lòng chọn ngành hàng cấp cuối cùng.");
        var attributes = await db.CategoryAttributes.AsNoTracking().Where(a => a.CategoryId == category.Id)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Id).ToListAsync(ct);

        List<TemplateColumn> columns =
        [
            new(BulkColumns.Group, false, "Các dòng cùng mã là các phân loại của MỘT sản phẩm; để trống nếu sản phẩm không có phân loại", null),
            new(BulkColumns.Name, true, "10–120 ký tự", null),
            new(BulkColumns.Description, true, "Chữ thường, xuống dòng giữ nguyên", null),
            new(BulkColumns.Condition, false, "Mặc định Mới", ["Mới", "Đã sử dụng"]),
            new(BulkColumns.Brand, false, "Tên thương hiệu có trên ShopHub", null),
            new(BulkColumns.Weight, true, "Cân nặng đóng gói, gram", null),
            new(BulkColumns.Length, false, null, null), new(BulkColumns.Width, false, null, null), new(BulkColumns.Height, false, null, null),
            new(BulkColumns.Images, true, $"1–{BulkColumns.MaxImagesPerProduct} link https, cách nhau bởi dấu ; — ảnh đầu là ảnh bìa (vuông 1:1)", null),
            new(BulkColumns.Tier1, false, "Ví dụ: Màu sắc", null), new(BulkColumns.Option1, false, "Ví dụ: Đỏ", null),
            new(BulkColumns.Tier2, false, "Ví dụ: Size", null), new(BulkColumns.Option2, false, "Ví dụ: XL", null),
            new(BulkColumns.Price, true, "Số nguyên, ₫", null), new(BulkColumns.OriginalPrice, false, "≥ giá; để trống = bằng giá", null),
            new(BulkColumns.Stock, true, "Số nguyên ≥ 0", null), new(BulkColumns.SellerSku, false, null, null),
            new(BulkColumns.MaxPerBuyer, false, "Để trống = không giới hạn", null),
        ];
        columns.AddRange(attributes.Select(a => new TemplateColumn(BulkColumns.Attribute(a), a.IsRequired,
            a.InputType switch
            {
                AttributeInputType.MultiSelect => "Chọn nhiều, cách nhau bởi dấu ;",
                AttributeInputType.Number => "Số",
                _ => null,
            },
            a.InputType is AttributeInputType.SingleSelect && a.Options.Count > 0 ? a.Options : null)));

        var guide = new List<string>
        {
            $"Tệp mẫu đăng hàng loạt — ngành {category.Name}.",
            "Mỗi dòng là một SKU. Sản phẩm không có phân loại: một dòng, để trống Mã nhóm và các cột phân loại.",
            "Sản phẩm có phân loại: các dòng cùng Mã nhóm; thông tin sản phẩm (tên, mô tả, ảnh, thuộc tính…) lấy ở dòng đầu tiên của nhóm.",
            "Cột có dấu * là bắt buộc. Dòng 2 là gợi ý — không xoá, dữ liệu bắt đầu từ dòng 3.",
            $"Tối đa {BulkColumns.MaxImportRows} dòng, tệp ≤ 5 MB. Sản phẩm hợp lệ được gửi duyệt ngay; dòng lỗi hiện trong bảng lỗi kèm số dòng.",
        };
        var meta = new Dictionary<string, string>
        {
            [BulkColumns.MetaKind] = nameof(BackgroundTaskKind.ProductImport),
            [BulkColumns.MetaCategory] = category.Id.ToString(),
            [BulkColumns.MetaShop] = request.ShopId.ToString(),
        };
        return ($"mau-dang-hang-{category.Slug}.xlsx", sheets.ImportTemplate($"Đăng hàng loạt — {category.Name}", columns, meta, guide));
    }
}

public record PriceStockExportQuery(Guid ShopId) : IRequest<(string FileName, byte[] Content)>;

public sealed class PriceStockExportHandler(IApplicationDbContext db, SellerAccess access, IProductSheets sheets)
    : IRequestHandler<PriceStockExportQuery, (string FileName, byte[] Content)>
{
    public async Task<(string FileName, byte[] Content)> Handle(PriceStockExportQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.InventoryManage, ct);
        var rows = await (from s in db.Skus.AsNoTracking()
                          join p in db.Products.AsNoTracking() on s.ProductId equals p.Id
                          where p.ShopId == request.ShopId && s.IsActive
                          orderby p.Name, p.Id, s.Id
                          select new
                          {
                              s.Id, s.SellerSku, p.Name, s.Price, s.OriginalPrice, s.Stock,
                              Option1 = db.VariantOptions.Where(o => o.Id == s.Option1Id).Select(o => o.Value).FirstOrDefault(),
                              Option2 = db.VariantOptions.Where(o => o.Id == s.Option2Id).Select(o => o.Value).FirstOrDefault(),
                          }).Take(BulkColumns.MaxPriceStockRows).ToListAsync(ct);
        var sheet = sheets.PriceStockSheet(rows.Select(r => new PriceStockRow(r.Id, r.SellerSku, r.Name,
                string.Join(", ", new[] { r.Option1, r.Option2 }.Where(v => !string.IsNullOrEmpty(v))) is { Length: > 0 } v ? v : null,
                r.Price, r.OriginalPrice, r.Stock)).ToList(),
            new Dictionary<string, string> { [BulkColumns.MetaKind] = nameof(BackgroundTaskKind.PriceStockUpdate), [BulkColumns.MetaShop] = request.ShopId.ToString() });
        return ("gia-ton-kho.xlsx", sheet);
    }
}

// ---------- start / follow ----------

public record StartBulkTaskCommand(Guid ShopId, BackgroundTaskKind Kind, string FileName, byte[] Content) : IRequest<BackgroundTaskDto>;

public sealed class StartBulkTaskValidator : AbstractValidator<StartBulkTaskCommand>
{
    public StartBulkTaskValidator()
    {
        RuleFor(x => x.Kind).Must(k => k is BackgroundTaskKind.ProductImport or BackgroundTaskKind.PriceStockUpdate)
            .WithMessage("Loại việc không hợp lệ.");
        RuleFor(x => x.Content).Cascade(CascadeMode.Stop).NotNull().Must(c => c.Length > 0).WithMessage("Vui lòng chọn tệp Excel.")
            .Must(c => c.Length <= BulkColumns.MaxFileBytes).WithMessage("Tệp tối đa 5 MB.");
    }
}

/// <summary>Checks the file is the right ShopHub sheet for this shop, stores it and queues the work; returns the task to follow.</summary>
public sealed class StartBulkTaskHandler(IApplicationDbContext db, SellerAccess access, IProductSheets sheets, IBackgroundTasks tasks, IClock clock)
    : IRequestHandler<StartBulkTaskCommand, BackgroundTaskDto>
{
    public async Task<BackgroundTaskDto> Handle(StartBulkTaskCommand request, CancellationToken ct)
    {
        var staff = await access.RequireAsync(request.ShopId,
            request.Kind == BackgroundTaskKind.ProductImport ? ShopPermissions.ProductManage : ShopPermissions.InventoryManage, ct);
        var content = sheets.Read(request.Content,
            request.Kind == BackgroundTaskKind.ProductImport ? BulkColumns.ImportFirstDataRow : BulkColumns.PriceStockFirstDataRow);
        if (content.Meta.GetValueOrDefault(BulkColumns.MetaKind) != request.Kind.ToString())
            throw new BusinessRuleException(request.Kind == BackgroundTaskKind.ProductImport
                ? "Đây không phải tệp mẫu đăng hàng loạt. Vui lòng tải tệp mẫu theo ngành rồi điền vào đó."
                : "Đây không phải tệp giá / tồn kho. Vui lòng tải tệp từ nút \"Tải tệp giá & tồn kho\".");
        if (content.Meta.GetValueOrDefault(BulkColumns.MetaShop) != request.ShopId.ToString())
            throw new BusinessRuleException("Tệp này được tải cho shop khác.");
        var max = request.Kind == BackgroundTaskKind.ProductImport ? BulkColumns.MaxImportRows : BulkColumns.MaxPriceStockRows;
        if (content.Rows.Count == 0) throw new BusinessRuleException("Tệp chưa có dòng dữ liệu nào.");
        if (content.Rows.Count > max) throw new BusinessRuleException($"Tối đa {max} dòng mỗi tệp (tệp có {content.Rows.Count} dòng).");

        var task = new BackgroundTask(request.Kind, staff.UserId, request.ShopId, Path.GetFileName(request.FileName), request.Content, clock.UtcNow);
        db.BackgroundTasks.Add(task);
        await db.SaveChangesAsync(ct);
        tasks.Enqueue(task.Id);
        return BulkMapping.ToDto(task);
    }
}

// ---------- exports in the background (6.4) ----------

public record StartOrdersExportCommand(Guid ShopId, ShopOrderTab Tab, DateTimeOffset? From, DateTimeOffset? To) : IRequest<BackgroundTaskDto>;

/// <summary>Queues "Xuất Excel" of the shop's orders; the page follows the task and downloads the file when it is ready.</summary>
public sealed class StartOrdersExportHandler(IApplicationDbContext db, SellerAccess access, IBackgroundTasks tasks, IClock clock)
    : IRequestHandler<StartOrdersExportCommand, BackgroundTaskDto>
{
    public async Task<BackgroundTaskDto> Handle(StartOrdersExportCommand request, CancellationToken ct)
    {
        var staff = await access.RequireAsync(request.ShopId, ShopPermissions.OrderView, ct);
        if (request.From is { } f && request.To is { } t && f > t) throw new BusinessRuleException("Khoảng ngày không hợp lệ: ngày bắt đầu sau ngày kết thúc.");
        var input = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new ExportShopOrdersQuery(request.ShopId, request.Tab, request.From, request.To),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var task = new BackgroundTask(BackgroundTaskKind.OrdersExport, staff.UserId, request.ShopId,
            $"don-hang-{VietnamTime.ToLocal(clock.UtcNow):yyyyMMdd-HHmm}.xlsx", input, clock.UtcNow);
        db.BackgroundTasks.Add(task);
        await db.SaveChangesAsync(ct);
        tasks.Enqueue(task.Id);
        return BulkMapping.ToDto(task);
    }
}

public record ShopTaskFileQuery(Guid ShopId, Guid TaskId) : IRequest<Admin.TaskFile>;

public sealed class ShopTaskFileHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<ShopTaskFileQuery, Admin.TaskFile>
{
    public async Task<Admin.TaskFile> Handle(ShopTaskFileQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.OrderView, ct);
        return await Admin.TaskFiles.LoadAsync(db, t => t.Id == request.TaskId && t.ShopId == request.ShopId, ct);
    }
}

public record BulkTasksQuery(Guid ShopId) : IRequest<IReadOnlyList<BackgroundTaskDto>>;

public sealed class BulkTasksHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<BulkTasksQuery, IReadOnlyList<BackgroundTaskDto>>
{
    public async Task<IReadOnlyList<BackgroundTaskDto>> Handle(BulkTasksQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.ProductView, ct);
        var rows = await db.BackgroundTasks.AsNoTracking().Where(t => t.ShopId == request.ShopId)
            .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id).Take(20).ToListAsync(ct);
        return rows.Select(BulkMapping.ToDto).ToList();
    }
}

public record BulkTaskQuery(Guid ShopId, Guid TaskId) : IRequest<BackgroundTaskDto>;

public sealed class BulkTaskHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<BulkTaskQuery, BackgroundTaskDto>
{
    public async Task<BackgroundTaskDto> Handle(BulkTaskQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.ProductView, ct);
        var task = await db.BackgroundTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.TaskId && t.ShopId == request.ShopId, ct)
                   ?? throw new NotFoundException("Không tìm thấy việc.");
        return BulkMapping.ToDto(task);
    }
}
