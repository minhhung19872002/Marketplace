using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Seller;
using ShopHub.Application.Security;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Promo;

namespace ShopHub.Application.Features.Marketing;

// ---------- Đăng ký chiến dịch của sàn (III.5, VI.6) ----------

public record OpenCampaignDto(Guid Id, string Name, string Slug, DateTimeOffset StartAt, DateTimeOffset EndAt, int Pending, int Approved, int Rejected);

public record CampaignRegistrationDto(Guid Id, Guid CampaignId, Guid ShopId, string ShopName, Guid ProductId, string ProductName, string? ImageUrl,
    long MinPrice, CampaignRegistrationStatus Status, string? RejectReason, DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt);

internal static class CampaignRegistrations
{
    public const int MaxPerShop = 50;

    /// <summary>Campaigns a shop can still register for: switched on, not ended, with a block that shows registered products.</summary>
    public static IQueryable<Campaign> Open(IApplicationDbContext db, DateTimeOffset now) =>
        db.Campaigns.Where(c => c.IsActive && c.EndAt > now);

    public static IQueryable<CampaignRegistrationDto> Rows(IApplicationDbContext db, IQueryable<CampaignRegistration> q) =>
        q.Select(r => new CampaignRegistrationDto(r.Id, r.CampaignId, r.ShopId,
            db.Shops.Where(s => s.Id == r.ShopId).Select(s => s.Name).FirstOrDefault() ?? "",
            r.ProductId, db.Products.IgnoreQueryFilters().Where(p => p.Id == r.ProductId).Select(p => p.Name).FirstOrDefault() ?? "",
            db.ProductMedia.Where(m => m.ProductId == r.ProductId && m.Type == MediaType.Image).OrderBy(m => m.SortOrder).Select(m => m.Url).FirstOrDefault(),
            db.Products.IgnoreQueryFilters().Where(p => p.Id == r.ProductId).Select(p => p.MinPrice).FirstOrDefault(),
            r.Status, r.RejectReason, r.CreatedAt, r.DecidedAt));
}

public record ShopCampaignsQuery(Guid ShopId) : IRequest<IReadOnlyList<OpenCampaignDto>>;

public sealed class ShopCampaignsHandler(IApplicationDbContext db, SellerAccess access, IClock clock) : IRequestHandler<ShopCampaignsQuery, IReadOnlyList<OpenCampaignDto>>
{
    public async Task<IReadOnlyList<OpenCampaignDto>> Handle(ShopCampaignsQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        var now = clock.UtcNow;
        // Blocks are jsonb: which campaigns take registrations is decided here, the counts in one query
        var campaigns = (await CampaignRegistrations.Open(db, now).AsNoTracking().OrderBy(c => c.StartAt).ThenBy(c => c.Id).ToListAsync(ct))
            .Where(c => c.Blocks.Any(b => b.Type == CampaignBlockType.Registered)).ToList();
        var ids = campaigns.Select(c => c.Id).ToList();
        var counts = await db.CampaignRegistrations.AsNoTracking().Where(r => r.ShopId == request.ShopId && ids.Contains(r.CampaignId))
            .GroupBy(r => new { r.CampaignId, r.Status }).Select(g => new { g.Key.CampaignId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        int Count(Guid id, CampaignRegistrationStatus s) => counts.FirstOrDefault(x => x.CampaignId == id && x.Status == s)?.Count ?? 0;
        return campaigns.Select(c => new OpenCampaignDto(c.Id, c.Name, c.Slug, c.StartAt, c.EndAt, Count(c.Id, CampaignRegistrationStatus.Pending),
            Count(c.Id, CampaignRegistrationStatus.Approved), Count(c.Id, CampaignRegistrationStatus.Rejected))).ToList();
    }
}

public record ShopCampaignRegistrationsQuery(Guid ShopId, Guid CampaignId) : IRequest<IReadOnlyList<CampaignRegistrationDto>>;

public sealed class ShopCampaignRegistrationsHandler(IApplicationDbContext db, SellerAccess access)
    : IRequestHandler<ShopCampaignRegistrationsQuery, IReadOnlyList<CampaignRegistrationDto>>
{
    public async Task<IReadOnlyList<CampaignRegistrationDto>> Handle(ShopCampaignRegistrationsQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        return await CampaignRegistrations.Rows(db, db.CampaignRegistrations.AsNoTracking()
                .Where(r => r.ShopId == request.ShopId && r.CampaignId == request.CampaignId).OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id))
            .ToListAsync(ct);
    }
}

public record RegisterCampaignProductsCommand(Guid ShopId, Guid CampaignId, IReadOnlyList<Guid> ProductIds) : IRequest<int>;

public sealed class RegisterCampaignProductsValidator : AbstractValidator<RegisterCampaignProductsCommand>
{
    public RegisterCampaignProductsValidator() =>
        RuleFor(x => x.ProductIds).NotEmpty().WithMessage("Chọn ít nhất một sản phẩm.")
            .Must(i => i.Count <= CampaignRegistrations.MaxPerShop).WithMessage($"Mỗi lần tối đa {CampaignRegistrations.MaxPerShop} sản phẩm.");
}

/// <summary>
/// Put the shop's selling products forward for the campaign: refused when the shop is banned from campaigns by penalty
/// points, the campaign has ended, a product is not the shop's or not on sale, or the shop already has 50 in it.
/// A refused product can be put forward again; one already pending / approved is left as it is.
/// </summary>
public sealed class RegisterCampaignProductsHandler(IApplicationDbContext db, SellerAccess access, ShopPenaltyService penalties, IClock clock)
    : IRequestHandler<RegisterCampaignProductsCommand, int>
{
    public async Task<int> Handle(RegisterCampaignProductsCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        var penalty = await penalties.OfShopAsync(request.ShopId, ct);
        if (penalty.Level >= PenaltyLevel.CampaignBan)
            throw new ConflictException($"Shop có {penalty.Points} điểm phạt (từ {penalty.CampaignBanAt} điểm không được đăng ký chiến dịch của sàn).", "PENALTY_BAN");
        var now = clock.UtcNow;
        var campaign = await CampaignRegistrations.Open(db, now).AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CampaignId, ct)
                       ?? throw new NotFoundException("Chiến dịch không tồn tại hoặc đã kết thúc.");
        if (campaign.Blocks.All(b => b.Type != CampaignBlockType.Registered))
            throw new ConflictException("Chiến dịch này không nhận đăng ký sản phẩm của shop.", "CAMPAIGN_NOT_OPEN");

        var ids = request.ProductIds.Distinct().ToList();
        var selling = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id) && p.ShopId == request.ShopId && p.Status == ProductStatus.Active)
            .Select(p => p.Id).ToListAsync(ct);
        if (selling.Count != ids.Count) throw new ConflictException("Chỉ đăng ký được sản phẩm đang bán của shop.", "PRODUCT_NOT_SELLING");

        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"campaign:{campaign.Id}:shop:{request.ShopId}", ct);
        var existing = await db.CampaignRegistrations.Where(r => r.CampaignId == campaign.Id && r.ShopId == request.ShopId).ToListAsync(ct);
        var added = 0;
        foreach (var id in ids)
        {
            var row = existing.FirstOrDefault(r => r.ProductId == id);
            if (row is null)
            {
                db.CampaignRegistrations.Add(new CampaignRegistration(campaign.Id, request.ShopId, id, now));
                added++;
            }
            else if (row.Status == CampaignRegistrationStatus.Rejected)
            {
                row.Resubmit(now);
                added++;
            }
        }
        if (existing.Count(r => r.Status != CampaignRegistrationStatus.Rejected) + added > CampaignRegistrations.MaxPerShop)
            throw new ConflictException($"Mỗi shop đăng ký tối đa {CampaignRegistrations.MaxPerShop} sản phẩm cho một chiến dịch.", "CAMPAIGN_LIMIT");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return added;
    }
}

