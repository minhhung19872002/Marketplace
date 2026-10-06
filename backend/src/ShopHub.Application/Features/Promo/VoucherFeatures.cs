using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Checkout;
using ShopHub.Application.Features.Seller;
using ShopHub.Application.Features.Storefront;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Promo;

namespace ShopHub.Application.Features.Promo;

public record VoucherInput(
    string Code,
    string Name,
    VoucherType Type,
    long DiscountValue,
    int DiscountPercentBp,
    long? MaxDiscount,
    long MinOrder,
    VoucherAudience Audience,
    IReadOnlyList<Guid>? CategoryIds,
    IReadOnlyList<Guid>? ProductIds,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    int? TotalQuota,
    int PerUserLimit,
    bool IsPublic,
    VoucherChannel Channel,
    // Platform vouchers only: covers just the shops in Freeship Xtra (free shipping) / Voucher Xtra (other types)
    bool XtraOnly = false);

public record VoucherDto(
    Guid Id,
    VoucherOwner Owner,
    Guid? ShopId,
    string? ShopName,
    string Code,
    string Name,
    VoucherType Type,
    long DiscountValue,
    int DiscountPercentBp,
    long? MaxDiscount,
    long MinOrder,
    VoucherAudience Audience,
    IReadOnlyList<Guid> CategoryIds,
    IReadOnlyList<Guid> ProductIds,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    int? TotalQuota,
    int UsedCount,
    int PerUserLimit,
    bool IsPublic,
    VoucherChannel Channel,
    bool IsActive,
    string State,
    bool XtraOnly);

public sealed class VoucherInputValidator : AbstractValidator<VoucherInput>
{
    public VoucherInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên chương trình.").MaximumLength(100).WithMessage("Tên tối đa 100 ký tự.");
        RuleFor(x => x.Code).NotEmpty().WithMessage("Vui lòng nhập mã voucher.")
            .Matches("^[A-Za-z0-9]{3,20}$").WithMessage("Mã voucher gồm 3–20 chữ cái hoặc số, không dấu, không khoảng trắng.");
        RuleFor(x => x.MinOrder).GreaterThanOrEqualTo(0).WithMessage("Giá trị đơn tối thiểu không được âm.");
        RuleFor(x => x.PerUserLimit).InclusiveBetween(1, 100).WithMessage("Lượt dùng mỗi người từ 1 đến 100.");
        RuleFor(x => x.TotalQuota).InclusiveBetween(1, 10_000_000).When(x => x.TotalQuota is not null).WithMessage("Tổng lượt dùng từ 1 trở lên.");
        RuleFor(x => x.EndAt).GreaterThan(x => x.StartAt).WithMessage("Thời gian kết thúc phải sau thời gian bắt đầu.");
        RuleFor(x => x.CategoryIds).Must(c => c is null || c.Count <= 50).WithMessage("Tối đa 50 danh mục.");
        RuleFor(x => x.ProductIds).Must(c => c is null || c.Count <= 100).WithMessage("Tối đa 100 sản phẩm.");
    }
}

internal static class VoucherMapping
{
    public static string State(Voucher v, DateTimeOffset now) =>
        !v.IsActive ? "Đã dừng" : now < v.StartAt ? "Sắp diễn ra" : now >= v.EndAt ? "Đã kết thúc"
        : v.TotalQuota is { } q && v.UsedCount >= q ? "Hết lượt" : "Đang diễn ra";

    public static VoucherDto ToDto(Voucher v, string? shopName, DateTimeOffset now) => new(v.Id, v.Owner, v.ShopId, shopName, v.Code, v.Name, v.Type,
        v.DiscountValue, v.DiscountPercentBp, v.MaxDiscount, v.MinOrder, v.Audience, v.CategoryIds, v.ProductIds, v.StartAt, v.EndAt, v.TotalQuota,
        v.UsedCount, v.PerUserLimit, v.IsPublic, v.Channel, v.IsActive, State(v, now), v.XtraOnly);

