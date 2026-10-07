using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Media;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;

namespace ShopHub.Application.Features.Seller;

// ---------- DTOs ----------

public record SellerProductRowDto(
    Guid Id,
    string Name,
    string? ImageUrl,
    ProductStatus Status,
    long MinPrice,
    long MaxPrice,
    int TotalStock,
    int TotalAvailable,
    int SkuCount,
    int SoldCount,
    string? ReviewNote,
    string? BanReason,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset CreatedAt);

public record ProductTierDto(int TierIndex, string Name, IReadOnlyList<ProductOptionDto> Options);

public record ProductOptionDto(Guid Id, string Value, string? ImageUrl);

public record ProductSkuDto(
    Guid Id,
    string? Option1,
    string? Option2,
    string? SellerSku,
    long Price,
    long OriginalPrice,
    int Stock,
    int Reserved,
    int Available,
    int? WeightG,
    bool IsActive,
    PackageSize? Size = null);

public record ProductMediaDto(MediaType Type, Guid? AssetId, string Url, string? OptionValue);

public record ProductAttributeValueDto(Guid AttributeId, IReadOnlyList<string> Values);

public record SellerProductDetailDto(
    Guid Id,
    Guid ShopId,
    Guid CategoryId,
    IReadOnlyList<string> CategoryPath,
    Guid? BrandId,
    string Name,
    string Description,
    ProductStatus Status,
    ProductCondition Condition,
    int WeightG,
    int LengthMm,
    int WidthMm,
    int HeightMm,
    bool IsPreorder,
    int PreorderDays,
    IReadOnlyList<ProductAttributeValueDto> Attributes,
    IReadOnlyList<ProductMediaDto> Media,
    IReadOnlyList<ProductTierDto> Tiers,
    IReadOnlyList<ProductSkuDto> Skus,
    string? ReviewNote,
    string? BanReason,
    string? Flags,
    uint Version,
    int? MaxPerBuyer,
    Guid? WarehouseId = null,
    IReadOnlyList<string>? CarrierCodes = null);

// ---------- Input ----------

public record MediaInput(Guid AssetId, string? OptionValue);

public record OptionInput(string Value, Guid? ImageAssetId);

public record TierInput(string Name, IReadOnlyList<OptionInput> Options);

public record SkuInput(string? Option1, string? Option2, string? SellerSku, long Price, long OriginalPrice, int Stock, int? WeightG, bool IsActive = true,
    // Kích thước đóng gói of this variant (mm); null = the product's
    PackageSize? Size = null);

public record ProductInput(
    Guid CategoryId,
    Guid? BrandId,
    string Name,
    string Description,
    ProductCondition Condition,
    int WeightG,
    int LengthMm,
    int WidthMm,
    int HeightMm,
    bool IsPreorder,
    int PreorderDays,
    IReadOnlyList<ProductAttributeValueDto> Attributes,
    IReadOnlyList<MediaInput> Media,
    IReadOnlyList<TierInput> Tiers,
    IReadOnlyList<SkuInput> Skus,
    // Giới hạn mua mỗi người (all variants together); null = no limit
    int? MaxPerBuyer = null,
    // Kho gửi (đa kho; null = the default pickup warehouse) and the carriers allowed for it (empty = all the shop uses)
    Guid? WarehouseId = null,
    IReadOnlyList<string>? CarrierCodes = null);

public sealed class ProductInputValidator : AbstractValidator<ProductInput>
{
    public ProductInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên sản phẩm.")
            .MaximumLength(Product.MaxNameLength).WithMessage($"Tên sản phẩm tối đa {Product.MaxNameLength} ký tự.");
        RuleFor(x => x.Description).MaximumLength(20_000).WithMessage("Mô tả tối đa 20.000 ký tự.");
        RuleFor(x => x.WeightG).InclusiveBetween(1, 1_000_000).WithMessage("Cân nặng phải từ 1 g đến 1.000 kg.");
        RuleFor(x => x.Media).NotNull().Must(m => m.Count >= 1).WithMessage("Sản phẩm cần ít nhất 1 ảnh.");
        RuleFor(x => x.Tiers).NotNull().Must(t => t.Count <= Product.MaxTiers).WithMessage("Sản phẩm có tối đa 2 tầng phân loại.");
        RuleFor(x => x.Skus).NotNull().Must(s => s.Count >= 1).WithMessage("Sản phẩm cần ít nhất một SKU.")
            .Must(s => s.Count <= 400).WithMessage("Tối đa 400 SKU cho một sản phẩm.");
        RuleForEach(x => x.Skus).ChildRules(sku =>
        {
            sku.RuleFor(s => s.Price).GreaterThan(0).WithMessage("Giá bán phải lớn hơn 0.");
            sku.RuleFor(s => s.OriginalPrice).GreaterThanOrEqualTo(s => s.Price).WithMessage("Giá gốc không được thấp hơn giá bán.");
            sku.RuleFor(s => s.Stock).InclusiveBetween(0, 10_000_000).WithMessage("Tồn kho không hợp lệ.");
        });
    }
}

