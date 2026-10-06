using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Identity;
using ShopHub.Application.Security;
using ShopHub.Domain.Iam;

namespace ShopHub.Application.Features.Admin;

// ---------- Permissions & roles ----------

public record PermissionDto(string Code, string Module, string Name);

public record ListPermissionsQuery : IRequest<IReadOnlyList<PermissionDto>>;

public sealed class ListPermissionsHandler(IApplicationDbContext db) : IRequestHandler<ListPermissionsQuery, IReadOnlyList<PermissionDto>>
{
    public async Task<IReadOnlyList<PermissionDto>> Handle(ListPermissionsQuery request, CancellationToken ct) =>
        await db.Permissions.AsNoTracking().OrderBy(p => p.Module).ThenBy(p => p.Code)
            .Select(p => new PermissionDto(p.Code, p.Module, p.Name)).ToListAsync(ct);
}

public record RoleDto(Guid Id, string Code, string Name, string Description, bool IsSystem, IReadOnlyList<string> Permissions, int UserCount);

public record ListRolesQuery : IRequest<IReadOnlyList<RoleDto>>;

public sealed class ListRolesHandler(IApplicationDbContext db) : IRequestHandler<ListRolesQuery, IReadOnlyList<RoleDto>>
{
    public async Task<IReadOnlyList<RoleDto>> Handle(ListRolesQuery request, CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().Include(r => r.Permissions).OrderBy(r => r.Code).ToListAsync(ct);
        var counts = await db.UserRoles.GroupBy(ur => ur.RoleId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return roles.Select(r => new RoleDto(r.Id, r.Code, r.Name, r.Description, r.IsSystem,
            r.Permissions.Select(p => p.PermissionCode).Order().ToList(),
            counts.FirstOrDefault(c => c.Key == r.Id)?.Count ?? 0)).ToList();
    }
}

public record SaveRoleCommand(Guid? Id, string Code, string Name, string Description, IReadOnlyList<string> Permissions) : IRequest<Guid>;

public sealed class SaveRoleValidator : AbstractValidator<SaveRoleCommand>
{
    public SaveRoleValidator()
    {
        RuleFor(x => x.Code).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Vui lòng nhập mã vai trò.")
            .Matches("^[A-Z][A-Z0-9_]{2,49}$").WithMessage("Mã vai trò gồm 3–50 ký tự IN HOA, số hoặc gạch dưới.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên vai trò.").MaximumLength(100).WithMessage("Tên tối đa 100 ký tự.");
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("Mô tả tối đa 500 ký tự.");
        RuleFor(x => x.Permissions).NotNull().WithMessage("Thiếu danh sách quyền.");
    }
}

public sealed class SaveRoleHandler(IApplicationDbContext db, ISessionValidator sessions) : IRequestHandler<SaveRoleCommand, Guid>
{
    public async Task<Guid> Handle(SaveRoleCommand request, CancellationToken ct)
    {
        var known = await db.Permissions.Select(p => p.Code).ToListAsync(ct);
        var unknown = request.Permissions.Where(p => p != Permissions.All && !known.Contains(p)).ToList();
        if (unknown.Count > 0)
            throw new ValidationException([new FluentValidation.Results.ValidationFailure("permissions", $"Quyền không tồn tại: {string.Join(", ", unknown)}.")]);

        Role role;
        if (request.Id is { } id)
        {
            role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct)
                ?? throw new NotFoundException("Không tìm thấy vai trò.");
            if (role.Code == RoleCatalog.SuperAdmin && !request.Permissions.Contains(Permissions.All))
                throw new ConflictException("Không thể bỏ toàn quyền của vai trò Quản trị cao nhất.", "SUPER_ADMIN_PROTECTED");
            role.Rename(request.Name, request.Description);
        }
        else
        {
            if (await db.Roles.AnyAsync(r => r.Code == request.Code, ct))
                throw new ConflictException("Mã vai trò đã tồn tại.", "ROLE_CODE_TAKEN");
            role = new Role(request.Code, request.Name.Trim(), request.Description.Trim(), isSystem: false);
            db.Roles.Add(role);
        }

        role.SetPermissions(request.Permissions);
        await db.SaveChangesAsync(ct);

        // Holders of the role get new permissions on their next token refresh; cached session state is dropped now
        var holders = await db.UserRoles.Where(ur => ur.RoleId == role.Id).Select(ur => ur.UserId).ToListAsync(ct);
        foreach (var userId in holders) sessions.InvalidateUser(userId);
        return role.Id;
    }
}

public record DeleteRoleCommand(Guid Id) : IRequest<Unit>;

public sealed class DeleteRoleHandler(IApplicationDbContext db) : IRequestHandler<DeleteRoleCommand, Unit>
{
    public async Task<Unit> Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.Id, ct) ?? throw new NotFoundException("Không tìm thấy vai trò.");
        if (role.IsSystem) throw new ConflictException("Không thể xoá vai trò hệ thống.", "SYSTEM_ROLE");
        if (await db.UserRoles.AnyAsync(ur => ur.RoleId == role.Id, ct))
            throw new ConflictException("Vai trò đang được gán cho người dùng, hãy gỡ trước khi xoá.", "ROLE_IN_USE");
        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ---------- Users ----------

public record AdminUserDto(
    Guid Id,
    string FullName,
    string? Phone,
    string? Email,
    string? Username,
    UserStatus Status,
    string? LockReason,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public record ListUsersQuery(int Page = 1, int PageSize = PagingLimits.DefaultPageSize, string? Q = null, UserStatus? Status = null, bool? AdminsOnly = null)
    : IRequest<PagedResult<AdminUserDto>>, IPagedRequest;

public sealed class ListUsersValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersValidator() => this.ApplyPagingRules();
}

