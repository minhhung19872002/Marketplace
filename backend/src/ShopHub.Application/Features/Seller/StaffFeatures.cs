using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Account;
using ShopHub.Application.Identity;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Common;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Seller;

// Tài khoản phụ (III.9): the owner (or staff holding STAFF.MANAGE) invites existing ShopHub accounts as staff with a
// role and a set of grants; they join only by accepting the invitation (D6). Nobody can hand out a grant they do not hold
// themselves; the owner row is never touched.

public record ShopStaffDto(Guid Id, Guid UserId, string FullName, string? PhoneMasked, string? EmailMasked, ShopStaffRole Role,
    IReadOnlyList<string> Permissions, DateTimeOffset CreatedAt);

public record StaffInvitationDto(Guid Id, string FullName, string? PhoneMasked, string? EmailMasked, ShopStaffRole Role,
    IReadOnlyList<string> Permissions, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public record ShopStaffBoardDto(IReadOnlyList<ShopStaffDto> Staff, IReadOnlyList<StaffInvitationDto> Invitations, IReadOnlyList<string> AllPermissions,
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

public sealed class ListShopStaffHandler(IApplicationDbContext db, SellerAccess access, IClock clock) : IRequestHandler<ListShopStaffQuery, ShopStaffBoardDto>
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
        var now = clock.UtcNow;
        var invited = await (from i in db.ShopStaffInvitations
                             join u in db.Users on i.UserId equals u.Id
                             where i.ShopId == request.ShopId && i.Status == StaffInvitationStatus.Pending && i.ExpiresAt > now
                             orderby i.CreatedAt, i.Id
                             select new { i, u.FullName, u.Phone, u.Email }).AsNoTracking().ToListAsync(ct);
        var invitations = invited.Select(r => new StaffInvitationDto(r.i.Id, r.FullName, StaffRules.Mask(r.Phone), StaffRules.Mask(r.Email), r.i.Role,
            r.i.Permissions, r.i.CreatedAt, r.i.ExpiresAt)).ToList();
        return new ShopStaffBoardDto(staff, invitations, ShopPermissions.All, defaults, StaffRules.MaxStaff);
    }
}

public record AddShopStaffCommand(Guid ShopId, string Login, ShopStaffRole Role, IReadOnlyList<string>? Permissions) : IRequest<Guid>;

public sealed class AddShopStaffValidator : AbstractValidator<AddShopStaffCommand>
{
    public AddShopStaffValidator() =>
        RuleFor(x => x.Login).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Vui lòng nhập số điện thoại hoặc email của nhân viên.")
            .Must(l => Identifiers.NormaliseOtpTarget(l) is not null).WithMessage("Số điện thoại hoặc email không hợp lệ.");
}

/// <summary>"Mời nhân viên": sends an invitation (returns its id) — the account joins the shop only when it accepts.</summary>
public sealed class AddShopStaffHandler(IApplicationDbContext db, SellerAccess access, ISystemParameters parameters, Admin.MessageTemplates templates,
    IClock clock)
    : IRequestHandler<AddShopStaffCommand, Guid>
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
        var now = clock.UtcNow;
        // An expired invitation no longer blocks a new one
        foreach (var stale in await db.ShopStaffInvitations
                     .Where(i => i.ShopId == request.ShopId && i.UserId == user.Id && i.Status == StaffInvitationStatus.Pending && i.ExpiresAt <= now).ToListAsync(ct))
            stale.Revoke(now);
        if (await db.ShopStaffInvitations.AnyAsync(i => i.ShopId == request.ShopId && i.UserId == user.Id && i.Status == StaffInvitationStatus.Pending
                                                         && i.ExpiresAt > now, ct))
            throw new ConflictException("Tài khoản này đang có lời mời chờ trả lời.", "INVITATION_PENDING");

        var days = Math.Max(1, await parameters.GetIntAsync(ParameterKeys.ShopStaffInviteDays, ct));
        var shopName = await db.Shops.Where(s => s.Id == request.ShopId).Select(s => s.Name).FirstAsync(ct);
        var invitation = new ShopStaffInvitation(request.ShopId, user.Id, request.Role, grants, actor.UserId, now.AddDays(days), now);
        db.ShopStaffInvitations.Add(invitation);
        var (title, body) = await templates.NoticeAsync(Admin.TemplateCatalog.StaffInvitation, new Dictionary<string, string>
        {
            ["shop"] = shopName, ["role"] = ShopStaffLabels.Of(request.Role), ["deadline"] = VietnamTime.Format(invitation.ExpiresAt),
        }, ct);
        db.Notifications.Add(new Notification(user.Id, NotificationCategory.Activity, title, body, "/seller/loi-moi", "shop_staff_invitation", invitation.Id,
            now, $"staff-invite:{invitation.Id}"));
        // The partial unique index (one waiting invitation per person) settles two invitations sent at once
        await db.SaveChangesAsync(ct);
        return invitation.Id;
    }
}