public record WithdrawCampaignProductCommand(Guid ShopId, Guid RegistrationId) : IRequest<Unit>;

public sealed class WithdrawCampaignProductHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<WithdrawCampaignProductCommand, Unit>
{
    public async Task<Unit> Handle(WithdrawCampaignProductCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        var row = await db.CampaignRegistrations.FirstOrDefaultAsync(r => r.Id == request.RegistrationId && r.ShopId == request.ShopId, ct)
                  ?? throw new NotFoundException("Không tìm thấy đăng ký.");
        db.CampaignRegistrations.Remove(row);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ---------- admin ----------

public record AdminCampaignRegistrationsQuery(Guid CampaignId, CampaignRegistrationStatus? Status, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<CampaignRegistrationDto>>, IPagedRequest;

public sealed class AdminCampaignRegistrationsValidator : AbstractValidator<AdminCampaignRegistrationsQuery>
{
    public AdminCampaignRegistrationsValidator() => this.ApplyPagingRules();
}

public sealed class AdminCampaignRegistrationsHandler(IApplicationDbContext db)
    : IRequestHandler<AdminCampaignRegistrationsQuery, PagedResult<CampaignRegistrationDto>>
{
    public async Task<PagedResult<CampaignRegistrationDto>> Handle(AdminCampaignRegistrationsQuery request, CancellationToken ct)
    {
        var q = db.CampaignRegistrations.AsNoTracking().Where(r => r.CampaignId == request.CampaignId);
        if (request.Status is { } status) q = q.Where(r => r.Status == status);
        var total = await q.CountAsync(ct);
        var items = await CampaignRegistrations.Rows(db, q.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)).ToListAsync(ct);
        return new PagedResult<CampaignRegistrationDto>(items, total, request.Page, request.PageSize);
    }
}

public record DecideCampaignRegistrationsCommand(Guid CampaignId, IReadOnlyList<Guid> RegistrationIds, bool Approve, string? Reason) : IRequest<int>;

public sealed class DecideCampaignRegistrationsValidator : AbstractValidator<DecideCampaignRegistrationsCommand>
{
    public DecideCampaignRegistrationsValidator()
    {
        RuleFor(x => x.RegistrationIds).NotEmpty().WithMessage("Chọn ít nhất một đăng ký.").Must(i => i.Count <= 200).WithMessage("Mỗi lần tối đa 200 đăng ký.");
        RuleFor(x => x.Reason).NotEmpty().When(x => !x.Approve).WithMessage("Vui lòng nhập lý do từ chối.");
    }
}

/// <summary>Duyệt / từ chối đăng ký (kèm lý do); each change goes to the audit log.</summary>
public sealed class DecideCampaignRegistrationsHandler(IApplicationDbContext db, IClock clock) : IRequestHandler<DecideCampaignRegistrationsCommand, int>
{
    public async Task<int> Handle(DecideCampaignRegistrationsCommand request, CancellationToken ct)
    {
        var ids = request.RegistrationIds.Distinct().ToList();
        var rows = await db.CampaignRegistrations.Where(r => r.CampaignId == request.CampaignId && ids.Contains(r.Id)).ToListAsync(ct);
        if (rows.Count != ids.Count) throw new NotFoundException("Không tìm thấy đăng ký.");
        foreach (var r in rows) r.Decide(request.Approve, request.Reason, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }
}
