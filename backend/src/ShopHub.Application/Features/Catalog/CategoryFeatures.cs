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
    IReadOnlyList<CategoryNodeDto> Children,
    // Products on sale in the category and its subtree: the storefront hides industries with nothing to show
    int ProductCount = 0);

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
    /// <param name="rates">Fixed fee in force per category (the fee schedule is the source; the column is a copy)</param>
    public static IReadOnlyList<CategoryNodeDto> Build(IReadOnlyList<Category> all, Guid? parentId, IReadOnlyDictionary<Guid, int> rates,
        IReadOnlyDictionary<Guid, int>? products = null)
    {
        return all.Where(c => c.ParentId == parentId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c =>
            {
                var children = Build(all, c.Id, rates, products);
                return new CategoryNodeDto(c.Id, c.ParentId, c.Name, c.Slug, c.IconUrl, c.Level, c.SortOrder, c.IsActive,
                    rates.GetValueOrDefault(c.Id, c.CommissionRateBp), children.Count == 0, children,
                    (products?.GetValueOrDefault(c.Id) ?? 0) + children.Sum(x => x.ProductCount));
            })
            .ToList();
    }

    public static CategoryAttributeDto ToDto(CategoryAttribute a) =>
        new(a.Id, a.CategoryId, a.Name, a.InputType, a.Unit, a.IsRequired, a.IsFilterable, a.Options, a.SortOrder);
}

// ---------- Public ----------

public record GetCategoryTreeQuery(bool IncludeInactive = false) : IRequest<IReadOnlyList<CategoryNodeDto>>;

public sealed class GetCategoryTreeHandler(IApplicationDbContext db, IClock clock) : IRequestHandler<GetCategoryTreeQuery, IReadOnlyList<CategoryNodeDto>>
{
    public async Task<IReadOnlyList<CategoryNodeDto>> Handle(GetCategoryTreeQuery request, CancellationToken ct)
    {
        var all = await db.Categories.AsNoTracking().Where(c => request.IncludeInactive || c.IsActive).ToListAsync(ct);
        // A rate scheduled ahead shows once it starts (L145): read the schedule, not the copy saved with the form
        var schedule = new Finance.FeeSchedule(db);
        var now = clock.UtcNow;
        var rates = new Dictionary<Guid, int>();
        foreach (var c in all) rates[c.Id] = await schedule.RateAsync(Domain.Finance.FeeType.Fixed, c.Id, now, ct);
        var products = await db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Active)
            .GroupBy(p => p.CategoryId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return CategoryTree.Build(all, null, rates, products);
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
        // Same bound as the fee schedule (ck_fee_rules_rate), which every change of this rate goes through (L145)
        RuleFor(x => x.CommissionRateBp).InclusiveBetween(0, 5_000).WithMessage("Phí cố định phải từ 0% đến 50%.");
    }
}

public sealed class SaveCategoryHandler(IApplicationDbContext db, IClock clock) : IRequestHandler<SaveCategoryCommand, Guid>
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

        // Slugs are global (URL /danh-muc/{slug}): suffix -2, -3… when another category already uses it
        var baseSlug = Slug.From(request.Name);
        var slug = baseSlug;
        for (var n = 2; await db.Categories.AnyAsync(c => c.Slug == slug && c.Id != request.Id, ct); n++) slug = $"{baseSlug}-{n}";
        Category category;
        if (request.Id is { } id)
        {
            category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Không tìm thấy danh mục.");
            if (request.ParentId != category.ParentId)
                await EnsureMovableAsync(category, request.ParentId, level, ct);
            category.Rename(request.Name, slug);
            category.SetIcon(request.IconUrl);
            category.SetSortOrder(request.SortOrder);
            var inForce = await new Finance.FeeSchedule(db).RateAsync(Domain.Finance.FeeType.Fixed, category.Id, clock.UtcNow, ct);
            if (inForce != request.CommissionRateBp) await StartFixedFeeAsync(category.Id, request.CommissionRateBp, ct);
            category.SetCommission(request.CommissionRateBp);
            category.SetActive(request.IsActive);
            if (request.ParentId != category.ParentId) category.MoveTo(request.ParentId, level);
        }
        else
        {
            category = new Category(request.ParentId, level, request.Name, slug, request.IconUrl, request.SortOrder, request.CommissionRateBp);
            category.SetActive(request.IsActive);
            db.Categories.Add(category);
            // Inherits the parent's fixed fee unless a different one was typed
            var inherited = await new Finance.FeeSchedule(db).RateAsync(Domain.Finance.FeeType.Fixed, request.ParentId, clock.UtcNow, ct);
            if (inherited != request.CommissionRateBp)
            {
                await db.SaveChangesAsync(ct);
                await StartFixedFeeAsync(category.Id, request.CommissionRateBp, ct);
            }
        }

        await db.SaveChangesAsync(ct);
        return category.Id;
    }

    /// <summary>The fee schedule is the source of truth for fees: a new fixed fee for this category starts now (old orders keep theirs).</summary>
    private async Task StartFixedFeeAsync(Guid categoryId, int rateBp, CancellationToken ct) =>
        await Finance.FeeSchedule.StartAsync(db, categoryId, Domain.Finance.FeeType.Fixed, rateBp, clock.UtcNow, "Đổi phí cố định ở màn danh mục",
            clock.UtcNow, ct);

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
