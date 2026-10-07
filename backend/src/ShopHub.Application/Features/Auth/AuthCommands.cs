using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Identity;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Iam;

namespace ShopHub.Application.Features.Auth;

// ---------- OTP ----------

public record SendOtpCommand(string Target, OtpPurpose Purpose) : IRequest<OtpIssued>;

public sealed class SendOtpValidator : AbstractValidator<SendOtpCommand>
{
    public SendOtpValidator()
    {
        RuleFor(x => x.Target).Must(t => Identifiers.NormaliseOtpTarget(t) is not null)
            .WithMessage("Số điện thoại hoặc email không hợp lệ.");
        RuleFor(x => x.Purpose).Must(p => p is OtpPurpose.Register or OtpPurpose.Login or OtpPurpose.ResetPassword)
            .WithMessage("Mục đích gửi mã không hợp lệ.");
    }
}

public sealed class SendOtpHandler(IApplicationDbContext db, OtpService otp) : IRequestHandler<SendOtpCommand, OtpIssued>
{
    public async Task<OtpIssued> Handle(SendOtpCommand request, CancellationToken ct)
    {
        var target = Identifiers.NormaliseOtpTarget(request.Target)!;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Phone == target || u.Email == target, ct);

        if (request.Purpose == OtpPurpose.Register && user is not null)
            throw new ConflictException(Identifiers.IsPhone(target)
                ? "Số điện thoại đã được đăng ký."
                : "Email đã được đăng ký.", "ALREADY_REGISTERED");

        // Login / reset: only deliver to an existing active account, but answer the same either way
        var deliver = request.Purpose == OtpPurpose.Register || user is { Status: UserStatus.Active };
        if (request.Purpose == OtpPurpose.Login && !Identifiers.IsPhone(target))
            throw new ValidationException([new FluentValidation.Results.ValidationFailure("target", "Đăng nhập bằng mã chỉ hỗ trợ số điện thoại.")]);

        return await otp.IssueAsync(target, request.Purpose, deliver, ct);
    }
}

public record VerifyOtpCommand(string Target, OtpPurpose Purpose, string Code) : IRequest<OtpTicketDto>;

public record OtpTicketDto(string Ticket);

public sealed class VerifyOtpValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpValidator()
    {
        RuleFor(x => x.Target).Must(t => Identifiers.NormaliseOtpTarget(t) is not null)
            .WithMessage("Số điện thoại hoặc email không hợp lệ.");
        RuleFor(x => x.Code).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Vui lòng nhập mã xác thực.")
            .Matches(@"^\d{6}$").WithMessage("Mã xác thực gồm 6 chữ số.");
        RuleFor(x => x.Purpose).Must(p => p is OtpPurpose.Register or OtpPurpose.ResetPassword)
            .WithMessage("Mục đích xác thực không hợp lệ.");
    }
}

public sealed class VerifyOtpHandler(OtpService otp) : IRequestHandler<VerifyOtpCommand, OtpTicketDto>
{
    public async Task<OtpTicketDto> Handle(VerifyOtpCommand request, CancellationToken ct) =>
        new(await otp.VerifyForTicketAsync(Identifiers.NormaliseOtpTarget(request.Target)!, request.Purpose, request.Code, ct));
}

// ---------- Register ----------

public record RegisterCommand(string Target, string Ticket, string Password, string FullName, bool AcceptTerms, string? Device)
    : IRequest<AuthResult>;

public sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Target).Must(t => Identifiers.NormaliseOtpTarget(t) is not null)
            .WithMessage("Số điện thoại hoặc email không hợp lệ.");
        RuleFor(x => x.Ticket).NotEmpty().WithMessage("Thiếu mã xác thực. Vui lòng xác thực số điện thoại/email trước.");
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Vui lòng nhập họ tên.")
            .MaximumLength(100).WithMessage("Họ tên tối đa 100 ký tự.");
        RuleFor(x => x.AcceptTerms).Equal(true)
            .WithMessage("Bạn cần đồng ý Điều khoản sử dụng và Chính sách xử lý dữ liệu cá nhân.");
    }
}