public record RevokeStaffInvitationCommand(Guid ShopId, Guid InvitationId) : IRequest<Unit>;

public sealed class RevokeStaffInvitationHandler(IApplicationDbContext db, SellerAccess access, IClock clock) : IRequestHandler<RevokeStaffInvitationCommand, Unit>
{
    public async Task<Unit> Handle(RevokeStaffInvitationCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.StaffManage, ct);
        var invitation = await db.ShopStaffInvitations.FirstOrDefaultAsync(i => i.Id == request.InvitationId && i.ShopId == request.ShopId
                                                                                && i.Status == StaffInvitationStatus.Pending, ct)
                         ?? throw new NotFoundException("Không tìm thấy lời mời.");
        invitation.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ---------- the invitee's side ----------

public record MyStaffInvitationDto(Guid Id, Guid ShopId, string ShopName, string? ShopLogoUrl, ShopStaffRole Role, IReadOnlyList<string> Permissions,
    DateTimeOffset ExpiresAt);

public record MyStaffInvitationsQuery : IRequest<IReadOnlyList<MyStaffInvitationDto>>;

public sealed class MyStaffInvitationsHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<MyStaffInvitationsQuery, IReadOnlyList<MyStaffInvitationDto>>
{
    public async Task<IReadOnlyList<MyStaffInvitationDto>> Handle(MyStaffInvitationsQuery request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        return await (from i in db.ShopStaffInvitations.AsNoTracking()
                      join s in db.Shops.AsNoTracking() on i.ShopId equals s.Id
                      where i.UserId == userId && i.Status == StaffInvitationStatus.Pending && i.ExpiresAt > now
                      orderby i.CreatedAt descending, i.Id
                      select new MyStaffInvitationDto(i.Id, s.Id, s.Name, s.LogoUrl, i.Role, i.Permissions, i.ExpiresAt)).ToListAsync(ct);
    }
}

public record AnswerStaffInvitationCommand(Guid InvitationId, bool Accept) : IRequest<Guid?>;

/// <summary>
/// The invitee says yes (→ a staff row, returned) or no. Only the invitee sees the invitation (404 for anyone else);
/// expired → 409. Accepting re-checks "not staff yet" and the staff cap under the shop's lock.
/// </summary>
public sealed class AnswerStaffInvitationHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<AnswerStaffInvitationCommand, Guid?>
{
    public async Task<Guid?> Handle(AnswerStaffInvitationCommand request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        await using var tx = await db.BeginTransactionAsync(ct);
        var invitation = await db.ShopStaffInvitations.FirstOrDefaultAsync(i => i.Id == request.InvitationId && i.UserId == userId
                                                                                && i.Status == StaffInvitationStatus.Pending, ct)
                         ?? throw new NotFoundException("Không tìm thấy lời mời.");
        if (!invitation.IsOpen(now)) throw new ConflictException("Lời mời đã hết hạn. Hãy nhờ shop gửi lời mời mới.", "INVITATION_EXPIRED");
        if (!request.Accept)
        {
            invitation.Decline(now);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return null;
        }
        await db.LockAsync($"shop:staff:{invitation.ShopId}", ct);
        if (await db.ShopStaff.AnyAsync(s => s.ShopId == invitation.ShopId && s.UserId == userId, ct))
            throw new ConflictException("Bạn đã là nhân viên của shop này.", "STAFF_EXISTS");
        if (await db.ShopStaff.CountAsync(s => s.ShopId == invitation.ShopId, ct) >= StaffRules.MaxStaff)
            throw new ConflictException($"Shop đã đủ {StaffRules.MaxStaff} tài khoản, chưa nhận thêm nhân viên được.", "STAFF_LIMIT");
        var staff = new ShopStaff(invitation.ShopId, userId, invitation.Role, invitation.Permissions);
        db.ShopStaff.Add(staff);
        invitation.Accept(now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
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
