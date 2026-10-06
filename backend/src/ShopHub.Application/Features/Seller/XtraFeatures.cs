using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Finance;
using ShopHub.Application.Security;
using ShopHub.Domain.Finance;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Seller;

// Chương trình dịch vụ (spec 3.9): Freeship Xtra / Voucher Xtra. Joining makes the platform's Xtra vouchers apply to the
// shop's products; every order placed while in the programme carries its service fee at settlement.

public record XtraProgramDto(XtraProgram Program, bool Joined, DateTimeOffset? Since, int RateBp);

public record ShopXtraQuery(Guid ShopId) : IRequest<IReadOnlyList<XtraProgramDto>>;

public sealed class ShopXtraHandler(IApplicationDbContext db, SellerAccess access, FeeSchedule fees, IClock clock)
    : IRequestHandler<ShopXtraQuery, IReadOnlyList<XtraProgramDto>>
{
    public async Task<IReadOnlyList<XtraProgramDto>> Handle(ShopXtraQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        var shop = await db.Shops.AsNoTracking().FirstAsync(s => s.Id == request.ShopId, ct);
        var now = clock.UtcNow;
        return
        [
            new(XtraProgram.FreeshipXtra, shop.FreeshipXtraSince is not null, shop.FreeshipXtraSince, await fees.RateAsync(FeeType.FreeshipXtra, null, now, ct)),
            new(XtraProgram.VoucherXtra, shop.VoucherXtraSince is not null, shop.VoucherXtraSince, await fees.RateAsync(FeeType.VoucherXtra, null, now, ct)),
        ];
    }
}

public record SetShopXtraCommand(Guid ShopId, XtraProgram Program, bool Join) : IRequest<Unit>;

/// <summary>Join / leave: applies to orders placed from now on; orders already placed keep what they were placed with.</summary>
public sealed class SetShopXtraHandler(IApplicationDbContext db, SellerAccess access, ShopPenaltyService penalties, IClock clock)
    : IRequestHandler<SetShopXtraCommand, Unit>
{
    public async Task<Unit> Handle(SetShopXtraCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        if (request.Join)
        {
            var penalty = await penalties.OfShopAsync(request.ShopId, ct);
            if (penalty.Level >= PenaltyLevel.CampaignBan)
                throw new Common.ConflictException($"Shop có {penalty.Points} điểm phạt (từ {penalty.CampaignBanAt} điểm không được tham gia chương trình của sàn).",
                    "PENALTY_BAN");
        }
        var shop = await db.Shops.FirstAsync(s => s.Id == request.ShopId, ct);
        shop.SetXtra(request.Program, request.Join, clock.UtcNow);
        // One column of its own: written even if counters changed the row meanwhile
        await db.SaveOwnChangesAsync(ct);
        return Unit.Value;
    }
}
