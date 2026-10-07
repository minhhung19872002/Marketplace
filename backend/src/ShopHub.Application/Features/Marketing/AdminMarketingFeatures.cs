using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Common;
using ShopHub.Domain.Promo;

namespace ShopHub.Application.Features.Marketing;

// ---------- platform Flash Sale slots ----------

public record AdminFlashSlotsQuery(bool Upcoming = true) : IRequest<IReadOnlyList<FlashSlotDto>>;

public sealed class AdminFlashSlotsHandler(IApplicationDbContext db, IClock clock) : IRequestHandler<AdminFlashSlotsQuery, IReadOnlyList<FlashSlotDto>>
{
    public async Task<IReadOnlyList<FlashSlotDto>> Handle(AdminFlashSlotsQuery request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var slots = await db.FlashSaleSlots.AsNoTracking().Where(s => s.Owner == FlashSaleOwner.Platform && (!request.Upcoming || s.EndAt > now))
            .OrderBy(s => s.StartAt).Take(100).ToListAsync(ct);
        var ids = slots.Select(s => s.Id).ToList();
        var items = await db.FlashSaleItems.AsNoTracking().Where(i => ids.Contains(i.SlotId)).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        var views = await FlashViews.ItemsAsync(db, items, ct);
        return slots.Select(s => new FlashSlotDto(s.Id, s.Owner, null, s.StartAt, s.EndAt, s.MinDiscountBp, s.MinRating, s.CategoryIds, s.Status,
            MarketingViews.State(s.StartAt, s.EndAt, s.Status == FlashSlotStatus.Cancelled, now), views.Where(v => v.SlotId == s.Id).ToList())).ToList();
    }
}

/// <param name="Date">Vietnam calendar day of the slot.</param>
/// <param name="Hour">One of FLASH.SLOT_HOURS (Vietnam time); the slot lasts FLASH.SLOT_LENGTH_HOURS.</param>
public record CreateFlashSlotCommand(DateOnly Date, int Hour, int MinDiscountBp, double MinRating, IReadOnlyList<Guid>? CategoryIds) : IRequest<Guid>;

public sealed class CreateFlashSlotHandler(IApplicationDbContext db, ISystemParameters parameters, IClock clock) : IRequestHandler<CreateFlashSlotCommand, Guid>
{
    public async Task<Guid> Handle(CreateFlashSlotCommand request, CancellationToken ct)
    {
        var hours = (await parameters.GetStringAsync(ParameterKeys.FlashSlotHours, ct))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToList();
        if (!hours.Contains(request.Hour)) throw new BusinessRuleException($"Khung Flash Sale chỉ mở lúc {string.Join(", ", hours.Select(h => $"{h:00}:00"))}.");
        var length = await parameters.GetIntAsync(ParameterKeys.FlashSlotLengthHours, ct);
        var start = new DateTimeOffset(request.Date.ToDateTime(new TimeOnly(request.Hour, 0)), TimeSpan.FromHours(7)).ToUniversalTime();
        var now = clock.UtcNow;
        if (start.AddHours(length) <= now) throw new BusinessRuleException("Khung giờ này đã qua.");
        if (await db.FlashSaleSlots.AnyAsync(s => s.Owner == FlashSaleOwner.Platform && s.Status == FlashSlotStatus.Open && s.StartAt == start, ct))
            throw new ConflictException("Đã có khung Flash Sale của sàn ở giờ này.", "SLOT_EXISTS");
        var slot = new FlashSaleSlot(FlashSaleOwner.Platform, null, start, start.AddHours(length), request.MinDiscountBp, request.MinRating,
            request.CategoryIds ?? [], now);
        db.FlashSaleSlots.Add(slot);
        await db.SaveChangesAsync(ct);
        return slot.Id;
    }
}

public record DecideFlashItemCommand(Guid ItemId, bool Approve, string? Reason) : IRequest<Unit>;

