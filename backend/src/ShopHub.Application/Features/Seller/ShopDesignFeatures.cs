using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Media;
using ShopHub.Application.Security;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Common;
using ShopHub.Domain.Media;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Seller;

// Danh mục của shop & trang trí shop (III.9) — Kênh Người Bán side.

public record ShopCategoryDto(Guid Id, string Name, int SortOrder, bool IsVisible, int ProductCount);

public record ListShopCategoriesQuery(Guid ShopId) : IRequest<IReadOnlyList<ShopCategoryDto>>;

public sealed class ListShopCategoriesHandler(IApplicationDbContext db, SellerAccess access)
    : IRequestHandler<ListShopCategoriesQuery, IReadOnlyList<ShopCategoryDto>>
{
    public async Task<IReadOnlyList<ShopCategoryDto>> Handle(ListShopCategoriesQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.ProductView, ct);
        return await db.ShopCategories.AsNoTracking().Where(c => c.ShopId == request.ShopId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new ShopCategoryDto(c.Id, c.Name, c.SortOrder, c.IsVisible,
                db.ShopCategoryProducts.Count(x => x.ShopCategoryId == c.Id && db.Products.Any(p => p.Id == x.ProductId))))
            .ToListAsync(ct);
    }
}

public record SaveShopCategoryCommand(Guid ShopId, Guid? Id, string Name, int SortOrder, bool IsVisible) : IRequest<Guid>;

public sealed class SaveShopCategoryValidator : AbstractValidator<SaveShopCategoryCommand>
{
    public SaveShopCategoryValidator() =>
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên danh mục.")
            .MaximumLength(ShopCategory.MaxNameLength).WithMessage($"Tên danh mục tối đa {ShopCategory.MaxNameLength} ký tự.");
}

public sealed class SaveShopCategoryHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<SaveShopCategoryCommand, Guid>
{
    public async Task<Guid> Handle(SaveShopCategoryCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.ProductManage, ct);
        var name = request.Name.Trim();
        if (await db.ShopCategories.AnyAsync(c => c.ShopId == request.ShopId && c.Id != request.Id && c.Name.ToLower() == name.ToLower(), ct))
            throw new ConflictException("Shop đã có danh mục cùng tên.", "SHOP_CATEGORY_EXISTS");
        ShopCategory category;
        if (request.Id is { } id)
        {
            category = await db.ShopCategories.FirstOrDefaultAsync(c => c.Id == id && c.ShopId == request.ShopId, ct)
                       ?? throw new NotFoundException("Không tìm thấy danh mục.");
            category.Update(name, request.SortOrder, request.IsVisible);
        }
        else
        {
            if (await db.ShopCategories.CountAsync(c => c.ShopId == request.ShopId, ct) >= ShopCategory.MaxPerShop)
                throw new ConflictException($"Mỗi shop có tối đa {ShopCategory.MaxPerShop} danh mục.", "SHOP_CATEGORY_LIMIT");
            category = new ShopCategory(request.ShopId, name, request.SortOrder);
            category.Update(name, request.SortOrder, request.IsVisible);
            db.ShopCategories.Add(category);
        }
        await db.SaveChangesAsync(ct);
        return category.Id;
    }
}

public record DeleteShopCategoryCommand(Guid ShopId, Guid Id) : IRequest<Unit>;

public sealed class DeleteShopCategoryHandler(IApplicationDbContext db, SellerAccess access, IClock clock) : IRequestHandler<DeleteShopCategoryCommand, Unit>
{
    public async Task<Unit> Handle(DeleteShopCategoryCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.ProductManage, ct);
        var category = await db.ShopCategories.FirstOrDefaultAsync(c => c.Id == request.Id && c.ShopId == request.ShopId, ct)
                       ?? throw new NotFoundException("Không tìm thấy danh mục.");
        category.DeletedAt = clock.UtcNow;
        db.ShopCategoryProducts.RemoveRange(await db.ShopCategoryProducts.Where(x => x.ShopCategoryId == category.Id).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public record ShopCategoryMembersQuery(Guid ShopId, Guid CategoryId) : IRequest<IReadOnlyList<DecorationProductDto>>;

public sealed class ShopCategoryMembersHandler(IApplicationDbContext db, SellerAccess access)
    : IRequestHandler<ShopCategoryMembersQuery, IReadOnlyList<DecorationProductDto>>
{
    public async Task<IReadOnlyList<DecorationProductDto>> Handle(ShopCategoryMembersQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.ProductView, ct);
        if (!await db.ShopCategories.AnyAsync(c => c.Id == request.CategoryId && c.ShopId == request.ShopId, ct))
            throw new NotFoundException("Không tìm thấy danh mục.");
        return await (from x in db.ShopCategoryProducts
                      join p in db.Products on x.ProductId equals p.Id
                      where x.ShopCategoryId == request.CategoryId
                      orderby x.SortOrder, x.Id
                      select new DecorationProductDto(p.Id, p.Name,
                          p.Media.Where(m => m.Type == MediaType.Image).OrderBy(m => m.SortOrder).Select(m => m.Url).FirstOrDefault(), p.Status))
            .AsNoTracking().ToListAsync(ct);
    }
}

public record SetShopCategoryProductsCommand(Guid ShopId, Guid CategoryId, IReadOnlyList<Guid> ProductIds) : IRequest<int>;

public sealed class SetShopCategoryProductsValidator : AbstractValidator<SetShopCategoryProductsCommand>
{
    public const int Max = 500;

