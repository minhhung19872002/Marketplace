using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Identity;
using ShopHub.Domain.Iam;

namespace ShopHub.Application.Features.Account;

internal static class CurrentUserExtensions
{
    public static Guid RequireUserId(this ICurrentUser user) =>
        user.UserId ?? throw new AuthenticationFailedException("Bạn cần đăng nhập để tiếp tục.");
}

// ---------- Me / profile ----------

public record MeDto(
    Guid Id,
    string FullName,
    string? Phone,
    string? Email,
    string? Username,
    string? AvatarUrl,
    Gender? Gender,
    DateOnly? DateOfBirth,
    bool MustChangePassword,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions,
    DateTimeOffset CreatedAt);

public record GetMeQuery : IRequest<MeDto>;

public sealed class GetMeHandler(IApplicationDbContext db, ICurrentUser currentUser, SessionService sessions)
    : IRequestHandler<GetMeQuery, MeDto>
{
    public async Task<MeDto> Handle(GetMeQuery request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException();
        var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                           where ur.UserId == userId select r.Code).ToListAsync(ct);
        var permissions = await sessions.GetPermissionsAsync(userId, ct);
        return new MeDto(user.Id, user.FullName, user.Phone, user.Email, user.Username, user.AvatarUrl, user.Gender,
            user.DateOfBirth, user.MustChangePassword, roles, permissions, user.CreatedAt);
    }
}

public record UpdateProfileCommand(string FullName, Gender? Gender, DateOnly? DateOfBirth) : IRequest<Unit>;

public sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileValidator(IClock clock)
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Vui lòng nhập họ tên.")
            .MaximumLength(100).WithMessage("Họ tên tối đa 100 ký tự.");
        RuleFor(x => x.DateOfBirth)
            .Must(d => d is null || (d.Value.Year >= 1900 && d.Value <= DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)))
            .WithMessage("Ngày sinh không hợp lệ.");
    }
}

public sealed class UpdateProfileHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<UpdateProfileCommand, Unit>
{
    public async Task<Unit> Handle(UpdateProfileCommand request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException();
        user.UpdateProfile(request.FullName, request.Gender, request.DateOfBirth);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ---------- Password ----------

public record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<Unit>;

public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Vui lòng nhập mật khẩu hiện tại.");
        RuleFor(x => x.NewPassword).StrongPassword()
            .NotEqual(x => x.CurrentPassword).WithMessage("Mật khẩu mới phải khác mật khẩu hiện tại.");
    }
}

public sealed class ChangePasswordHandler(
    IApplicationDbContext db, ICurrentUser currentUser, IPasswordHasher hasher, SessionService sessions)
    : IRequestHandler<ChangePasswordCommand, Unit>
{
    public async Task<Unit> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException();
        if (!hasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new ValidationException([new ValidationFailure("currentPassword", "Mật khẩu hiện tại không đúng.")]);

        user.ChangePassword(hasher.Hash(request.NewPassword));
        await db.SaveChangesAsync(ct);
        // Every other device must sign in again; this one stays
        await sessions.RevokeAllAsync(userId, RevokeReasons.PasswordChanged, currentUser.SessionId, ct);
        return Unit.Value;
    }
}

// ---------- Devices (login sessions) ----------

public record SessionDto(Guid Id, string? Device, string? Ip, DateTimeOffset SignedInAt, DateTimeOffset LastActiveAt, bool IsCurrent);

public record ListSessionsQuery : IRequest<IReadOnlyList<SessionDto>>;

public sealed class ListSessionsHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<ListSessionsQuery, IReadOnlyList<SessionDto>>
{
    public async Task<IReadOnlyList<SessionDto>> Handle(ListSessionsQuery request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        var families = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId)
            .GroupBy(t => t.FamilyId)
            .Where(g => g.Any(t => t.RevokedAt == null && t.ExpiresAt > now))
            .Select(g => new
            {
                Id = g.Key,
                SignedInAt = g.Min(t => t.CreatedAt),
                LastActiveAt = g.Max(t => t.CreatedAt),
            })
            .ToListAsync(ct);

        var latest = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .Select(t => new { t.FamilyId, t.Device, t.Ip })
            .ToListAsync(ct);

        return families
            .Select(f =>
            {
                var info = latest.FirstOrDefault(l => l.FamilyId == f.Id);
                return new SessionDto(f.Id, info?.Device, info?.Ip, f.SignedInAt, f.LastActiveAt, f.Id == currentUser.SessionId);
            })
            .OrderByDescending(s => s.LastActiveAt).ThenBy(s => s.Id)
            .ToList();
    }
}

public record RevokeSessionCommand(Guid SessionId) : IRequest<Unit>;

public sealed class RevokeSessionHandler(IApplicationDbContext db, ICurrentUser currentUser, SessionService sessions)
    : IRequestHandler<RevokeSessionCommand, Unit>
{
    public async Task<Unit> Handle(RevokeSessionCommand request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        // Ownership is part of the query: someone else's session id is simply "not found"
        var exists = await db.RefreshTokens.AnyAsync(t => t.FamilyId == request.SessionId && t.UserId == userId && t.RevokedAt == null, ct);
        if (!exists) throw new NotFoundException("Không tìm thấy phiên đăng nhập.");
        await sessions.RevokeFamilyAsync(userId, request.SessionId, RevokeReasons.RemoteLogout, ct);
        return Unit.Value;
    }
}