// ---------- Saving (create / update) ----------

/// <summary>Turns a validated ProductInput into domain calls; shared by create and update.</summary>
public sealed class ProductWriter(
    IApplicationDbContext db,
    IObjectStorage storage,
    IHtmlSanitizer sanitizer,
    ISystemParameters parameters,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task ApplyAsync(Product product, ProductInput input, Guid uploaderId, CancellationToken ct)
    {
        var errors = new List<ValidationFailure>();

        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == input.CategoryId, ct);
        if (category is null || !category.IsActive) errors.Add(new("categoryId", "Danh mục không hợp lệ."));
        else if (await db.Categories.AnyAsync(c => c.ParentId == category.Id, ct)) errors.Add(new("categoryId", "Vui lòng chọn danh mục cấp cuối."));
        if (input.BrandId is { } brandId && !await db.Brands.AnyAsync(b => b.Id == brandId, ct))
            errors.Add(new("brandId", "Thương hiệu không tồn tại."));
        if (input.WarehouseId is { } warehouseId && !await db.ShopWarehouses.AnyAsync(w => w.Id == warehouseId && w.ShopId == product.ShopId, ct))
            errors.Add(new("warehouseId", "Kho gửi không thuộc shop này."));
        var carrierCodes = (input.CarrierCodes ?? []).Select(c => c.Trim()).Where(c => c.Length > 0).Distinct().ToList();
        if (carrierCodes.Count > 0 && await db.Carriers.CountAsync(c => carrierCodes.Contains(c.Code), ct) != carrierCodes.Count)
            errors.Add(new("carrierCodes", "Có đơn vị vận chuyển không tồn tại."));

        // Required / typed industry attributes of the leaf category
        var definitions = await db.CategoryAttributes.AsNoTracking().Where(a => a.CategoryId == input.CategoryId).ToListAsync(ct);
        var submitted = input.Attributes.ToDictionary(a => a.AttributeId, a => a.Values);
        foreach (var def in definitions)
            if (def.Check(submitted.GetValueOrDefault(def.Id) ?? []) is { } error) errors.Add(new($"attributes.{def.Id}", error));
        if (submitted.Keys.Any(k => definitions.All(d => d.Id != k))) errors.Add(new("attributes", "Có thuộc tính không thuộc danh mục đã chọn."));
        if (errors.Count > 0) throw new ValidationException(errors);

        // Media and option images must have been uploaded by this user for products
        var assetIds = input.Media.Select(m => m.AssetId)
            .Concat(input.Tiers.SelectMany(t => t.Options).Where(o => o.ImageAssetId is not null).Select(o => o.ImageAssetId!.Value));
        var assets = await LoadAssetsAsync(product, assetIds, uploaderId, ct);
        string UrlOf(Guid id) => assets[id].Kind == Domain.Media.MediaKind.Image
            ? storage.PublicUrl(assets[id].Bucket, ImageSizes.Key(assets[id].ObjectKey, ImageSizes.Large))
            : storage.PublicUrl(assets[id].Bucket, assets[id].ObjectKey);

        var sensitive = product.SetInfo(input.CategoryId, input.BrandId, input.Name, Slug.From(input.Name),
            sanitizer.Sanitize(input.Description ?? string.Empty), input.Condition, input.WeightG, input.LengthMm, input.WidthMm,
            input.HeightMm, input.IsPreorder, input.PreorderDays);
        product.SetAttributes(input.Attributes.Select(a => (a.AttributeId, a.Values)));
        product.SetPurchaseLimit(input.MaxPerBuyer);
        product.ShipFrom(input.WarehouseId);
        product.LimitCarriers(carrierCodes);

        var deltas = product.SetVariants(
            input.Tiers.Select(t => new TierSpec(t.Name, t.Options.Select(o =>
                new OptionSpec(o.Value, o.ImageAssetId is { } img ? UrlOf(img) : null)).ToList())).ToList(),
            input.Skus.Select(s => new SkuSpec(s.Option1, s.Option2, s.SellerSku, s.Price, s.OriginalPrice, s.Stock, s.WeightG, s.IsActive, s.Size)).ToList());

        sensitive |= product.SetMedia(input.Media.Select(m => new MediaSpec(
            assets[m.AssetId].Kind == Domain.Media.MediaKind.Video ? MediaType.Video : MediaType.Image,
            m.AssetId, UrlOf(m.AssetId), m.OptionValue)).ToList());

        var now = clock.UtcNow;
        foreach (var (sku, delta) in deltas)
            db.InventoryMovements.Add(new InventoryMovement(sku.Id, delta, 0, InventoryReason.SellerEdit, "product", product.Id,
                currentUser.UserId, null, now));

        if (sensitive && await parameters.GetBoolAsync(ParameterKeys.ProductReviewOnEdit, ct))
            product.RequireReReview(now);
    }

    private async Task<Dictionary<Guid, Domain.Media.MediaAsset>> LoadAssetsAsync(Product product, IEnumerable<Guid> ids, Guid uploaderId,
        CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        // Media already attached to this product stays usable even if a colleague uploaded it
        var attached = product.Media.Where(m => m.AssetId is not null).Select(m => m.AssetId!.Value).ToHashSet();
        var assets = await db.MediaAssets.Where(a => wanted.Contains(a.Id)
                && a.Purpose == "product" && (a.OwnerUserId == uploaderId || attached.Contains(a.Id)))
            .ToDictionaryAsync(a => a.Id, ct);
        if (assets.Count != wanted.Count) throw new NotFoundException("Không tìm thấy ảnh/video đã tải lên (hoặc tệp không thuộc về bạn).");
        return assets;
    }

    /// <summary>Banned keywords (accent-insensitive) found in name or description, for the review queue.</summary>
    public async Task<string?> FlagsForAsync(Product product, CancellationToken ct)
    {
        var json = await parameters.GetStringAsync(ParameterKeys.ProductBannedKeywords, ct);
        var keywords = JsonSerializer.Deserialize<List<string>>(json) ?? [];
        var haystack = $" {Slug.Fold(product.Name)} {Slug.Fold(System.Text.RegularExpressions.Regex.Replace(product.Description, "<[^>]+>", " "))} ";
        var hits = keywords.Where(k => k.Length > 0 && haystack.Contains($" {Slug.Fold(k)} ", StringComparison.Ordinal)).ToList();
        return hits.Count == 0 ? null : $"Từ khoá cấm: {string.Join(", ", hits)}";
    }
}

