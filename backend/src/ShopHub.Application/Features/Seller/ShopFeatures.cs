using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Media;
using ShopHub.Application.Identity;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Seller;

/// <summary>
/// Resolves "am I staff of this shop, with this permission?" inside the SQL query. Someone else's shop is
/// "not found" (404); own shop without the specific grant is 403.
/// </summary>
public sealed class SellerAccess(IApplicationDbContext db, ICurrentUser currentUser)
{
    public Guid UserId => currentUser.UserId ?? throw new AuthenticationFailedException("Bạn cần đăng nhập để tiếp tục.");

    public async Task<ShopStaff> RequireAsync(Guid shopId, string permission, CancellationToken ct)
    {
        var userId = UserId;
        var staff = await db.ShopStaff.AsNoTracking().FirstOrDefaultAsync(s => s.ShopId == shopId && s.UserId == userId, ct)
            ?? throw new NotFoundException("Không tìm thấy shop.");
        if (!staff.Has(permission)) throw new ForbiddenException("Tài khoản của bạn không có quyền này trong shop.");
        return staff;
    }
}

public record WarehouseInput(string ContactName, string Phone, string ProvinceCode, string DistrictCode, string WardCode, string Street);

public record PersonalKycInput(string LegalName, string IdCardNumber, Guid FrontAssetId, Guid BackAssetId);

public record BusinessKycInput(string LegalName, string TaxCode, Guid LicenseAssetId);

public record BankInput(string BankCode, string AccountNo, string AccountName);

public record MyShopDto(
    Guid Id,
    string Name,
    string Slug,
    ShopType Type,
    ShopStatus Status,
    ShopStaffRole Role,
    IReadOnlyList<string> Permissions,
    string? LogoUrl,
    string? RejectReason,
    string? LockReason,
    DateTimeOffset CreatedAt);

internal static class ShopRules
{
    public static async Task EnsureWarehouseAsync(IApplicationDbContext db, WarehouseInput w, CancellationToken ct)
    {
        var codes = new[] { w.ProvinceCode, w.DistrictCode, w.WardCode };
        var units = await db.AdminDivisions.AsNoTracking().Where(d => codes.Contains(d.Code)).ToListAsync(ct);
        var district = units.FirstOrDefault(u => u.Code == w.DistrictCode);
        var ward = units.FirstOrDefault(u => u.Code == w.WardCode);
        if (units.All(u => u.Code != w.ProvinceCode) || district?.ParentCode != w.ProvinceCode || ward?.ParentCode != w.DistrictCode)
            throw new ValidationException([new ValidationFailure("warehouse", "Địa chỉ lấy hàng không hợp lệ (Tỉnh/Quận/Phường không khớp).")]);
    }

    public static async Task ApplyKycAsync(IApplicationDbContext db, IDataEncryptor encryptor, ShopKyc kyc, Guid userId, ShopType type,
        PersonalKycInput? personal, BusinessKycInput? business, CancellationToken ct)
    {
        if (type == ShopType.Personal)
        {
            var p = personal ?? throw new ValidationException([new ValidationFailure("personal", "Shop cá nhân cần CCCD hai mặt.")]);
            var assets = await MediaUrls.LoadOwnedAsync(db, userId, "kyc", [p.FrontAssetId, p.BackAssetId], ct);
            kyc.SetPersonal(p.LegalName, encryptor.Encrypt(p.IdCardNumber.Trim()), assets[p.FrontAssetId].ObjectKey, assets[p.BackAssetId].ObjectKey);
        }
        else
        {
            var b = business ?? throw new ValidationException([new ValidationFailure("business", "Shop doanh nghiệp cần giấy phép và mã số thuế.")]);
            var assets = await MediaUrls.LoadOwnedAsync(db, userId, "kyc", [b.LicenseAssetId], ct);
            kyc.SetBusiness(b.LegalName, b.TaxCode, assets[b.LicenseAssetId].ObjectKey);
        }
    }
}

// ---------- Register a shop ----------

public record RegisterShopCommand(
    string Name,
    ShopType Type,
    string? Description,
    WarehouseInput Warehouse,
    PersonalKycInput? Personal,
    BusinessKycInput? Business,
    BankInput Bank) : IRequest<Guid>;

