using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Domain.Finance;

namespace ShopHub.Application.Features.Finance;

/// <summary>
/// Fee schedule lookup (spec 3.9, VI.7): the rate of a fee type for a category at a moment is the rule of the category
/// itself, else of its nearest ancestor, else the rule for every category — whichever was valid at that moment.
/// Orders are charged the rates in force when they were placed.
/// </summary>
public sealed class FeeSchedule(IApplicationDbContext db)
{
    private List<FeeRule>? _rules;
    private Dictionary<Guid, Guid?>? _parents;

    private async Task LoadAsync(CancellationToken ct)
    {
        _rules ??= await db.FeeRules.AsNoTracking().ToListAsync(ct);
        _parents ??= await db.Categories.AsNoTracking().IgnoreQueryFilters().ToDictionaryAsync(c => c.Id, c => c.ParentId, ct);
    }

    public async Task<int> RateAsync(FeeType type, Guid? categoryId, DateTimeOffset at, CancellationToken ct)
    {
        await LoadAsync(ct);
        for (var c = categoryId; c is not null; c = _parents!.GetValueOrDefault(c.Value))
        {
            var rule = _rules!.FirstOrDefault(r => r.FeeType == type && r.CategoryId == c && r.AppliesAt(at));
            if (rule is not null) return rule.RateBp;
        }
        return _rules!.FirstOrDefault(r => r.FeeType == type && r.CategoryId == null && r.AppliesAt(at))?.RateBp ?? 0;
    }

    /// <summary>
    /// Starts a new rate for a scope at <paramref name="from"/>: the rule open at that time stops there. Must run in a
    /// transaction holding the fee lock; a rule already scheduled after <paramref name="from"/> must be removed first.
    /// </summary>
    public static async Task<FeeRule> StartAsync(IApplicationDbContext db, Guid? categoryId, FeeType type, int rateBp, DateTimeOffset from, string? note,
        DateTimeOffset now, CancellationToken ct)
    {
        await db.LockAsync("finance:fee-rules", ct);
        var open = await db.FeeRules.Where(r => r.CategoryId == categoryId && r.FeeType == type && r.ValidTo == null).FirstOrDefaultAsync(ct);
        if (open is not null)
        {
            if (from <= open.ValidFrom)
                throw new ConflictException("Ngày hiệu lực phải sau ngày bắt đầu của biểu phí đang áp dụng.", "FEE_RULE_ORDER");
            open.EndAt(from);
            // The partial unique index (one open rule per scope) is checked per statement: close the old rule first
            await db.SaveChangesAsync(ct);
        }
        var rule = new FeeRule(categoryId, type, rateBp, from, note, now);
        db.FeeRules.Add(rule);
        return rule;
    }
}