public record CreateProductCommand(Guid ShopId, ProductInput Input) : IRequest<Guid>;

public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator() =>
        RuleFor(x => x.Input).NotNull().WithMessage("Thiếu thông tin sản phẩm.").SetValidator(new ProductInputValidator());
}

public sealed class CreateProductHandler(IApplicationDbContext db, SellerAccess access, ProductWriter writer)
    : IRequestHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> Handle(CreateProductCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.ProductManage, ct);
        var shop = await db.Shops.AsNoTracking().FirstAsync(s => s.Id == request.ShopId, ct);
        if (shop.Status is Domain.Shops.ShopStatus.Locked or Domain.Shops.ShopStatus.Rejected)
            throw new ConflictException("Shop không ở trạng thái được đăng sản phẩm.", "SHOP_NOT_ALLOWED");

        var product = new Product(request.ShopId);
        await writer.ApplyAsync(product, request.Input, access.UserId, ct);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return product.Id;
    }
}

public record UpdateProductCommand(Guid ShopId, Guid ProductId, ProductInput Input, uint? Version) : IRequest<Unit>;

public sealed class UpdateProductValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductValidator() =>
        RuleFor(x => x.Input).NotNull().WithMessage("Thiếu thông tin sản phẩm.").SetValidator(new ProductInputValidator());
}

public sealed class UpdateProductHandler(IApplicationDbContext db, SellerAccess access, ProductWriter writer)
    : IRequestHandler<UpdateProductCommand, Unit>
{
    public async Task<Unit> Handle(UpdateProductCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.ProductManage, ct);
        var product = await ProductLoader.LoadForEditAsync(db, request.ShopId, request.ProductId, ct);
        if (request.Version is { } v && v != product.Version)
            throw new ConflictException("Sản phẩm vừa được sửa ở nơi khác. Vui lòng tải lại.", "STALE_VERSION");
        if (!product.IsEditableBySeller) throw new ConflictException("Sản phẩm đang bị khoá hoặc đã xoá, không sửa được.", "PRODUCT_LOCKED");

        await writer.ApplyAsync(product, request.Input, access.UserId, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Sản phẩm vừa được sửa ở nơi khác. Vui lòng tải lại.", "STALE_VERSION");
        }
        return Unit.Value;
    }
}

internal static class ProductLoader
{
    /// <summary>Product of THIS shop with its children (another shop's product is simply not found).</summary>
    public static async Task<Product> LoadForEditAsync(IApplicationDbContext db, Guid shopId, Guid productId, CancellationToken ct) =>
        await db.Products
            .Include(p => p.Tiers).ThenInclude(t => t.Options)
            .Include(p => p.Skus)
            .Include(p => p.Media)
            .Include(p => p.Attributes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == productId && p.ShopId == shopId, ct)
        ?? throw new NotFoundException("Không tìm thấy sản phẩm.");