public sealed class RegisterShopValidator : AbstractValidator<RegisterShopCommand>
{
    public RegisterShopValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên shop.").Length(3, 50).WithMessage("Tên shop phải có từ 3 đến 50 ký tự.");
        RuleFor(x => x.Type).Must(t => t is ShopType.Personal or ShopType.Business).WithMessage("Loại shop không hợp lệ.");
        RuleFor(x => x.Description).MaximumLength(2000).WithMessage("Mô tả tối đa 2.000 ký tự.");
        RuleFor(x => x.Warehouse.ContactName).NotEmpty().WithMessage("Vui lòng nhập tên người liên hệ lấy hàng.");
        RuleFor(x => x.Warehouse.Phone).Must(p => Identifiers.NormalisePhone(p) is not null).WithMessage("Số điện thoại lấy hàng không hợp lệ.");
        RuleFor(x => x.Warehouse.Street).NotEmpty().WithMessage("Vui lòng nhập địa chỉ lấy hàng cụ thể.");
        When(x => x.Type == ShopType.Personal, () =>
        {
            RuleFor(x => x.Personal).NotNull().WithMessage("Shop cá nhân cần CCCD hai mặt.");
            RuleFor(x => x.Personal!.IdCardNumber).Matches(@"^\d{12}$").WithMessage("Số CCCD gồm 12 chữ số.").When(x => x.Personal is not null);
            RuleFor(x => x.Personal!.LegalName).NotEmpty().WithMessage("Vui lòng nhập họ tên trên CCCD.").When(x => x.Personal is not null);
        });
        When(x => x.Type == ShopType.Business, () =>
        {
            RuleFor(x => x.Business).NotNull().WithMessage("Shop doanh nghiệp cần giấy phép và mã số thuế.");
            RuleFor(x => x.Business!.TaxCode).Matches(@"^\d{10}(-\d{3})?$").WithMessage("Mã số thuế gồm 10 số (hoặc 10 số + -3 số).")
                .When(x => x.Business is not null);
            RuleFor(x => x.Business!.LegalName).NotEmpty().WithMessage("Vui lòng nhập tên doanh nghiệp.").When(x => x.Business is not null);
        });
        RuleFor(x => x.Bank.BankCode).NotEmpty().WithMessage("Vui lòng chọn ngân hàng.");
        RuleFor(x => x.Bank.AccountNo).Matches(@"^\d{6,20}$").WithMessage("Số tài khoản gồm 6–20 chữ số.");
        RuleFor(x => x.Bank.AccountName).NotEmpty().WithMessage("Vui lòng nhập tên chủ tài khoản.");
    }
}

public sealed class RegisterShopHandler(IApplicationDbContext db, SellerAccess access, IDataEncryptor encryptor, IOutbox outbox)
    : IRequestHandler<RegisterShopCommand, Guid>
{
    public async Task<Guid> Handle(RegisterShopCommand request, CancellationToken ct)
    {
        var userId = access.UserId;
        if (await db.Shops.AnyAsync(s => s.OwnerId == userId && s.Status == ShopStatus.PendingReview, ct))
            throw new ConflictException("Bạn đang có một hồ sơ shop chờ duyệt.", "SHOP_PENDING_EXISTS");
        await ShopRules.EnsureWarehouseAsync(db, request.Warehouse, ct);

        var shop = new Shop(userId, request.Name, Slug.From(request.Name), request.Type);
        shop.UpdateProfile(request.Description ?? string.Empty, null, null);
        db.Shops.Add(shop);
        db.ShopStaff.Add(new ShopStaff(shop.Id, userId, ShopStaffRole.Owner, ShopPermissions.All));

        var warehouse = new ShopWarehouse(shop.Id);
        var w = request.Warehouse;
        warehouse.Update("Kho chính", w.ContactName, Identifiers.NormalisePhone(w.Phone)!, w.ProvinceCode, w.DistrictCode, w.WardCode,
            w.Street, isPickupDefault: true, isReturnDefault: true);
        db.ShopWarehouses.Add(warehouse);

        var kyc = new ShopKyc(shop.Id);
        await ShopRules.ApplyKycAsync(db, encryptor, kyc, userId, request.Type, request.Personal, request.Business, ct);
        db.ShopKycs.Add(kyc);

        var account = request.Bank.AccountNo.Trim();
        db.ShopBankAccounts.Add(new ShopBankAccount(shop.Id, request.Bank.BankCode, encryptor.Encrypt(account), account[^4..],
            request.Bank.AccountName, isDefault: true));

        outbox.Enqueue(OutboxTypes.ShopEvent, new ShopEventPayload(shop.Id, "SUBMITTED", null));
        // Unique index on the shop name is the real guard against two parallel registrations
        await db.SaveChangesAsync(ct);
        return shop.Id;
    }
}