public sealed class DecideFlashItemValidator : AbstractValidator<DecideFlashItemCommand>
{
    public DecideFlashItemValidator() => RuleFor(x => x.Reason).NotEmpty().When(x => !x.Approve).WithMessage("Vui lòng nhập lý do từ chối.");
}

/// <summary>Duyệt đăng ký Flash Sale: an approved item gets its price programme (exclusion constraint) and its Redis counter.</summary>
public sealed class DecideFlashItemHandler(IApplicationDbContext db, FlashSaleQuota quota, IClock clock) : IRequestHandler<DecideFlashItemCommand, Unit>
{
    public async Task<Unit> Handle(DecideFlashItemCommand request, CancellationToken ct)
    {
        var item = await db.FlashSaleItems.FirstOrDefaultAsync(i => i.Id == request.ItemId, ct) ?? throw new NotFoundException("Không tìm thấy đăng ký.");
        var slot = await db.FlashSaleSlots.AsNoTracking().SingleAsync(s => s.Id == item.SlotId, ct);
        var now = clock.UtcNow;
        if (request.Approve)
        {
            if (slot.EndAt <= now) throw new ConflictException("Khung Flash Sale đã kết thúc.", "SLOT_OVER");
            item.Approve(now);
            FlashViews.GoLive(db, slot, item);
        }
        else item.Reject(request.Reason!, now);
        await db.SaveChangesAsync(ct);
        if (request.Approve) await quota.ReloadAsync(item.Id, ct);
        return Unit.Value;
    }
}

// ---------- banners ----------

public record BannerDto(Guid Id, BannerPosition Position, string Title, string ImageUrl, string Link, Guid? CategoryId, DateTimeOffset StartAt,
    DateTimeOffset EndAt, int SortOrder, bool IsActive);

public record AdminBannersQuery(BannerPosition? Position = null) : IRequest<IReadOnlyList<BannerDto>>;

public sealed class AdminBannersHandler(IApplicationDbContext db) : IRequestHandler<AdminBannersQuery, IReadOnlyList<BannerDto>>
{
    public async Task<IReadOnlyList<BannerDto>> Handle(AdminBannersQuery request, CancellationToken ct) =>
        await db.Banners.AsNoTracking().Where(b => request.Position == null || b.Position == request.Position)
            .OrderBy(b => b.Position).ThenBy(b => b.SortOrder).ThenByDescending(b => b.StartAt)
            .Select(b => new BannerDto(b.Id, b.Position, b.Title, b.ImageUrl, b.Link, b.CategoryId, b.StartAt, b.EndAt, b.SortOrder, b.IsActive))
            .ToListAsync(ct);
}

public record SaveBannerCommand(Guid? Id, BannerPosition Position, string Title, string ImageUrl, string Link, Guid? CategoryId, DateTimeOffset StartAt,
    DateTimeOffset EndAt, int SortOrder, bool IsActive) : IRequest<Guid>;

public sealed class SaveBannerHandler(IApplicationDbContext db, IClock clock) : IRequestHandler<SaveBannerCommand, Guid>
{
    public async Task<Guid> Handle(SaveBannerCommand r, CancellationToken ct)
    {
        Banner banner;
        if (r.Id is { } id)
        {
            banner = await db.Banners.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Không tìm thấy banner.");
            banner.Update(r.Title, r.ImageUrl, r.Link, r.StartAt, r.EndAt, r.SortOrder, r.CategoryId);
        }
        else
        {
            banner = new Banner(r.Position, r.Title, r.ImageUrl, r.Link, r.StartAt, r.EndAt, r.SortOrder, r.CategoryId, clock.UtcNow);
            db.Banners.Add(banner);
        }
        banner.SetActive(r.IsActive);
        await db.SaveChangesAsync(ct);
        return banner.Id;
    }
}

// ---------- campaigns ----------

public record CampaignDto(Guid Id, string Name, string Slug, DateTimeOffset StartAt, DateTimeOffset EndAt, IReadOnlyList<CampaignBlock> Blocks, bool IsActive);