    public static void Apply(Voucher v, VoucherInput i)
    {
        v.Configure(i.Type, i.DiscountValue, i.DiscountPercentBp, i.MaxDiscount, i.MinOrder, i.Audience, i.CategoryIds ?? [], i.ProductIds ?? [],
            i.StartAt, i.EndAt, i.TotalQuota, i.PerUserLimit, i.IsPublic, i.Channel);
        v.SetXtraOnly(i.XtraOnly);
    }
}

// ---------- platform vouchers (admin) ----------

public record ListPlatformVouchersQuery(string? Q = null, int Page = 1, int PageSize = 20) : IRequest<PagedResult<VoucherDto>>, IPagedRequest;

public sealed class ListPlatformVouchersValidator : AbstractValidator<ListPlatformVouchersQuery>
{
    public ListPlatformVouchersValidator() => this.ApplyPagingRules();
}

public sealed class ListPlatformVouchersHandler(IApplicationDbContext db, IClock clock) : IRequestHandler<ListPlatformVouchersQuery, PagedResult<VoucherDto>>
{
    public async Task<PagedResult<VoucherDto>> Handle(ListPlatformVouchersQuery request, CancellationToken ct)
    {
        var q = db.Vouchers.AsNoTracking().Where(v => v.Owner == VoucherOwner.Platform);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var term = request.Q.Trim().ToUpper();
            q = q.Where(v => v.Code.Contains(term) || v.Name.ToUpper().Contains(term));
        }
        var now = clock.UtcNow;
        var page = await q.OrderByDescending(v => v.EndAt).ThenBy(v => v.Id).ToPagedResultAsync(request, ct);
        return new PagedResult<VoucherDto>(page.Items.Select(v => VoucherMapping.ToDto(v, null, now)).ToList(), page.TotalCount, page.Page, page.PageSize);
    }
}

public record SavePlatformVoucherCommand(Guid? Id, VoucherInput Input) : IRequest<VoucherDto>;

public sealed class SavePlatformVoucherValidator : AbstractValidator<SavePlatformVoucherCommand>
{
    public SavePlatformVoucherValidator() => RuleFor(x => x.Input).SetValidator(new VoucherInputValidator());
}

public sealed class SavePlatformVoucherHandler(IApplicationDbContext db, IClock clock) : IRequestHandler<SavePlatformVoucherCommand, VoucherDto>
{
    public async Task<VoucherDto> Handle(SavePlatformVoucherCommand request, CancellationToken ct)
    {
        Voucher voucher;
        if (request.Id is { } id)
        {
            voucher = await db.Vouchers.FirstOrDefaultAsync(v => v.Id == id && v.Owner == VoucherOwner.Platform, ct) ?? throw new NotFoundException("Không tìm thấy voucher.");
            if (Voucher.NormalizeCode(request.Input.Code) != voucher.Code) throw new ConflictException("Không đổi được mã của voucher đã tạo.", "CODE_FIXED");
            voucher.Rename(request.Input.Name);
        }
        else
        {
            voucher = new Voucher(VoucherOwner.Platform, null, request.Input.Code, request.Input.Name);
            db.Vouchers.Add(voucher);
        }
        VoucherMapping.Apply(voucher, request.Input);
        await db.SaveChangesAsync(ct);
        return VoucherMapping.ToDto(voucher, null, clock.UtcNow);
    }
}

public record StopPlatformVoucherCommand(Guid Id) : IRequest<VoucherDto>;