public sealed class ListUsersHandler(IApplicationDbContext db) : IRequestHandler<ListUsersQuery, PagedResult<AdminUserDto>>
{
    public async Task<PagedResult<AdminUserDto>> Handle(ListUsersQuery request, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking();
        if (request.Status is { } status) users = users.Where(u => u.Status == status);
        if (request.AdminsOnly == true) users = users.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id));
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = request.Q.Trim().ToLower();
            var phone = Identifiers.NormalisePhone(request.Q);
            users = users.Where(u => u.FullName.ToLower().Contains(q) || (u.Email != null && u.Email.Contains(q))
                                     || (u.Username != null && u.Username.Contains(q)) || (phone != null && u.Phone == phone));
        }

        var page = await users
            .OrderByDescending(u => u.CreatedAt).ThenByDescending(u => u.Id)
            .ToPagedResultAsync(u => new { u.Id, u.FullName, u.Phone, u.Email, u.Username, u.Status, u.LockReason, u.CreatedAt, u.LastLoginAt },
                request, ct);

        var ids = page.Items.Select(i => i.Id).ToList();
        var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                           where ids.Contains(ur.UserId) select new { ur.UserId, r.Code }).ToListAsync(ct);

        return new PagedResult<AdminUserDto>(
            page.Items.Select(u => new AdminUserDto(u.Id, u.FullName, u.Phone, u.Email, u.Username, u.Status, u.LockReason,
                roles.Where(r => r.UserId == u.Id).Select(r => r.Code).Order().ToList(), u.CreatedAt, u.LastLoginAt)).ToList(),
            page.TotalCount, page.Page, page.PageSize);
    }
}

public record SetUserRolesCommand(Guid UserId, IReadOnlyList<Guid> RoleIds) : IRequest<Unit>;

public sealed class SetUserRolesHandler(IApplicationDbContext db, SessionService sessions) : IRequestHandler<SetUserRolesCommand, Unit>
{
    public async Task<Unit> Handle(SetUserRolesCommand request, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == request.UserId, ct)) throw new NotFoundException("Không tìm thấy người dùng.");
        var wanted = request.RoleIds.Distinct().ToList();
        var validCount = await db.Roles.CountAsync(r => wanted.Contains(r.Id), ct);
        if (validCount != wanted.Count) throw new NotFoundException("Có vai trò không tồn tại.");

        var current = await db.UserRoles.Where(ur => ur.UserId == request.UserId).ToListAsync(ct);
        db.UserRoles.RemoveRange(current.Where(c => !wanted.Contains(c.RoleId)));
        db.UserRoles.AddRange(wanted.Where(w => current.All(c => c.RoleId != w)).Select(w => new UserRole(request.UserId, w)));

        // There must always be at least one active super admin left
        var superAdminId = await db.Roles.Where(r => r.Code == RoleCatalog.SuperAdmin).Select(r => r.Id).SingleAsync(ct);
        var otherSuperAdmins = await db.UserRoles.CountAsync(ur => ur.RoleId == superAdminId && ur.UserId != request.UserId
            && db.Users.Any(u => u.Id == ur.UserId && u.Status == UserStatus.Active), ct);
        if (otherSuperAdmins == 0 && !wanted.Contains(superAdminId))
            throw new ConflictException("Phải còn ít nhất một Quản trị cao nhất đang hoạt động.", "LAST_SUPER_ADMIN");

        await db.SaveChangesAsync(ct);
        // Permissions live in the access token: force this user to fetch a new one
        await sessions.RevokeAllAsync(request.UserId, RevokeReasons.RemoteLogout, keepFamilyId: null, ct);
        return Unit.Value;
    }
}

public record LockUserCommand(Guid UserId, string Reason) : IRequest<Unit>;

public sealed class LockUserValidator : AbstractValidator<LockUserCommand>
{
    public LockUserValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Vui lòng nhập lý do khoá.").MaximumLength(500).WithMessage("Lý do tối đa 500 ký tự.");
}

public sealed class LockUserHandler(IApplicationDbContext db, ICurrentUser currentUser, SessionService sessions)
    : IRequestHandler<LockUserCommand, Unit>
{
    public async Task<Unit> Handle(LockUserCommand request, CancellationToken ct)
    {
        if (request.UserId == currentUser.UserId) throw new ConflictException("Bạn không thể tự khoá tài khoản của mình.", "SELF_LOCK");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, ct) ?? throw new NotFoundException("Không tìm thấy người dùng.");
        user.Lock(request.Reason);
        await db.SaveChangesAsync(ct);
        // Cut every open session now (refresh tokens revoked + session cache dropped on all instances)
        await sessions.RevokeAllAsync(user.Id, RevokeReasons.AccountLocked, keepFamilyId: null, ct);
        return Unit.Value;
    }
}

public record UnlockUserCommand(Guid UserId) : IRequest<Unit>;

public sealed class UnlockUserHandler(IApplicationDbContext db, ISessionValidator sessions) : IRequestHandler<UnlockUserCommand, Unit>
{
    public async Task<Unit> Handle(UnlockUserCommand request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, ct) ?? throw new NotFoundException("Không tìm thấy người dùng.");
        user.Unlock();
        await db.SaveChangesAsync(ct);
        sessions.InvalidateUser(user.Id);
        return Unit.Value;
    }
}