    public static SellerProductDetailDto ToDetail(Product p, IReadOnlyList<string> categoryPath)
    {
        var options = p.Tiers.SelectMany(t => t.Options.Select(o => (t.TierIndex, o))).ToDictionary(x => x.o.Id, x => x.o);
        return new SellerProductDetailDto(p.Id, p.ShopId, p.CategoryId, categoryPath, p.BrandId, p.Name, p.Description, p.Status, p.Condition,
            p.WeightG, p.LengthMm, p.WidthMm, p.HeightMm, p.IsPreorder, p.PreorderDays,
            p.Attributes.Select(a => new ProductAttributeValueDto(a.AttributeId, a.Values)).ToList(),
            p.Media.OrderBy(m => m.SortOrder).Select(m => new ProductMediaDto(m.Type, m.AssetId, m.Url,
                m.VariantOptionId is { } oid && options.TryGetValue(oid, out var opt) ? opt.Value : null)).ToList(),
            p.Tiers.OrderBy(t => t.TierIndex).Select(t => new ProductTierDto(t.TierIndex, t.Name,
                t.Options.Where(o => o.IsActive).OrderBy(o => o.SortOrder).Select(o => new ProductOptionDto(o.Id, o.Value, o.ImageUrl)).ToList()))
                .Where(t => t.Options.Count > 0).ToList(),
            p.Skus.OrderBy(s => OptionSort(options, s.Option1Id)).ThenBy(s => OptionSort(options, s.Option2Id))
                .Select(s => new ProductSkuDto(s.Id, OptionValue(options, s.Option1Id), OptionValue(options, s.Option2Id), s.SellerSku,
                    s.Price, s.OriginalPrice, s.Stock, s.Reserved, s.Available, s.WeightG, s.IsActive, s.Size)).ToList(),
            p.ReviewNote, p.BanReason, p.Flags, p.Version, p.MaxPerBuyer, p.WarehouseId, p.CarrierCodes);
    }

    private static string? OptionValue(Dictionary<Guid, VariantOption> options, Guid? id) =>
        id is { } v && options.TryGetValue(v, out var o) ? o.Value : null;

    private static int OptionSort(Dictionary<Guid, VariantOption> options, Guid? id) =>
        id is { } v && options.TryGetValue(v, out var o) ? o.SortOrder : -1;

    public static async Task<IReadOnlyList<string>> CategoryPathAsync(IApplicationDbContext db, Guid categoryId, CancellationToken ct)
    {
        var path = new List<string>();
        Guid? current = categoryId;
        while (current is { } id && path.Count < 5)
        {
            var c = await db.Categories.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Name, x.ParentId }).FirstOrDefaultAsync(ct);
            if (c is null) break;
            path.Insert(0, c.Name);
            current = c.ParentId;
        }
        return path;
    }
}

public record GetSellerProductQuery(Guid ShopId, Guid ProductId) : IRequest<SellerProductDetailDto>;

public sealed class GetSellerProductHandler(IApplicationDbContext db, SellerAccess access)
    : IRequestHandler<GetSellerProductQuery, SellerProductDetailDto>
{
    public async Task<SellerProductDetailDto> Handle(GetSellerProductQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.ProductView, ct);
        var product = await ProductLoader.LoadForEditAsync(db, request.ShopId, request.ProductId, ct);
        return ProductLoader.ToDetail(product, await ProductLoader.CategoryPathAsync(db, product.CategoryId, ct));
    }
}

// ---------- Listing ----------

public enum SellerProductTab
{
    All,
    Active,
    SoldOut,
    Pending,
    Violation,
    Hidden,
    Draft,
    LowStock,
}

public record ListSellerProductsQuery(
    Guid ShopId,
    SellerProductTab Tab = SellerProductTab.All,
    string? Q = null,
    Guid? CategoryId = null,
    int Page = 1,
    int PageSize = PagingLimits.DefaultPageSize,
    // Lọc theo tồn kho (units available, all variants) and giá (any variant inside the range)
    int? MinStock = null,
    int? MaxStock = null,
    long? MinPrice = null,
    long? MaxPrice = null) : IRequest<PagedResult<SellerProductRowDto>>, IPagedRequest;