public sealed class StopPlatformVoucherHandler(IApplicationDbContext db, IClock clock) : IRequestHandler<StopPlatformVoucherCommand, VoucherDto>
{
    public async Task<VoucherDto> Handle(StopPlatformVoucherCommand request, CancellationToken ct)
    {
        var voucher = await db.Vouchers.FirstOrDefaultAsync(v => v.Id == request.Id && v.Owner == VoucherOwner.Platform, ct)
                      ?? throw new NotFoundException("Không tìm thấy voucher.");
        voucher.Stop(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return VoucherMapping.ToDto(voucher, null, clock.UtcNow);
    }
}

// ---------- shop vouchers (seller centre) ----------

public record ListShopVouchersQuery(Guid ShopId, int Page = 1, int PageSize = 20) : IRequest<PagedResult<VoucherDto>>, IPagedRequest;

public sealed class ListShopVouchersValidator : AbstractValidator<ListShopVouchersQuery>
{
    public ListShopVouchersValidator() => this.ApplyPagingRules();
}

public sealed class ListShopVouchersHandler(IApplicationDbContext db, SellerAccess access, IClock clock)
    : IRequestHandler<ListShopVouchersQuery, PagedResult<VoucherDto>>
{
    public async Task<PagedResult<VoucherDto>> Handle(ListShopVouchersQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        var now = clock.UtcNow;
        var page = await db.Vouchers.AsNoTracking().Where(v => v.Owner == VoucherOwner.Shop && v.ShopId == request.ShopId)
            .OrderByDescending(v => v.EndAt).ThenBy(v => v.Id).ToPagedResultAsync(request, ct);
        return new PagedResult<VoucherDto>(page.Items.Select(v => VoucherMapping.ToDto(v, null, now)).ToList(), page.TotalCount, page.Page, page.PageSize);
    }
}

public record SaveShopVoucherCommand(Guid ShopId, Guid? Id, VoucherInput Input) : IRequest<VoucherDto>;

public sealed class SaveShopVoucherValidator : AbstractValidator<SaveShopVoucherCommand>
{
    public SaveShopVoucherValidator() => RuleFor(x => x.Input).SetValidator(new VoucherInputValidator());
}

public sealed class SaveShopVoucherHandler(IApplicationDbContext db, SellerAccess access, IClock clock) : IRequestHandler<SaveShopVoucherCommand, VoucherDto>
{
    public async Task<VoucherDto> Handle(SaveShopVoucherCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        // Product scope may only name this shop's products
        if (request.Input.ProductIds is { Count: > 0 } productIds)
        {
            var own = await db.Products.CountAsync(p => productIds.Contains(p.Id) && p.ShopId == request.ShopId, ct);
            if (own != productIds.Distinct().Count()) throw new NotFoundException("Không tìm thấy sản phẩm trong shop.");
        }
        Voucher voucher;
        if (request.Id is { } id)
        {
            voucher = await db.Vouchers.FirstOrDefaultAsync(v => v.Id == id && v.Owner == VoucherOwner.Shop && v.ShopId == request.ShopId, ct)
                      ?? throw new NotFoundException("Không tìm thấy voucher.");
            if (Voucher.NormalizeCode(request.Input.Code) != voucher.Code) throw new ConflictException("Không đổi được mã của voucher đã tạo.", "CODE_FIXED");
            voucher.Rename(request.Input.Name);
        }
        else
        {
            voucher = new Voucher(VoucherOwner.Shop, request.ShopId, request.Input.Code, request.Input.Name);
            db.Vouchers.Add(voucher);
        }
        VoucherMapping.Apply(voucher, request.Input);
        await db.SaveChangesAsync(ct);
        return VoucherMapping.ToDto(voucher, null, clock.UtcNow);
    }
}

public record StopShopVoucherCommand(Guid ShopId, Guid Id) : IRequest<VoucherDto>;

public sealed class StopShopVoucherHandler(IApplicationDbContext db, SellerAccess access, IClock clock) : IRequestHandler<StopShopVoucherCommand, VoucherDto>
{
    public async Task<VoucherDto> Handle(StopShopVoucherCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        var voucher = await db.Vouchers.FirstOrDefaultAsync(v => v.Id == request.Id && v.Owner == VoucherOwner.Shop && v.ShopId == request.ShopId, ct)
                      ?? throw new NotFoundException("Không tìm thấy voucher.");
        voucher.Stop(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return VoucherMapping.ToDto(voucher, null, clock.UtcNow);
    }
}

// ---------- buyer: discover, save, wallet ----------

public record WalletVoucherDto(VoucherDto Voucher, bool Claimed, int UsedByMe, string? Problem);

/// <summary>Public vouchers still running — of the platform, or of one shop (shop page / cart block).</summary>
public record AvailableVouchersQuery(Guid? ShopId) : IRequest<IReadOnlyList<WalletVoucherDto>>;

public sealed class AvailableVouchersHandler(IApplicationDbContext db, VoucherEvaluator evaluator, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<AvailableVouchersQuery, IReadOnlyList<WalletVoucherDto>>
{
    public async Task<IReadOnlyList<WalletVoucherDto>> Handle(AvailableVouchersQuery request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var q = db.Vouchers.AsNoTracking().Where(v => v.IsActive && v.IsPublic && v.EndAt > now);
        q = request.ShopId is { } shopId ? q.Where(v => v.Owner == VoucherOwner.Shop && v.ShopId == shopId) : q.Where(v => v.Owner == VoucherOwner.Platform);
        var list = await q.OrderBy(v => v.EndAt).ThenBy(v => v.Id).Take(50).ToListAsync(ct);
        var userId = currentUser.UserId;
        var claimed = userId is null ? [] : await db.VoucherClaims.AsNoTracking().Where(c => c.UserId == userId).Select(c => c.VoucherId).ToListAsync(ct);
        var result = new List<WalletVoucherDto>();
        foreach (var v in list)
        {
            var used = userId is null ? 0 : await db.VoucherUserCounters.Where(c => c.VoucherId == v.Id && c.UserId == userId).Select(c => c.UsedCount).FirstOrDefaultAsync(ct);
            result.Add(new WalletVoucherDto(VoucherMapping.ToDto(v, null, now), claimed.Contains(v.Id), used,
                userId is { } uid ? await evaluator.ProblemAsync(v, uid, ct) : null));
        }
        return result;
    }
}

public record ClaimVoucherCommand(Guid VoucherId) : IRequest<Unit>;

public sealed class ClaimVoucherHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock) : IRequestHandler<ClaimVoucherCommand, Unit>
{
    public async Task<Unit> Handle(ClaimVoucherCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var now = clock.UtcNow;
        if (!await db.Vouchers.AnyAsync(v => v.Id == request.VoucherId && v.IsActive && v.IsPublic && v.EndAt > now, ct))
            throw new NotFoundException("Không tìm thấy voucher.");
        if (await db.VoucherClaims.AnyAsync(c => c.VoucherId == request.VoucherId && c.UserId == userId, ct)) return Unit.Value;
        db.VoucherClaims.Add(new VoucherClaim(request.VoucherId, userId, now));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (ConflictException ce) when (ce.Constraint == "ux_voucher_claims")
        {
            // A parallel click saved it first — same end state
        }
        return Unit.Value;
    }
}

public enum WalletTab
{
    Valid,
    ExpiringSoon,
    Used,
    Expired,
}

public record MyVouchersQuery(WalletTab Tab = WalletTab.Valid) : IRequest<IReadOnlyList<WalletVoucherDto>>;

public sealed class MyVouchersHandler(IApplicationDbContext db, VoucherEvaluator evaluator, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<MyVouchersQuery, IReadOnlyList<WalletVoucherDto>>
{
    public async Task<IReadOnlyList<WalletVoucherDto>> Handle(MyVouchersQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var now = clock.UtcNow;
        var soon = now.AddDays(3);
        var rows = await (from c in db.VoucherClaims.AsNoTracking()
                          join v in db.Vouchers.AsNoTracking() on c.VoucherId equals v.Id
                          where c.UserId == userId
                          select new
                          {
                              Voucher = v,
                              Used = db.VoucherUserCounters.Where(u => u.VoucherId == v.Id && u.UserId == userId).Select(u => u.UsedCount).FirstOrDefault(),
                              ShopName = db.Shops.Where(s => s.Id == v.ShopId).Select(s => s.Name).FirstOrDefault(),
                          }).ToListAsync(ct);
        var filtered = request.Tab switch
        {
            WalletTab.Used => rows.Where(r => r.Used >= r.Voucher.PerUserLimit),
            WalletTab.Expired => rows.Where(r => r.Used < r.Voucher.PerUserLimit && (!r.Voucher.IsActive || r.Voucher.EndAt <= now)),
            WalletTab.ExpiringSoon => rows.Where(r => r.Used < r.Voucher.PerUserLimit && r.Voucher.IsActive && r.Voucher.EndAt > now && r.Voucher.EndAt <= soon),
            _ => rows.Where(r => r.Used < r.Voucher.PerUserLimit && r.Voucher.IsActive && r.Voucher.EndAt > now),
        };
        var result = new List<WalletVoucherDto>();
        foreach (var r in filtered.OrderBy(r => r.Voucher.EndAt).ThenBy(r => r.Voucher.Id))
            result.Add(new WalletVoucherDto(VoucherMapping.ToDto(r.Voucher, r.ShopName, now), true, r.Used,
                request.Tab is WalletTab.Valid or WalletTab.ExpiringSoon ? await evaluator.ProblemAsync(r.Voucher, userId, ct) : null));
        return result;
    }
}

// ---------- ShopHub Xu ----------

public record CoinEntryDto(Guid Id, long Delta, CoinReason Reason, string? Note, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);

public record CoinWalletDto(long Balance, long ExpiringSoon, PagedResult<CoinEntryDto> History);

public record MyCoinsQuery(int Page = 1, int PageSize = 20) : IRequest<CoinWalletDto>, IPagedRequest;

public sealed class MyCoinsValidator : AbstractValidator<MyCoinsQuery>
{
    public MyCoinsValidator() => this.ApplyPagingRules();
}

public sealed class MyCoinsHandler(IApplicationDbContext db, CoinWallet wallet, ICurrentUser currentUser, IClock clock) : IRequestHandler<MyCoinsQuery, CoinWalletDto>
{
    public async Task<CoinWalletDto> Handle(MyCoinsQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var soon = clock.UtcNow.AddDays(30);
        var balance = await wallet.BalanceAsync(userId, ct);
        var expiring = await db.CoinLedger.Where(c => c.UserId == userId && c.Delta > 0 && c.ExpiresAt != null && c.ExpiresAt <= soon)
            .SumAsync(c => (long?)c.Delta, ct) ?? 0;
        var history = await db.CoinLedger.AsNoTracking().Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToPagedResultAsync(c => new CoinEntryDto(c.Id, c.Delta, c.Reason, c.Note, c.CreatedAt, c.ExpiresAt), request, ct);
        return new CoinWalletDto(balance, Math.Min(balance, expiring), history);
    }
}

/// <summary>Admin adjustment (support gestures, demo data). Taking coins away can never push the balance below zero.</summary>
public record GrantCoinsCommand(Guid UserId, long Delta, string Reason) : IRequest<long>;

public sealed class GrantCoinsValidator : AbstractValidator<GrantCoinsCommand>
{
    public GrantCoinsValidator()
    {
        RuleFor(x => x.Delta).NotEqual(0).WithMessage("Số xu phải khác 0.").InclusiveBetween(-10_000_000, 10_000_000).WithMessage("Mỗi lần tối đa 10.000.000 xu.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Vui lòng nhập lý do.").MaximumLength(300).WithMessage("Lý do tối đa 300 ký tự.");
    }
}

public sealed class GrantCoinsHandler(IApplicationDbContext db, CoinWallet wallet, ISystemParameters parameters, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<GrantCoinsCommand, long>
{
    public async Task<long> Handle(GrantCoinsCommand request, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == request.UserId, ct)) throw new NotFoundException("Không tìm thấy người dùng.");
        var now = clock.UtcNow;
        var days = await parameters.GetIntAsync(ParameterKeys.CoinExpiryDays, ct);
        return await db.InLockedTransactionAsync($"coins:{request.UserId}", async () =>
        {
            var balance = await wallet.BalanceAsync(request.UserId, ct);
            if (balance + request.Delta < 0) throw new ConflictException($"Người dùng chỉ còn {balance} xu.", "COINS_NEGATIVE");
            db.CoinLedger.Add(new CoinEntry(request.UserId, request.Delta, CoinReason.AdminGrant, "admin", currentUser.UserId,
                request.Delta > 0 ? now.AddDays(days) : null, request.Reason.Trim(), now));
            await db.SaveChangesAsync(ct);
            return balance + request.Delta;
        }, ct);
    }
}