public record AdminCampaignsQuery : IRequest<IReadOnlyList<CampaignDto>>;

public sealed class AdminCampaignsHandler(IApplicationDbContext db) : IRequestHandler<AdminCampaignsQuery, IReadOnlyList<CampaignDto>>
{
    public async Task<IReadOnlyList<CampaignDto>> Handle(AdminCampaignsQuery request, CancellationToken ct) =>
        (await db.Campaigns.AsNoTracking().OrderByDescending(c => c.StartAt).Take(100).ToListAsync(ct))
        .Select(c => new CampaignDto(c.Id, c.Name, c.Slug, c.StartAt, c.EndAt, c.Blocks, c.IsActive)).ToList();
}

public record SaveCampaignCommand(Guid? Id, string Name, string Slug, DateTimeOffset StartAt, DateTimeOffset EndAt, IReadOnlyList<CampaignBlock> Blocks,
    bool IsActive) : IRequest<Guid>;

public sealed class SaveCampaignHandler(IApplicationDbContext db, IClock clock) : IRequestHandler<SaveCampaignCommand, Guid>
{
    public async Task<Guid> Handle(SaveCampaignCommand r, CancellationToken ct)
    {
        Campaign campaign;
        if (r.Id is { } id)
        {
            campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Không tìm thấy chiến dịch.");
            campaign.Update(r.Name, r.Slug?.Trim().ToLowerInvariant() ?? "", r.StartAt, r.EndAt, r.Blocks ?? []);
        }
        else
        {
            campaign = new Campaign(r.Name, r.Slug?.Trim().ToLowerInvariant() ?? "", r.StartAt, r.EndAt, r.Blocks ?? [], clock.UtcNow);
            db.Campaigns.Add(campaign);
        }
        campaign.SetActive(r.IsActive);
        await db.SaveChangesAsync(ct);
        return campaign.Id;
    }
}

// ---------- Từ khoá hot thủ công (VI.6, F8) ----------

public record HotKeywordsQuery : IRequest<IReadOnlyList<string>>;

public sealed class HotKeywordsHandler(ISystemParameters parameters) : IRequestHandler<HotKeywordsQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(HotKeywordsQuery request, CancellationToken ct) =>
        System.Text.Json.JsonSerializer.Deserialize<List<string>>(await parameters.GetStringAsync(ParameterKeys.SearchHotKeywords, ct)) ?? [];
}

public record SetHotKeywordsCommand(IReadOnlyList<string> Keywords) : IRequest<IReadOnlyList<string>>;

public sealed class SetHotKeywordsValidator : AbstractValidator<SetHotKeywordsCommand>
{
    public const int Max = 10;

    public SetHotKeywordsValidator()
    {
        RuleFor(x => x.Keywords).NotNull().WithMessage("Thiếu danh sách từ khoá.")
            .Must(k => k is null || k.Count <= Max).WithMessage($"Tối đa {Max} từ khoá.");
        RuleForEach(x => x.Keywords).Must(k => !string.IsNullOrWhiteSpace(k) && k.Trim().Length <= 50)
            .WithMessage("Mỗi từ khoá từ 1 đến 50 ký tự.");
    }
}

/// <summary>
/// The curated "từ khoá hot" (shown while real search traffic is thin). Written through the parameter command, so the
/// change is audited and every instance reloads it — but the marketing role needs no right over other parameters.
/// </summary>
public sealed class SetHotKeywordsHandler(ISender sender) : IRequestHandler<SetHotKeywordsCommand, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(SetHotKeywordsCommand request, CancellationToken ct)
    {
        var keywords = request.Keywords.Select(k => k.Trim()).DistinctBy(k => k.ToLowerInvariant()).ToList();
        await sender.Send(new SystemConfig.UpdateSystemParameterCommand(ParameterKeys.SearchHotKeywords, System.Text.Json.JsonSerializer.Serialize(keywords), null), ct);
        return keywords;
    }
}
