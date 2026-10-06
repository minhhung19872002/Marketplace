using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Identity;
using ShopHub.Application.Security;
using ShopHub.Domain.Common;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Seller;

// Tài khoản phụ (III.9): the owner (or staff holding STAFF.MANAGE) adds existing ShopHub accounts as staff with a role
// and a set of grants. Nobody can hand out a grant they do not hold themselves; the owner row is never touched.

public record ShopStaffDto(Guid Id, Guid UserId, string FullName, string? PhoneMasked, string? EmailMasked, ShopStaffRole Role,
    IReadOnlyList<string> Permissions, DateTimeOffset CreatedAt);

public record ShopStaffBoardDto(IReadOnlyList<ShopStaffDto> Staff, IReadOnlyList<string> AllPermissions,
    IReadOnlyDictionary<ShopStaffRole, IReadOnlyList<string>> RoleDefaults, int MaxStaff);

internal static class StaffRules
{
    public const int MaxStaff = 20;

    public static string? Mask(string? value) => value switch
    {
        null => null,
        _ when value.Contains('@') => value[..Math.Min(2, value.IndexOf('@'))] + "***" + value[value.IndexOf('@')..],
        _ when value.Length > 6 => value[..3] + new string('*', value.Length - 6) + value[^3..],
        _ => "***",
    };

    public static IReadOnlyList<string> Grants(ShopStaff actor, ShopStaffRole role, IReadOnlyList<string>? requested)
    {
        if (role == ShopStaffRole.Owner) throw new BusinessRuleException("Mỗi shop chỉ có một chủ shop.");
        var grants = (requested ?? ShopPermissions.DefaultsFor(role)).Distinct().ToList();
        var unknown = grants.Except(ShopPermissions.All).ToList();
        if (unknown.Count > 0) throw new BusinessRuleException($"Quyền không hợp lệ: {string.Join(", ", unknown)}.");
        var beyond = grants.Where(g => !actor.Has(g)).ToList();
        if (beyond.Count > 0) throw new ForbiddenException($"Bạn không thể cấp quyền mình không có: {string.Join(", ", beyond)}.");
        return grants;
    }
}

public record ListShopStaffQuery(Guid ShopId) : IRequest<ShopStaffBoardDto>;

public sealed class ListShopStaffHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<ListShopStaffQuery, ShopStaffBoardDto>
{
    public async Task<ShopStaffBoardDto> Handle(ListShopStaffQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.StaffManage, ct);
        var rows = await (from st in db.ShopStaff
                          join u in db.Users on st.UserId equals u.Id
                          where st.ShopId == request.ShopId
                          orderby st.Role, st.CreatedAt, st.Id
                          select new { st, u.FullName, u.Phone, u.Email }).AsNoTracking().ToListAsync(ct);
        var staff = rows.Select(r => new ShopStaffDto(r.st.Id, r.st.UserId, r.FullName, StaffRules.Mask(r.Phone), StaffRules.Mask(r.Email),
            r.st.Role, r.st.Role == ShopStaffRole.Owner ? ShopPermissions.All : r.st.Permissions, r.st.CreatedAt)).ToList();
        var defaults = Enum.GetValues<ShopStaffRole>().Where(r => r != ShopStaffRole.Owner)
            .ToDictionary(r => r, ShopPermissions.DefaultsFor);
        return new ShopStaffBoardDto(staff, ShopPermissions.All, defaults, StaffRules.MaxStaff);
    }
}

public record AddShopStaffCommand(Guid ShopId, string Login, ShopStaffRole Role, IReadOnlyList<string>? Permissions) : IRequest<Guid>;

public sealed class AddShopStaffValidator : AbstractValidator<AddShopStaffCommand>
{
    public AddShopStaffValidator() =>
        RuleFor(x => x.Login).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Vui lòng nhập số điện thoại hoặc email của nhân viên.")
            .Must(l => Identifiers.NormaliseOtpTarget(l) is not null).WithMessage("Số điện thoại hoặc email không hợp lệ.");
}