public sealed class ListSellerProductsValidator : AbstractValidator<ListSellerProductsQuery>
{
    public ListSellerProductsValidator()
    {
        this.ApplyPagingRules();
        RuleFor(x => x).Must(x => x.MinStock is null || x.MaxStock is null || x.MinStock <= x.MaxStock)
            .WithName("minStock").WithMessage("Khoảng tồn kho không hợp lệ: từ phải nhỏ hơn hoặc bằng đến.");
        RuleFor(x => x).Must(x => x.MinPrice is null || x.MaxPrice is null || x.MinPrice <= x.MaxPrice)
            .WithName("minPrice").WithMessage("Khoảng giá không hợp lệ: giá từ phải nhỏ hơn hoặc bằng giá đến.");
    }
}

public sealed class ListSellerProductsHandler(IApplicationDbContext db, SellerAccess access, ISystemParameters parameters)
    : IRequestHandler<ListSellerProductsQuery, PagedResult<SellerProductRowDto>>
{
    public async Task<PagedResult<SellerProductRowDto>> Handle(ListSellerProductsQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.ProductView, ct);
        var lowStock = (int)await LowStock.ThresholdAsync(db, parameters, request.ShopId, ct);

        var products = db.Products.AsNoTracking().Where(p => p.ShopId == request.ShopId);
        products = request.Tab switch
        {
            SellerProductTab.Active => products.Where(p => p.Status == ProductStatus.Active),
            SellerProductTab.SoldOut => products.Where(p => p.Status == ProductStatus.Active && !p.Skus.Any(s => s.IsActive && s.Stock - s.Reserved > 0)),
            SellerProductTab.Pending => products.Where(p => p.Status == ProductStatus.PendingReview),
            SellerProductTab.Violation => products.Where(p => p.Status == ProductStatus.Banned),
            SellerProductTab.Hidden => products.Where(p => p.Status == ProductStatus.Hidden),
            SellerProductTab.Draft => products.Where(p => p.Status == ProductStatus.Draft),
            SellerProductTab.LowStock => products.Where(p => p.Status == ProductStatus.Active
                && p.Skus.Where(s => s.IsActive).Sum(s => s.Stock - s.Reserved) <= lowStock),
            _ => products,
        };
        if (request.CategoryId is { } categoryId) products = products.Where(p => p.CategoryId == categoryId);
        if (request.MinStock is { } minStock) products = products.Where(p => p.Skus.Where(s => s.IsActive).Sum(s => s.Stock - s.Reserved) >= minStock);
        if (request.MaxStock is { } maxStock) products = products.Where(p => p.Skus.Where(s => s.IsActive).Sum(s => s.Stock - s.Reserved) <= maxStock);
        if (request.MinPrice is { } minPrice) products = products.Where(p => p.MaxPrice >= minPrice);
        if (request.MaxPrice is { } maxPrice) products = products.Where(p => p.MinPrice <= maxPrice);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = request.Q.Trim().ToLower();
            products = products.Where(p => p.Name.ToLower().Contains(q) || p.Skus.Any(s => s.SellerSku != null && s.SellerSku.ToLower() == q));
        }

        return await products
            .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt).ThenByDescending(p => p.Id)
            .ToPagedResultAsync(p => new SellerProductRowDto(
                p.Id, p.Name,
                p.Media.Where(m => m.Type == MediaType.Image).OrderBy(m => m.SortOrder).Select(m => m.Url).FirstOrDefault(),
                p.Status, p.MinPrice, p.MaxPrice,
                p.Skus.Where(s => s.IsActive).Sum(s => s.Stock),
                p.Skus.Where(s => s.IsActive).Sum(s => s.Stock - s.Reserved),
                p.Skus.Count(s => s.IsActive), p.SoldCount, p.ReviewNote, p.BanReason, p.UpdatedAt, p.CreatedAt),
                request, ct);
    }
}

// ---------- Lifecycle ----------

public enum SellerProductAction
{
    Submit,
    Hide,
    Show,
    Delete,
}

public record ChangeProductStatusCommand(Guid ShopId, Guid ProductId, SellerProductAction Action) : IRequest<ProductStatus>;

public sealed class ChangeProductStatusHandler(IApplicationDbContext db, SellerAccess access, ProductWriter writer, IClock clock)
    : IRequestHandler<ChangeProductStatusCommand, ProductStatus>
{
    public async Task<ProductStatus> Handle(ChangeProductStatusCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.ProductManage, ct);
        var product = await ProductLoader.LoadForEditAsync(db, request.ShopId, request.ProductId, ct);
        switch (request.Action)
        {
            case SellerProductAction.Submit:
                var shop = await db.Shops.AsNoTracking().FirstAsync(s => s.Id == request.ShopId, ct);
                if (!shop.CanSell && shop.Status != Domain.Shops.ShopStatus.Vacation)
                    throw new ConflictException("Shop chưa được duyệt, chưa thể gửi duyệt sản phẩm.", "SHOP_NOT_ACTIVE");
                product.SubmitForReview(clock.UtcNow, await writer.FlagsForAsync(product, ct));
                break;
            case SellerProductAction.Hide:
                product.Hide();
                break;
            case SellerProductAction.Show:
                product.Show();
                break;
            case SellerProductAction.Delete:
                if (product.Skus.Any(s => s.Reserved > 0))
                    throw new ConflictException("Sản phẩm đang có đơn giữ hàng, chưa xoá được.", "PRODUCT_HAS_RESERVATIONS");
                product.MarkDeleted(clock.UtcNow);
                break;
        }
        await db.SaveChangesAsync(ct);
        return product.Status;
    }
}