public record ResubmitShopCommand(Guid ShopId, PersonalKycInput? Personal, BusinessKycInput? Business) : IRequest<Unit>;

public sealed class ResubmitShopHandler(IApplicationDbContext db, SellerAccess access, IDataEncryptor encryptor, IOutbox outbox)
    : IRequestHandler<ResubmitShopCommand, Unit>
{
    public async Task<Unit> Handle(ResubmitShopCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.SettingsManage, ct);
        var shop = await db.Shops.FirstAsync(s => s.Id == request.ShopId, ct);
        var kyc = await db.ShopKycs.FirstAsync(k => k.ShopId == shop.Id, ct);
        await ShopRules.ApplyKycAsync(db, encryptor, kyc, access.UserId, shop.Type, request.Personal, request.Business, ct);
        shop.Resubmit();
        outbox.Enqueue(OutboxTypes.ShopEvent, new ShopEventPayload(shop.Id, "SUBMITTED", null));
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public record ListMyShopsQuery : IRequest<IReadOnlyList<MyShopDto>>;

public sealed class ListMyShopsHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<ListMyShopsQuery, IReadOnlyList<MyShopDto>>
{
    public async Task<IReadOnlyList<MyShopDto>> Handle(ListMyShopsQuery request, CancellationToken ct)
    {
        var userId = access.UserId;
        var rows = await (from st in db.ShopStaff
                          join s in db.Shops on st.ShopId equals s.Id
                          where st.UserId == userId
                          orderby s.CreatedAt, s.Id
                          select new { s, st }).AsNoTracking().ToListAsync(ct);
        return rows.Select(r => new MyShopDto(r.s.Id, r.s.Name, r.s.Slug, r.s.Type, r.s.Status, r.st.Role,
            r.st.Role == ShopStaffRole.Owner ? ShopPermissions.All : r.st.Permissions, r.s.LogoUrl, r.s.RejectReason,
            r.s.LockReason, r.s.CreatedAt)).ToList();
    }
}

public record UpdateShopProfileCommand(Guid ShopId, string Description, Guid? LogoAssetId, Guid? CoverAssetId) : IRequest<Unit>;

public sealed class UpdateShopProfileHandler(IApplicationDbContext db, SellerAccess access, IObjectStorage storage)
    : IRequestHandler<UpdateShopProfileCommand, Unit>
{
    public async Task<Unit> Handle(UpdateShopProfileCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.SettingsManage, ct);
        var shop = await db.Shops.FirstAsync(s => s.Id == request.ShopId, ct);
        var ids = new[] { request.LogoAssetId, request.CoverAssetId }.Where(i => i is not null).Select(i => i!.Value).ToList();
        var assets = ids.Count == 0 ? [] : await MediaUrls.LoadOwnedAsync(db, access.UserId, "shop", ids, ct);
        string? Url(Guid? id, int size, string? current) =>
            id is { } v ? storage.PublicUrl(assets[v].Bucket, ImageSizes.Key(assets[v].ObjectKey, size)) : current;
        shop.UpdateProfile(request.Description, Url(request.LogoAssetId, ImageSizes.Small, shop.LogoUrl),
            Url(request.CoverAssetId, ImageSizes.Large, shop.CoverUrl));
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public record SetVacationCommand(Guid ShopId, DateTimeOffset? Until) : IRequest<Unit>;

public sealed class SetVacationHandler(IApplicationDbContext db, SellerAccess access, IClock clock) : IRequestHandler<SetVacationCommand, Unit>
{
    public async Task<Unit> Handle(SetVacationCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.SettingsManage, ct);
        var shop = await db.Shops.FirstAsync(s => s.Id == request.ShopId, ct);
        if (request.Until is { } until) shop.StartVacation(until, clock.UtcNow);
        else shop.EndVacation();
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
