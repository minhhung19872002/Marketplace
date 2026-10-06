using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Domain.Catalog;

namespace ShopHub.Application.Features.Catalog;

public record CategoryNodeDto(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Slug,
    string? IconUrl,
    int Level,
    int SortOrder,
    bool IsActive,
    int CommissionRateBp,
    bool IsLeaf,
    IReadOnlyList<CategoryNodeDto> Children);

public record CategoryAttributeDto(
    Guid Id,
    Guid CategoryId,
    string Name,
    AttributeInputType InputType,
    string? Unit,
    bool IsRequired,
    bool IsFilterable,
    IReadOnlyList<string> Options,
    int SortOrder);

public record BrandDto(Guid Id, string Name, string Slug, string? LogoUrl, bool IsVerified);

internal static class CategoryTree
{
    public static IReadOnlyList<CategoryNodeDto> Build(IReadOnlyList<Category> all, Guid? parentId)
    {
        return all.Where(c => c.ParentId == parentId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c =>
            {
                var children = Build(all, c.Id);
                return new CategoryNodeDto(c.Id, c.ParentId, c.Name, c.Slug, c.IconUrl, c.Level, c.SortOrder, c.IsActive,
                    c.CommissionRateBp, children.Count == 0, children);
            })
            .ToList();
    }

    public static CategoryAttributeDto ToDto(CategoryAttribute a) =>
        new(a.Id, a.CategoryId, a.Name, a.InputType, a.Unit, a.IsRequired, a.IsFilterable, a.Options, a.SortOrder);
}

// ---------- Public ----------

public record GetCategoryTreeQuery(bool IncludeInactive = false) : IRequest<IReadOnlyList<CategoryNodeDto>>;

public sealed class GetCategoryTreeHandler(IApplicationDbContext db) : IRequestHandler<GetCategoryTreeQuery, IReadOnlyList<CategoryNodeDto>>
{
    public async Task<IReadOnlyList<CategoryNodeDto>> Handle(GetCategoryTreeQuery request, CancellationToken ct)
    {
        var all = await db.Categories.AsNoTracking().Where(c => request.IncludeInactive || c.IsActive).ToListAsync(ct);
        return CategoryTree.Build(all, null);
    }
}

public record GetCategoryAttributesQuery(Guid CategoryId) : IRequest<IReadOnlyList<CategoryAttributeDto>>;

public sealed class GetCategoryAttributesHandler(IApplicationDbContext db)
    : IRequestHandler<GetCategoryAttributesQuery, IReadOnlyList<CategoryAttributeDto>>
{
    public async Task<IReadOnlyList<CategoryAttributeDto>> Handle(GetCategoryAttributesQuery request, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId, ct)) throw new NotFoundException("Không tìm thấy danh mục.");
        var attributes = await db.CategoryAttributes.AsNoTracking().Where(a => a.CategoryId == request.CategoryId)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Id).ToListAsync(ct);
        return attributes.Select(CategoryTree.ToDto).ToList();
    }
}

public record SearchBrandsQuery(string? Q) : IRequest<IReadOnlyList<BrandDto>>;

public sealed class SearchBrandsHandler(IApplicationDbContext db) : IRequestHandler<SearchBrandsQuery, IReadOnlyList<BrandDto>>
{
    public async Task<IReadOnlyList<BrandDto>> Handle(SearchBrandsQuery request, CancellationToken ct)
    {
        var brands = db.Brands.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = Slug.From(request.Q);
            brands = brands.Where(b => b.Slug.Contains(q));
        }
        return await brands.OrderByDescending(b => b.IsVerified).ThenBy(b => b.Name).ThenBy(b => b.Id).Take(50)
            .Select(b => new BrandDto(b.Id, b.Name, b.Slug, b.LogoUrl, b.IsVerified)).ToListAsync(ct);
    }
}

// ---------- Admin: categories ----------

public record SaveCategoryCommand(Guid? Id, Guid? ParentId, string Name, string? IconUrl, int SortOrder, int CommissionRateBp, bool IsActive)
    : IRequest<Guid>;

public sealed class SaveCategoryValidator : AbstractValidator<SaveCategoryCommand>
{
    public SaveCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên danh mục.").MaximumLength(100).WithMessage("Tên tối đa 100 ký tự.");
        RuleFor(x => x.CommissionRateBp).InclusiveBetween(0, 10_000).WithMessage("Phí cố định phải từ 0% đến 100%.");
    }
}

public sealed class SaveCategoryHandler(IApplicationDbContext db) : IRequestHandler<SaveCategoryCommand, Guid>
{
    public async Task<Guid> Handle(SaveCategoryCommand request, CancellationToken ct)
    {
        var level = 1;
        if (request.ParentId is { } parentId)
        {
            var parent = await db.Categories.FirstOrDefaultAsync(c => c.Id == parentId, ct)
                ?? throw new NotFoundException("Không tìm thấy danh mục cha.");
            level = parent.Level + 1;
            if (level > Category.MaxLevel) throw new ConflictException("Danh mục chỉ có tối đa 3 cấp.", "CATEGORY_DEPTH");
            // A category that already holds products must stay a leaf
            if (await db.Products.AnyAsync(p => p.CategoryId == parentId, ct))
                throw new ConflictException("Danh mục cha đang có sản phẩm, không thể thêm danh mục con.", "CATEGORY_HAS_PRODUCTS");
        }

        var slug = Slug.From(request.Name);
        Category category;
        if (request.Id is { } id)
        {
            category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Không tìm thấy danh mục.");
            if (request.ParentId != category.ParentId)
                await EnsureMovableAsync(category, request.ParentId, level, ct);
            category.Rename(request.Name, slug);
            category.SetIcon(request.IconUrl);
            category.SetSortOrder(request.SortOrder);
            category.SetCommission(request.CommissionRateBp);
            category.SetActive(request.IsActive);
            if (request.ParentId != category.ParentId) category.MoveTo(request.ParentId, level);
        }
        else
        {
            category = new Category(request.ParentId, level, request.Name, slug, request.IconUrl, request.SortOrder, request.CommissionRateBp);
            category.SetActive(request.IsActive);
            db.Categories.Add(category);
        }

        await db.SaveChangesAsync(ct);
        return category.Id;
    }