    public SetShopCategoryProductsValidator() =>
        RuleFor(x => x.ProductIds).NotNull().WithMessage("Thiếu danh sách sản phẩm.")
            .Must(p => p is null || p.Count <= Max).WithMessage($"Mỗi danh mục tối đa {Max} sản phẩm.");
}

/// <summary>Replaces the products of one shop category, in the given order; only the shop's own (not deleted) products.</summary>
public sealed class SetShopCategoryProductsHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<SetShopCategoryProductsCommand, int>
{
    public async Task<int> Handle(SetShopCategoryProductsCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.ProductManage, ct);
        if (!await db.ShopCategories.AnyAsync(c => c.Id == request.CategoryId && c.ShopId == request.ShopId, ct))
            throw new NotFoundException("Không tìm thấy danh mục.");
        var wanted = request.ProductIds.Distinct().ToList();
        var own = await db.Products.Where(p => wanted.Contains(p.Id) && p.ShopId == request.ShopId).Select(p => p.Id).ToListAsync(ct);
        if (own.Count != wanted.Count) throw new NotFoundException("Có sản phẩm không thuộc shop hoặc đã bị xoá.");
        db.ShopCategoryProducts.RemoveRange(await db.ShopCategoryProducts.Where(x => x.ShopCategoryId == request.CategoryId).ToListAsync(ct));
        for (var i = 0; i < wanted.Count; i++) db.ShopCategoryProducts.Add(new ShopCategoryProduct(request.CategoryId, wanted[i], i));
        await db.SaveChangesAsync(ct);
        return wanted.Count;
    }
}

// ---------- Decoration ----------

/// <summary>An image of a banner block: a fresh upload (AssetId, purpose "shop") or an image already on the published layout (Url).</summary>
public record DecorationImageInput(Guid? AssetId, string? Url, string? Link);

public record DecorationBlockInput(DecorationBlockType Type, string? Title, IReadOnlyList<DecorationImageInput>? Images,
    IReadOnlyList<Guid>? ProductIds, Guid? ShopCategoryId, Guid? VideoAssetId, string? VideoUrl, string? Text);

public record DecorationProductDto(Guid Id, string Name, string? ImageUrl, ProductStatus Status);

public record ShopDecorationDto(IReadOnlyList<DecorationBlock> Blocks, DateTimeOffset? PublishedAt, IReadOnlyList<DecorationProductDto> Products,
    int MaxBlocks, int MaxBannerImages, int MaxProducts);

public record GetShopDecorationQuery(Guid ShopId) : IRequest<ShopDecorationDto>;

public sealed class GetShopDecorationHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<GetShopDecorationQuery, ShopDecorationDto>
{
    public async Task<ShopDecorationDto> Handle(GetShopDecorationQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.SettingsManage, ct);
        var decoration = await db.ShopDecorations.AsNoTracking().FirstOrDefaultAsync(d => d.ShopId == request.ShopId, ct);
        var blocks = decoration?.Blocks ?? [];
        var ids = blocks.SelectMany(b => b.ProductIds ?? []).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id) && p.ShopId == request.ShopId)
            .OrderBy(p => p.Id)
            .Select(p => new DecorationProductDto(p.Id, p.Name,
                p.Media.Where(m => m.Type == MediaType.Image).OrderBy(m => m.SortOrder).Select(m => m.Url).FirstOrDefault(), p.Status))
            .ToListAsync(ct);
        return new ShopDecorationDto(blocks, decoration?.PublishedAt, products, ShopDecoration.MaxBlocks, ShopDecoration.MaxBannerImages,
            ShopDecoration.MaxProducts);
    }
}