// ---------- Hàng loạt & sao chép (III.3) ----------

public record BulkProductResultDto(Guid ProductId, bool Ok, string? Status, string? Error);

public record BulkProductActionCommand(Guid ShopId, IReadOnlyList<Guid> ProductIds, SellerProductAction Action) : IRequest<IReadOnlyList<BulkProductResultDto>>;

public sealed class BulkProductActionValidator : AbstractValidator<BulkProductActionCommand>
{
    public const int Max = 100;

    public BulkProductActionValidator() =>
        RuleFor(x => x.ProductIds).NotEmpty().WithMessage("Chọn ít nhất một sản phẩm.").Must(i => i.Count <= Max).WithMessage($"Mỗi lần tối đa {Max} sản phẩm.");
}

/// <summary>Ẩn / hiện / xoá / gửi duyệt many products: each one on its own (one refusal does not stop the rest).</summary>
public sealed class BulkProductActionHandler(IApplicationDbContext db, ISender sender) : IRequestHandler<BulkProductActionCommand, IReadOnlyList<BulkProductResultDto>>
{
    public async Task<IReadOnlyList<BulkProductResultDto>> Handle(BulkProductActionCommand request, CancellationToken ct)
    {
        var results = new List<BulkProductResultDto>();
        foreach (var id in request.ProductIds.Distinct())
        {
            try
            {
                var status = await sender.Send(new ChangeProductStatusCommand(request.ShopId, id, request.Action), ct);
                results.Add(new BulkProductResultDto(id, true, status.ToString(), null));
            }
            catch (Exception ex) when (ex is ConflictException or NotFoundException or Domain.Common.BusinessRuleException)
            {
                db.ClearTracking();
                results.Add(new BulkProductResultDto(id, false, null, ex.Message));
            }
        }
        return results;
    }
}

public record CopyProductCommand(Guid ShopId, Guid ProductId) : IRequest<Guid>;

/// <summary>
/// "Sao chép": a new draft with the same info, attributes, variants, media and prices — stock 0 and no seller SKU codes
/// (those are the seller's own warehouse codes), so nothing can be sold twice by mistake.
/// </summary>
public sealed class CopyProductHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<CopyProductCommand, Guid>
{
    public async Task<Guid> Handle(CopyProductCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.ProductManage, ct);
        var source = await ProductLoader.LoadForEditAsync(db, request.ShopId, request.ProductId, ct);
        var name = $"Bản sao - {source.Name}";
        if (name.Length > Product.MaxNameLength) name = name[..Product.MaxNameLength];
        var copy = new Product(request.ShopId);
        copy.SetInfo(source.CategoryId, source.BrandId, name, Slug.From(name), source.Description, source.Condition, source.WeightG, source.LengthMm,
            source.WidthMm, source.HeightMm, source.IsPreorder, source.PreorderDays);
        copy.SetAttributes(source.Attributes.Select(a => (a.AttributeId, (IReadOnlyList<string>)a.Values.ToList())));
        copy.SetPurchaseLimit(source.MaxPerBuyer);
        copy.ShipFrom(source.WarehouseId);
        copy.LimitCarriers(source.CarrierCodes);
        var options = source.Tiers.SelectMany(t => t.Options).ToDictionary(o => o.Id);
        string? Value(Guid? id) => id is { } v && options.TryGetValue(v, out var o) ? o.Value : null;
        copy.SetVariants(
            source.Tiers.OrderBy(t => t.TierIndex).Select(t => new TierSpec(t.Name,
                t.Options.Where(o => o.IsActive).OrderBy(o => o.SortOrder).Select(o => new OptionSpec(o.Value, o.ImageUrl)).ToList()))
                .Where(t => t.Options.Count > 0).ToList(),
            source.Skus.Select(s => new SkuSpec(Value(s.Option1Id), Value(s.Option2Id), null, s.Price, s.OriginalPrice, 0, s.WeightG, s.IsActive, s.Size)).ToList());
        copy.SetMedia(source.Media.OrderBy(m => m.SortOrder)
            .Select(m => new MediaSpec(m.Type, m.AssetId, m.Url, m.VariantOptionId is { } oid && options.TryGetValue(oid, out var o) ? o.Value : null)).ToList());
        db.Products.Add(copy);
        await db.SaveChangesAsync(ct);
        return copy.Id;
    }
}

