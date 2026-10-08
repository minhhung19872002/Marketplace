using ShopHub.Domain.Common;

namespace ShopHub.Domain.Catalog;

// Three-level category tree; products may only be attached to a leaf
public class Category : AuditableEntity
{
    public const int MaxLevel = 3;

    private Category() { }

    public Category(Guid? parentId, int level, string name, string slug, string? iconUrl, int sortOrder, int commissionRateBp)
    {
        if (level is < 1 or > MaxLevel) throw new BusinessRuleException("Danh mục chỉ có tối đa 3 cấp.");
        ParentId = parentId;
        Level = level;
        Rename(name, slug);
        IconUrl = iconUrl;
        SortOrder = sortOrder;
        SetCommission(commissionRateBp);
        IsActive = true;
    }

    public Guid? ParentId { get; private set; }
    public int Level { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? IconUrl { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    /// <summary>
    /// Shown to buyers (menus, filters, top lists, sitemap). Off = sellers can still list in it, buyers do not see it —
    /// for an industry with nothing to show yet (UI G2-A7). Hides the whole subtree.
    /// </summary>
    public bool IsVisible { get; private set; } = true;

    // Fixed platform fee for orders in this category, basis points (1% = 100)
    public int CommissionRateBp { get; private set; }

    public void Rename(string name, string slug)
    {
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
    }

    public void SetIcon(string? iconUrl) => IconUrl = iconUrl;

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void SetActive(bool active) => IsActive = active;

    public void SetVisible(bool visible) => IsVisible = visible;

    public void SetCommission(int bp)
    {
        if (bp is < 0 or > 10_000) throw new BusinessRuleException("Tỉ lệ phí phải từ 0% đến 100%.");
        CommissionRateBp = bp;
    }

    public void MoveTo(Guid? parentId, int level)
    {
        if (level is < 1 or > MaxLevel) throw new BusinessRuleException("Danh mục chỉ có tối đa 3 cấp.");
        ParentId = parentId;
        Level = level;
    }
}

public enum AttributeInputType
{
    SingleSelect,
    MultiSelect,
    Text,
    Number,
}

// Industry attribute declared per leaf category (Thương hiệu, Xuất xứ, Chất liệu…)
public class CategoryAttribute : AuditableEntity
{
    private CategoryAttribute() { }

    public CategoryAttribute(Guid categoryId, string name, AttributeInputType inputType, string? unit, bool isRequired,
        bool isFilterable, IReadOnlyList<string> options, int sortOrder)
    {
        CategoryId = categoryId;
        Update(name, inputType, unit, isRequired, isFilterable, options, sortOrder);
    }

    public Guid CategoryId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public AttributeInputType InputType { get; private set; }
    public string? Unit { get; private set; }
    public bool IsRequired { get; private set; }
    public bool IsFilterable { get; private set; }
    public List<string> Options { get; private set; } = [];
    public int SortOrder { get; private set; }

    public void Update(string name, AttributeInputType inputType, string? unit, bool isRequired, bool isFilterable,
        IReadOnlyList<string> options, int sortOrder)
    {
        var isSelect = inputType is AttributeInputType.SingleSelect or AttributeInputType.MultiSelect;
        if (isSelect && options.Count == 0) throw new BusinessRuleException("Thuộc tính kiểu chọn phải có ít nhất một lựa chọn.");
        Name = name.Trim();
        InputType = inputType;
        Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
        IsRequired = isRequired;
        IsFilterable = isFilterable;
        Options = isSelect ? options.Select(o => o.Trim()).Where(o => o.Length > 0).Distinct().ToList() : [];
        SortOrder = sortOrder;
    }

    /// <summary>Vietnamese error, or null when the submitted value fits this attribute.</summary>
    public string? Check(IReadOnlyList<string> values)
    {
        var filled = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        if (filled.Count == 0) return IsRequired ? $"Vui lòng nhập {Name}." : null;
        return InputType switch
        {
            AttributeInputType.SingleSelect when filled.Count > 1 || !Options.Contains(filled[0]) => $"{Name}: lựa chọn không hợp lệ.",
            AttributeInputType.MultiSelect when filled.Any(v => !Options.Contains(v)) => $"{Name}: lựa chọn không hợp lệ.",
            AttributeInputType.Number when filled.Count > 1 || !decimal.TryParse(filled[0], System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out _) => $"{Name} phải là số.",
            AttributeInputType.Text when filled.Count > 1 || filled[0].Length > 200 => $"{Name} tối đa 200 ký tự.",
            _ => null,
        };
    }
}

public class Brand : AuditableEntity
{
    private Brand() { }

    public Brand(string name, string slug, string? logoUrl, bool isVerified)
    {
        Update(name, slug, logoUrl, isVerified);
    }

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? LogoUrl { get; private set; }
    public bool IsVerified { get; private set; }

    public void Update(string name, string slug, string? logoUrl, bool isVerified)
    {
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        LogoUrl = logoUrl;
        IsVerified = isVerified;
    }
}