public record SaveShopDecorationCommand(Guid ShopId, IReadOnlyList<DecorationBlockInput> Blocks) : IRequest<Unit>;

public sealed class SaveShopDecorationValidator : AbstractValidator<SaveShopDecorationCommand>
{
    public SaveShopDecorationValidator() =>
        RuleFor(x => x.Blocks).NotNull().WithMessage("Thiếu danh sách khối.")
            .Must(b => b is null || b.Count <= ShopDecoration.MaxBlocks).WithMessage($"Tối đa {ShopDecoration.MaxBlocks} khối trang trí.");
}

/// <summary>
/// Publishes the layout. Media must be the caller's own uploads or already on the layout (no arbitrary external URLs);
/// products and categories must be the shop's own.
/// </summary>
public sealed class SaveShopDecorationHandler(IApplicationDbContext db, SellerAccess access, IObjectStorage storage, IClock clock)
    : IRequestHandler<SaveShopDecorationCommand, Unit>
{
    public async Task<Unit> Handle(SaveShopDecorationCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.SettingsManage, ct);
        var decoration = await db.ShopDecorations.FirstOrDefaultAsync(d => d.ShopId == request.ShopId, ct);
        var known = (decoration?.Blocks ?? []).SelectMany(b => (b.Images ?? []).Select(i => i.Url).Append(b.VideoUrl ?? ""))
            .Where(u => u.Length > 0).ToHashSet();

        var assetIds = request.Blocks.SelectMany(b => (b.Images ?? []).Select(i => i.AssetId).Append(b.VideoAssetId))
            .Where(i => i is not null).Select(i => i!.Value).ToList();
        var assets = assetIds.Count == 0 ? [] : await MediaUrls.LoadOwnedAsync(db, access.UserId, "shop", assetIds, ct);

        var productIds = request.Blocks.SelectMany(b => b.ProductIds ?? []).Distinct().ToList();
        if (productIds.Count > 0 && await db.Products.CountAsync(p => productIds.Contains(p.Id) && p.ShopId == request.ShopId, ct) != productIds.Count)
            throw new NotFoundException("Có sản phẩm không thuộc shop hoặc đã bị xoá.");
        var categoryIds = request.Blocks.Where(b => b.ShopCategoryId is not null).Select(b => b.ShopCategoryId!.Value).Distinct().ToList();
        if (categoryIds.Count > 0 && await db.ShopCategories.CountAsync(c => categoryIds.Contains(c.Id) && c.ShopId == request.ShopId, ct) != categoryIds.Count)
            throw new NotFoundException("Không tìm thấy danh mục của shop.");

        string Media(Guid? assetId, string? url, MediaKind kind)
        {
            if (assetId is { } id)
            {
                var a = assets[id];
                if (a.Kind != kind) throw new BusinessRuleException(kind == MediaKind.Video ? "Khối video cần một tệp video." : "Banner chỉ nhận ảnh.");
                return kind == MediaKind.Video ? storage.PublicUrl(a.Bucket, a.ObjectKey) : storage.PublicUrl(a.Bucket, ImageSizes.Key(a.ObjectKey, ImageSizes.Large));
            }
            if (url is not null && known.Contains(url)) return url;
            throw new BusinessRuleException("Ảnh / video phải được tải lên từ Kênh Người Bán.");
        }

        var blocks = request.Blocks.Select(b => b.Type switch
        {
            DecorationBlockType.Banner => new DecorationBlock(b.Type, Clean(b.Title),
                (b.Images ?? []).Select(i => new DecorationImage(Media(i.AssetId, i.Url, MediaKind.Image), Clean(i.Link))).ToList(), null, null, null, null),
            DecorationBlockType.Products => new DecorationBlock(b.Type, Clean(b.Title), null, (b.ProductIds ?? []).Distinct().ToList(), null, null, null),
            DecorationBlockType.Category => new DecorationBlock(b.Type, Clean(b.Title), null, null, b.ShopCategoryId, null, null),
            DecorationBlockType.Video => new DecorationBlock(b.Type, Clean(b.Title), null, null, null,
                b.VideoAssetId is null && b.VideoUrl is null ? null : Media(b.VideoAssetId, b.VideoUrl, MediaKind.Video), null),
            _ => new DecorationBlock(b.Type, Clean(b.Title), null, null, null, null, b.Text?.Trim()),
        }).ToList();

        if (decoration is null)
        {
            decoration = new ShopDecoration(request.ShopId);
            db.ShopDecorations.Add(decoration);
        }
        decoration.Publish(blocks, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