public sealed class AddShopStaffHandler(IApplicationDbContext db, SellerAccess access, IClock clock) : IRequestHandler<AddShopStaffCommand, Guid>
{
    public async Task<Guid> Handle(AddShopStaffCommand request, CancellationToken ct)
    {
        var actor = await access.RequireAsync(request.ShopId, ShopPermissions.StaffManage, ct);
        var grants = StaffRules.Grants(actor, request.Role, request.Permissions);
        var login = Identifiers.NormaliseOtpTarget(request.Login)!;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Phone == login || u.Email == login, ct);
        if (user is null || user.Status != UserStatus.Active)
            throw new NotFoundException("Không tìm thấy tài khoản ShopHub đang hoạt động với thông tin này. Nhân viên cần đăng ký tài khoản trước.");
        if (await db.ShopStaff.AnyAsync(s => s.ShopId == request.ShopId && s.UserId == user.Id, ct))
            throw new ConflictException("Tài khoản này đã là nhân viên của shop.", "STAFF_EXISTS");
        if (await db.ShopStaff.CountAsync(s => s.ShopId == request.ShopId, ct) >= StaffRules.MaxStaff)
            throw new ConflictException($"Mỗi shop có tối đa {StaffRules.MaxStaff} tài khoản (kể cả chủ shop).", "STAFF_LIMIT");

        var shopName = await db.Shops.Where(s => s.Id == request.ShopId).Select(s => s.Name).FirstAsync(ct);
        var staff = new ShopStaff(request.ShopId, user.Id, request.Role, grants);
        db.ShopStaff.Add(staff);
        db.Notifications.Add(new Notification(user.Id, NotificationCategory.Activity, "Bạn được thêm vào một shop",
            $"Bạn đã được thêm làm {ShopStaffLabels.Of(request.Role)} của shop \"{shopName}\". Mở Kênh Người Bán để bắt đầu.",
            "/seller/", "shop_staff", staff.Id, clock.UtcNow, $"staff:{staff.Id}"));
        await db.SaveChangesAsync(ct);
        return staff.Id;
    }
}

public record UpdateShopStaffCommand(Guid ShopId, Guid StaffId, ShopStaffRole Role, IReadOnlyList<string>? Permissions) : IRequest<Unit>;

public sealed class UpdateShopStaffHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<UpdateShopStaffCommand, Unit>
{
    public async Task<Unit> Handle(UpdateShopStaffCommand request, CancellationToken ct)
    {
        var actor = await access.RequireAsync(request.ShopId, ShopPermissions.StaffManage, ct);
        var staff = await db.ShopStaff.FirstOrDefaultAsync(s => s.Id == request.StaffId && s.ShopId == request.ShopId, ct)
                    ?? throw new NotFoundException("Không tìm thấy nhân viên.");
        if (staff.UserId == actor.UserId) throw new BusinessRuleException("Bạn không thể tự đổi quyền của mình.");
        staff.Change(request.Role, StaffRules.Grants(actor, request.Role, request.Permissions));
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public record RemoveShopStaffCommand(Guid ShopId, Guid StaffId) : IRequest<Unit>;

public sealed class RemoveShopStaffHandler(IApplicationDbContext db, SellerAccess access, IClock clock) : IRequestHandler<RemoveShopStaffCommand, Unit>
{
    public async Task<Unit> Handle(RemoveShopStaffCommand request, CancellationToken ct)
    {
        var actor = await access.RequireAsync(request.ShopId, ShopPermissions.StaffManage, ct);
        var staff = await db.ShopStaff.FirstOrDefaultAsync(s => s.Id == request.StaffId && s.ShopId == request.ShopId, ct)
                    ?? throw new NotFoundException("Không tìm thấy nhân viên.");
        if (staff.Role == ShopStaffRole.Owner) throw new BusinessRuleException("Không thể gỡ chủ shop.");
        if (staff.UserId == actor.UserId) throw new BusinessRuleException("Bạn không thể tự gỡ mình khỏi shop.");
        // Access is checked against shop_staff on every request, so the removal takes effect on the next call
        staff.DeletedAt = clock.UtcNow;
        var assigned = await db.Conversations.Where(c => c.ShopId == request.ShopId && c.AssignedTo == staff.UserId).ToListAsync(ct);
        foreach (var c in assigned) c.AssignTo(null);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public static class ShopStaffLabels
{
    public static string Of(ShopStaffRole role) => role switch
    {
        ShopStaffRole.Owner => "chủ shop",
        ShopStaffRole.Manager => "quản lý",
        ShopStaffRole.CustomerService => "chăm sóc khách hàng",
        ShopStaffRole.Warehouse => "nhân viên kho",
        _ => "nhân viên",
    };
}