public sealed class RegisterHandler(
    IApplicationDbContext db,
    OtpService otp,
    SessionService sessions,
    IPasswordHasher hasher,
    IClock clock) : IRequestHandler<RegisterCommand, AuthResult>
{
    public async Task<AuthResult> Handle(RegisterCommand request, CancellationToken ct)
    {
        var target = Identifiers.NormaliseOtpTarget(request.Target)!;
        var isPhone = Identifiers.IsPhone(target);

        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Phone == target || u.Email == target, ct))
            throw new ConflictException(isPhone ? "Số điện thoại đã được đăng ký." : "Email đã được đăng ký.", "ALREADY_REGISTERED");

        await otp.RedeemTicketAsync(target, OtpPurpose.Register, request.Ticket, ct);

        var user = User.Register(isPhone ? target : null, isPhone ? null : target, hasher.Hash(request.Password),
            request.FullName, clock.UtcNow);
        user.RecordSuccessfulLogin(clock.UtcNow);
        db.Users.Add(user);
        // The unique index is the real guard against two parallel registrations of one number
        await db.SaveChangesAsync(ct);

        return await sessions.StartAsync(user, request.Device, ct);
    }
}

// ---------- Login ----------

public record LoginCommand(string Identifier, string Password, string? Device) : IRequest<AuthResult>;

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty().WithMessage("Vui lòng nhập số điện thoại, email hoặc tên đăng nhập.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Vui lòng nhập mật khẩu.");
    }
}

public sealed class LoginHandler(
    IApplicationDbContext db,
    SessionService sessions,
    IPasswordHasher hasher,
    ISystemParameters parameters,
    IClock clock) : IRequestHandler<LoginCommand, AuthResult>
{
    public async Task<AuthResult> Handle(LoginCommand request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var user = await FindAsync(request.Identifier, ct);
        if (user is null)
        {
            hasher.SimulateVerify(request.Password);
            throw new AuthenticationFailedException();
        }

        if (user.IsTemporarilyLocked(now))
        {
            var minutes = (int)Math.Ceiling((user.LockedUntil!.Value - now).TotalMinutes);
            throw new AuthenticationFailedException(
                $"Tài khoản tạm khoá do đăng nhập sai nhiều lần. Vui lòng thử lại sau {minutes} phút.", "TEMPORARILY_LOCKED");
        }

        if (!hasher.Verify(request.Password, user.PasswordHash))
        {
            await RegisterFailureAsync(user.Id, now, ct);
            throw new AuthenticationFailedException();
        }

        if (user.Status == UserStatus.Locked)
            throw new AuthenticationFailedException("Tài khoản đã bị khoá. Vui lòng liên hệ bộ phận hỗ trợ.", "ACCOUNT_LOCKED");

        await LoginStamp.RecordAsync(db, user, now, ct);
        return await sessions.StartAsync(user, request.Device, ct);
    }

    private async Task<User?> FindAsync(string identifier, CancellationToken ct)
    {
        if (Identifiers.Classify(identifier) is not var (kind, value)) return null;
        var users = db.Users.AsQueryable();
        users = kind switch
        {
            IdentifierKind.Phone => users.Where(u => u.Phone == value),
            IdentifierKind.Email => users.Where(u => u.Email == value),
            _ => users.Where(u => u.Username == value),
        };
        return await users.FirstOrDefaultAsync(ct);
    }

    // One atomic UPDATE: parallel wrong guesses cannot slip past the limit
    private async Task RegisterFailureAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var max = (int)await parameters.GetIntAsync(ParameterKeys.AuthMaxFailedLogin, ct);
        var lockUntil = now.AddMinutes(await parameters.GetIntAsync(ParameterKeys.AuthLockoutMinutes, ct));
        await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LockedUntil, u => u.FailedLoginCount + 1 >= max ? lockUntil : u.LockedUntil)
                .SetProperty(u => u.FailedLoginCount, u => u.FailedLoginCount + 1 >= max ? 0 : u.FailedLoginCount + 1), ct);
    }
}

public record LoginWithOtpCommand(string Phone, string Code, string? Device) : IRequest<AuthResult>;

public sealed class LoginWithOtpValidator : AbstractValidator<LoginWithOtpCommand>
{
    public LoginWithOtpValidator()
    {
        RuleFor(x => x.Phone).Must(p => Identifiers.NormalisePhone(p) is not null).WithMessage("Số điện thoại không hợp lệ.");
        RuleFor(x => x.Code).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Vui lòng nhập mã xác thực.")
            .Matches(@"^\d{6}$").WithMessage("Mã xác thực gồm 6 chữ số.");
    }
}