    private async Task EnsureMovableAsync(Category category, Guid? newParentId, int newLevel, CancellationToken ct)
    {
        var all = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync(ct);
        var descendants = new HashSet<Guid>();
        var frontier = new Queue<Guid>([category.Id]);
        var depth = 0;
        while (frontier.Count > 0)
        {
            var next = new Queue<Guid>();
            foreach (var idx in frontier)
                foreach (var child in all.Where(c => c.ParentId == idx))
                    if (descendants.Add(child.Id)) next.Enqueue(child.Id);
            if (next.Count > 0) depth++;
            frontier = next;
        }
        if (newParentId is { } p && (p == category.Id || descendants.Contains(p)))
            throw new ConflictException("Không thể chuyển danh mục vào chính nó hoặc danh mục con của nó.", "CATEGORY_CYCLE");
        if (newLevel + depth > Category.MaxLevel)
            throw new ConflictException("Chuyển như vậy sẽ vượt quá 3 cấp danh mục.", "CATEGORY_DEPTH");
        if (descendants.Count > 0)
            throw new ConflictException("Hãy chuyển các danh mục con trước khi chuyển danh mục này.", "CATEGORY_HAS_CHILDREN");
    }
}

// ---------- Admin: attributes ----------

public record SaveCategoryAttributeCommand(
    Guid? Id,
    Guid CategoryId,
    string Name,
    AttributeInputType InputType,
    string? Unit,
    bool IsRequired,
    bool IsFilterable,
    IReadOnlyList<string> Options,
    int SortOrder) : IRequest<Guid>;

public sealed class SaveCategoryAttributeValidator : AbstractValidator<SaveCategoryAttributeCommand>
{
    public SaveCategoryAttributeValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên thuộc tính.").MaximumLength(100).WithMessage("Tên tối đa 100 ký tự.");
        RuleFor(x => x.Options).NotNull().WithMessage("Thiếu danh sách lựa chọn.");
    }
}

public sealed class SaveCategoryAttributeHandler(IApplicationDbContext db) : IRequestHandler<SaveCategoryAttributeCommand, Guid>
{
    public async Task<Guid> Handle(SaveCategoryAttributeCommand request, CancellationToken ct)
    {
        var all = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync(ct);
        if (all.All(c => c.Id != request.CategoryId)) throw new NotFoundException("Không tìm thấy danh mục.");
        if (all.Any(c => c.ParentId == request.CategoryId))
            throw new ConflictException("Thuộc tính chỉ khai cho danh mục lá.", "CATEGORY_NOT_LEAF");

        CategoryAttribute attribute;
        if (request.Id is { } id)
        {
            attribute = await db.CategoryAttributes.FirstOrDefaultAsync(a => a.Id == id && a.CategoryId == request.CategoryId, ct)
                ?? throw new NotFoundException("Không tìm thấy thuộc tính.");
            attribute.Update(request.Name, request.InputType, request.Unit, request.IsRequired, request.IsFilterable, request.Options, request.SortOrder);
        }
        else
        {
            attribute = new CategoryAttribute(request.CategoryId, request.Name, request.InputType, request.Unit, request.IsRequired,
                request.IsFilterable, request.Options, request.SortOrder);
            db.CategoryAttributes.Add(attribute);
        }
        await db.SaveChangesAsync(ct);
        return attribute.Id;
    }
}

public record DeleteCategoryAttributeCommand(Guid Id) : IRequest<Unit>;

public sealed class DeleteCategoryAttributeHandler(IApplicationDbContext db) : IRequestHandler<DeleteCategoryAttributeCommand, Unit>
{
    public async Task<Unit> Handle(DeleteCategoryAttributeCommand request, CancellationToken ct)
    {
        var attribute = await db.CategoryAttributes.FirstOrDefaultAsync(a => a.Id == request.Id, ct)
            ?? throw new NotFoundException("Không tìm thấy thuộc tính.");
        db.CategoryAttributes.Remove(attribute);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ---------- Admin: brands ----------

public record SaveBrandCommand(Guid? Id, string Name, string? LogoUrl, bool IsVerified) : IRequest<Guid>;

public sealed class SaveBrandValidator : AbstractValidator<SaveBrandCommand>
{
    public SaveBrandValidator() =>
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên thương hiệu.").MaximumLength(100).WithMessage("Tên tối đa 100 ký tự.");
}

public sealed class SaveBrandHandler(IApplicationDbContext db) : IRequestHandler<SaveBrandCommand, Guid>
{
    public async Task<Guid> Handle(SaveBrandCommand request, CancellationToken ct)
    {
        var slug = Slug.From(request.Name);
        Brand brand;
        if (request.Id is { } id)
        {
            brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Không tìm thấy thương hiệu.");
            brand.Update(request.Name, slug, request.LogoUrl, request.IsVerified);
        }
        else
        {
            brand = new Brand(request.Name, slug, request.LogoUrl, request.IsVerified);
            db.Brands.Add(brand);
        }
        await db.SaveChangesAsync(ct);
        return brand.Id;
    }
}