// ---------- Inventory ----------

public record UpdateSkuQuickCommand(Guid ShopId, Guid SkuId, long? Price, long? OriginalPrice, int? Stock) : IRequest<ProductSkuDto>;

public sealed class UpdateSkuQuickHandler(IApplicationDbContext db, SellerAccess access, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<UpdateSkuQuickCommand, ProductSkuDto>
{
    public async Task<ProductSkuDto> Handle(UpdateSkuQuickCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.InventoryManage, ct);
        var productId = await db.Skus.Where(s => s.Id == request.SkuId && db.Products.Any(p => p.Id == s.ProductId && p.ShopId == request.ShopId))
            .Select(s => (Guid?)s.ProductId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Không tìm thấy SKU.");
        var product = await ProductLoader.LoadForEditAsync(db, request.ShopId, productId, ct);
        if (!product.IsEditableBySeller) throw new ConflictException("Sản phẩm đang bị khoá hoặc đã xoá.", "PRODUCT_LOCKED");

        // Quick edit goes through the same domain rules as the full editor
        var options = product.Tiers.SelectMany(t => t.Options).ToDictionary(o => o.Id, o => o.Value);
        var liveOptions = product.Tiers.SelectMany(t => t.Options).Where(o => o.IsActive).Select(o => o.Id).ToHashSet();
        string? Val(Guid? id) => id is { } v ? options[v] : null;
        // Every SKU of a still-offered combination (switched-off ones keep their flag)
        var current = product.Skus.Where(s => (s.Option1Id is null || liveOptions.Contains(s.Option1Id.Value))
                                              && (s.Option2Id is null || liveOptions.Contains(s.Option2Id.Value))).ToList();
        if (current.All(s => s.Id != request.SkuId)) throw new NotFoundException("Không tìm thấy SKU.");
        var specs = current.Select(s => s.Id == request.SkuId
            ? new SkuSpec(Val(s.Option1Id), Val(s.Option2Id), s.SellerSku, request.Price ?? s.Price,
                request.OriginalPrice ?? Math.Max(s.OriginalPrice, request.Price ?? s.Price), request.Stock ?? s.Stock, s.WeightG, s.IsActive, s.Size)
            : new SkuSpec(Val(s.Option1Id), Val(s.Option2Id), s.SellerSku, s.Price, s.OriginalPrice, s.Stock, s.WeightG, s.IsActive, s.Size)).ToList();
        var tiers = product.Tiers.OrderBy(t => t.TierIndex).Where(t => t.Options.Any(o => o.IsActive))
            .Select(t => new TierSpec(t.Name, t.Options.Where(o => o.IsActive).OrderBy(o => o.SortOrder)
                .Select(o => new OptionSpec(o.Value, o.ImageUrl)).ToList())).ToList();
        foreach (var (sku, delta) in product.SetVariants(tiers, specs))
            db.InventoryMovements.Add(new InventoryMovement(sku.Id, delta, 0, InventoryReason.SellerEdit, "sku", sku.Id,
                currentUser.UserId, "Sửa nhanh", clock.UtcNow));
        await db.SaveChangesAsync(ct);

        var updated = product.Skus.Single(s => s.Id == request.SkuId);
        return new ProductSkuDto(updated.Id, Val(updated.Option1Id), Val(updated.Option2Id), updated.SellerSku, updated.Price,
            updated.OriginalPrice, updated.Stock, updated.Reserved, updated.Available, updated.WeightG, updated.IsActive);
    }
}

public record AdjustStockCommand(Guid ShopId, Guid SkuId, int Delta, string? Note) : IRequest<int>;

public sealed class AdjustStockValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockValidator()
    {
        RuleFor(x => x.Delta).NotEqual(0).WithMessage("Số lượng điều chỉnh phải khác 0.")
            .InclusiveBetween(-1_000_000, 1_000_000).WithMessage("Số lượng điều chỉnh không hợp lệ.");
        RuleFor(x => x.Note).MaximumLength(200).WithMessage("Ghi chú tối đa 200 ký tự.");
    }
}