public sealed class LoginWithOtpHandler(IApplicationDbContext db, OtpService otp, SessionService sessions, IClock clock)
    : IRequestHandler<LoginWithOtpCommand, AuthResult>
{
    public async Task<AuthResult> Handle(LoginWithOtpCommand request, CancellationToken ct)
    {
        var phone = Identifiers.NormalisePhone(request.Phone)!;
        var code = await otp.VerifyAsync(phone, OtpPurpose.Login, request.Code, ct);
        await otp.ConsumeAsync(code, ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == phone, ct) ?? throw new AuthenticationFailedException();
        if (user.Status != UserStatus.Active)
            throw new AuthenticationFailedException("Tài khoản đã bị khoá. Vui lòng liên hệ bộ phận hỗ trợ.", "ACCOUNT_LOCKED");

        await LoginStamp.RecordAsync(db, user, clock.UtcNow, ct);
        return await sessions.StartAsync(user, request.Device, ct);
    }
}

/// <summary>
/// A successful sign-in clears the failure counter and stamps last_login_at with one set-based statement: several
/// devices signing in to one account at the same moment would otherwise trip the user row's version (409, L061).
/// </summary>
internal static class LoginStamp
{
    public static async Task RecordAsync(IApplicationDbContext db, User user, DateTimeOffset now, CancellationToken ct)
    {
        await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(u => u
            .SetProperty(x => x.FailedLoginCount, 0)
            .SetProperty(x => x.LockedUntil, (DateTimeOffset?)null)
            .SetProperty(x => x.LastLoginAt, now), ct);
    }
}

// ---------- Refresh / logout ----------

public record RefreshCommand(string RefreshToken, string? Device) : IRequest<AuthResult>;

public sealed class RefreshValidator : AbstractValidator<RefreshCommand>
{
    public RefreshValidator() =>
        RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("Thiếu refresh token.");
}

public sealed class RefreshHandler(SessionService sessions) : IRequestHandler<RefreshCommand, AuthResult>
{
    public Task<AuthResult> Handle(RefreshCommand request, CancellationToken ct) =>
        sessions.RotateAsync(request.RefreshToken, request.Device, ct);
}

public record LogoutCommand(string? RefreshToken) : IRequest<Unit>;

public sealed class LogoutHandler(IApplicationDbContext db, SessionService sessions, ISecretGenerator secrets, ICurrentUser currentUser)
    : IRequestHandler<LogoutCommand, Unit>
{
    public async Task<Unit> Handle(LogoutCommand request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(request.RefreshToken))
        {
            var hash = secrets.Hash(request.RefreshToken);
            var token = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (token is not null) await sessions.RevokeFamilyAsync(token.UserId, token.FamilyId, RevokeReasons.Logout, ct);
        }
        else if (currentUser is { UserId: { } userId, SessionId: { } sessionId })
        {
            await sessions.RevokeFamilyAsync(userId, sessionId, RevokeReasons.Logout, ct);
        }
        return Unit.Value;
    }
}

// ---------- Reset password ----------

public record ResetPasswordCommand(string Target, string Ticket, string NewPassword) : IRequest<Unit>;

public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Target).Must(t => Identifiers.NormaliseOtpTarget(t) is not null)
            .WithMessage("Số điện thoại hoặc email không hợp lệ.");
        RuleFor(x => x.Ticket).NotEmpty().WithMessage("Thiếu mã xác thực.");
        RuleFor(x => x.NewPassword).StrongPassword();
    }
}

public sealed class ResetPasswordHandler(IApplicationDbContext db, OtpService otp, SessionService sessions, IPasswordHasher hasher)
    : IRequestHandler<ResetPasswordCommand, Unit>
{
    public async Task<Unit> Handle(ResetPasswordCommand request, CancellationToken ct)
    {
        var target = Identifiers.NormaliseOtpTarget(request.Target)!;
        await otp.RedeemTicketAsync(target, OtpPurpose.ResetPassword, request.Ticket, ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == target || u.Email == target, ct)
            ?? throw new ConflictException("Phiên xác thực đã hết hạn. Vui lòng xác thực lại.", "TICKET_INVALID");
        user.ChangePassword(hasher.Hash(request.NewPassword));
        await db.SaveChangesAsync(ct);

        // A reset means the old password may be known to someone else: end every session
        await sessions.RevokeAllAsync(user.Id, RevokeReasons.PasswordChanged, keepFamilyId: null, ct);
        return Unit.Value;
    }
}