// ---------- Change phone / email (OTP to the NEW contact) ----------

public record RequestContactChangeCommand(string NewValue) : IRequest<OtpIssued>;

public sealed class RequestContactChangeValidator : AbstractValidator<RequestContactChangeCommand>
{
    public RequestContactChangeValidator() =>
        RuleFor(x => x.NewValue).Must(v => Identifiers.NormaliseOtpTarget(v) is not null)
            .WithMessage("Số điện thoại hoặc email không hợp lệ.");
}

public sealed class RequestContactChangeHandler(IApplicationDbContext db, ICurrentUser currentUser, OtpService otp)
    : IRequestHandler<RequestContactChangeCommand, OtpIssued>
{
    public async Task<OtpIssued> Handle(RequestContactChangeCommand request, CancellationToken ct)
    {
        currentUser.RequireUserId();
        var target = Identifiers.NormaliseOtpTarget(request.NewValue)!;
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Phone == target || u.Email == target, ct))
            throw new ConflictException(Identifiers.IsPhone(target)
                ? "Số điện thoại đã được tài khoản khác sử dụng."
                : "Email đã được tài khoản khác sử dụng.", "CONTACT_TAKEN");
        var purpose = Identifiers.IsPhone(target) ? OtpPurpose.ChangePhone : OtpPurpose.ChangeEmail;
        return await otp.IssueAsync(target, purpose, deliver: true, ct);
    }
}

public record ConfirmContactChangeCommand(string NewValue, string Code) : IRequest<Unit>;

public sealed class ConfirmContactChangeValidator : AbstractValidator<ConfirmContactChangeCommand>
{
    public ConfirmContactChangeValidator()
    {
        RuleFor(x => x.NewValue).Must(v => Identifiers.NormaliseOtpTarget(v) is not null)
            .WithMessage("Số điện thoại hoặc email không hợp lệ.");
        RuleFor(x => x.Code).Matches(@"^\d{6}$").WithMessage("Mã xác thực gồm 6 chữ số.");
    }
}

public sealed class ConfirmContactChangeHandler(IApplicationDbContext db, ICurrentUser currentUser, OtpService otp, IClock clock)
    : IRequestHandler<ConfirmContactChangeCommand, Unit>
{
    public async Task<Unit> Handle(ConfirmContactChangeCommand request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var target = Identifiers.NormaliseOtpTarget(request.NewValue)!;
        var isPhone = Identifiers.IsPhone(target);
        var code = await otp.VerifyAsync(target, isPhone ? OtpPurpose.ChangePhone : OtpPurpose.ChangeEmail, request.Code, ct);
        await otp.ConsumeAsync(code, ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException();
        if (isPhone) user.ChangePhone(target, clock.UtcNow);
        else user.ChangeEmail(target, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ---------- Privacy: export / delete (Decree 13/2023) ----------

public record MyDataExport(MeDto Profile, IReadOnlyList<AddressDto> Addresses, IReadOnlyList<SessionDto> Sessions, DateTimeOffset ExportedAt);

public record ExportMyDataQuery : IRequest<MyDataExport>;

public sealed class ExportMyDataHandler(ISender sender, IClock clock) : IRequestHandler<ExportMyDataQuery, MyDataExport>
{
    public async Task<MyDataExport> Handle(ExportMyDataQuery request, CancellationToken ct) => new(
        await sender.Send(new GetMeQuery(), ct),
        await sender.Send(new ListAddressesQuery(), ct),
        await sender.Send(new ListSessionsQuery(), ct),
        clock.UtcNow);
}

public record DeleteMyAccountCommand(string Password) : IRequest<Unit>;

public sealed class DeleteMyAccountValidator : AbstractValidator<DeleteMyAccountCommand>
{
    public DeleteMyAccountValidator() =>
        RuleFor(x => x.Password).NotEmpty().WithMessage("Vui lòng nhập mật khẩu để xác nhận.");
}

public sealed class DeleteMyAccountHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IPasswordHasher hasher,
    IEnumerable<IAccountDeletionGuard> guards,
    SessionService sessions,
    IClock clock) : IRequestHandler<DeleteMyAccountCommand, Unit>
{
    public async Task<Unit> Handle(DeleteMyAccountCommand request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException();
        if (!hasher.Verify(request.Password, user.PasswordHash))
            throw new ValidationException([new ValidationFailure("password", "Mật khẩu không đúng.")]);

        foreach (var guard in guards)
            if (await guard.GetBlockingReasonAsync(userId, ct) is { } reason)
                throw new ConflictException(reason, "ACCOUNT_DELETE_BLOCKED");

        var now = clock.UtcNow;
        user.Anonymise(now);
        await db.Addresses.Where(a => a.UserId == userId && a.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.DeletedAt, now), ct);
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllAsync(userId, RevokeReasons.AccountDeleted, keepFamilyId: null, ct);
        return Unit.Value;
    }
}