/// <summary>
/// +N (goods received) or −N (damage, loss). One conditional UPDATE: parallel adjustments can never push stock
/// below what is reserved for orders, and no read-then-write race exists.
/// </summary>
public sealed class AdjustStockHandler(IApplicationDbContext db, SellerAccess access, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : IRequestHandler<AdjustStockCommand, int>
{
    public async Task<int> Handle(AdjustStockCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.InventoryManage, ct);
        var owned = await db.Skus.AnyAsync(s => s.Id == request.SkuId && db.Products.Any(p => p.Id == s.ProductId && p.ShopId == request.ShopId
            && p.Status != ProductStatus.Deleted && p.Status != ProductStatus.Banned), ct);
        if (!owned) throw new NotFoundException("Không tìm thấy SKU.");

        var updated = await db.Skus
            .Where(s => s.Id == request.SkuId && s.Stock + request.Delta >= s.Reserved)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Stock, x => x.Stock + request.Delta), ct);
        if (updated == 0) throw new ConflictException("Không thể giảm tồn kho xuống dưới số đang giữ cho đơn.", "STOCK_BELOW_RESERVED");

        db.InventoryMovements.Add(new InventoryMovement(request.SkuId, request.Delta, 0, InventoryReason.SellerAdjust, "sku", request.SkuId,
            currentUser.UserId, request.Note, clock.UtcNow));
        // Set-based update bypassed the change tracker: tell the search index explicitly ("còn hàng" may have changed)
        var productId = await db.Skus.Where(s => s.Id == request.SkuId).Select(s => s.ProductId).SingleAsync(ct);
        outbox.Enqueue(OutboxTypes.SearchSyncProducts, new SearchSyncProductsPayload([productId]));
        await db.SaveChangesAsync(ct);
        return await db.Skus.Where(s => s.Id == request.SkuId).Select(s => s.Stock).SingleAsync(ct);
    }
}

public record InventoryMovementDto(Guid Id, int DeltaStock, int DeltaReserved, InventoryReason Reason, string? Note, Guid? ActorId, DateTimeOffset OccurredAt);

public record ListInventoryMovementsQuery(Guid ShopId, Guid SkuId, int Page = 1, int PageSize = PagingLimits.DefaultPageSize)
    : IRequest<PagedResult<InventoryMovementDto>>, IPagedRequest;

public sealed class ListInventoryMovementsValidator : AbstractValidator<ListInventoryMovementsQuery>
{
    public ListInventoryMovementsValidator() => this.ApplyPagingRules();
}

public sealed class ListInventoryMovementsHandler(IApplicationDbContext db, SellerAccess access)
    : IRequestHandler<ListInventoryMovementsQuery, PagedResult<InventoryMovementDto>>
{
    public async Task<PagedResult<InventoryMovementDto>> Handle(ListInventoryMovementsQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.ProductView, ct);
        var owned = await db.Skus.IgnoreQueryFilters().AnyAsync(s => s.Id == request.SkuId
            && db.Products.IgnoreQueryFilters().Any(p => p.Id == s.ProductId && p.ShopId == request.ShopId), ct);
        if (!owned) throw new NotFoundException("Không tìm thấy SKU.");

        return await db.InventoryMovements.AsNoTracking().Where(m => m.SkuId == request.SkuId)
            .OrderByDescending(m => m.OccurredAt).ThenByDescending(m => m.Id)
            .ToPagedResultAsync(m => new InventoryMovementDto(m.Id, m.DeltaStock, m.DeltaReserved, m.Reason, m.Note, m.ActorId, m.OccurredAt),
                request, ct);
    }
}

// ---------- Category suggestion from the product name ----------

public record SuggestCategoriesQuery(string Name) : IRequest<IReadOnlyList<CategorySuggestionDto>>;

public record CategorySuggestionDto(Guid Id, IReadOnlyList<string> Path);

public sealed class SuggestCategoriesHandler(IApplicationDbContext db) : IRequestHandler<SuggestCategoriesQuery, IReadOnlyList<CategorySuggestionDto>>
{
    public async Task<IReadOnlyList<CategorySuggestionDto>> Handle(SuggestCategoriesQuery request, CancellationToken ct)
    {
        var words = Slug.Fold(request.Name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2).ToHashSet();
        if (words.Count == 0) return [];
        var all = await db.Categories.AsNoTracking().Where(c => c.IsActive).ToListAsync(ct);
        var parents = all.Where(c => c.ParentId is not null).Select(c => c.ParentId!.Value).ToHashSet();

        IReadOnlyList<string> PathOf(Domain.Catalog.Category c)
        {
            var path = new List<string>();
            for (var cur = c; cur is not null; cur = all.FirstOrDefault(x => x.Id == cur.ParentId)) path.Insert(0, cur.Name);
            return path;
        }

        // Score leaves by how many words of the name appear in the leaf's full path
        return all.Where(c => !parents.Contains(c.Id))
            .Select(c => (c, path: PathOf(c)))
            .Select(x => (x.c, x.path, score: Slug.Fold(string.Join(' ', x.path)).Split(' ').Count(words.Contains)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score).ThenBy(x => x.c.Level).ThenBy(x => x.c.Name)
            .Take(5)
            .Select(x => new CategorySuggestionDto(x.c.Id, x.path))
            .ToList();
    }
}
