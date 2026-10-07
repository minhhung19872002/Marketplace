using System.Security.Cryptography;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Identity;
using ShopHub.Domain.Iam;

namespace ShopHub.Application.Features.Auth;

public record AuthProvidersDto(string? GoogleClientId);

public record AuthProvidersQuery : IRequest<AuthProvidersDto>;

public sealed class AuthProvidersHandler(IGoogleTokenVerifier google) : IRequestHandler<AuthProvidersQuery, AuthProvidersDto>
{
    public Task<AuthProvidersDto> Handle(AuthProvidersQuery request, CancellationToken ct) => Task.FromResult(new AuthProvidersDto(google.ClientId));
}

public record GoogleLoginCommand(string IdToken, string? Device, bool AcceptTerms) : IRequest<AuthResult>;

public sealed class GoogleLoginValidator : AbstractValidator<GoogleLoginCommand>
{
    public GoogleLoginValidator() => RuleFor(x => x.IdToken).NotEmpty().WithMessage("Thiếu thông tin đăng nhập Google.");
}

/// <summary>
/// The Google account already linked → sign in. Not linked but an account has that (Google-verified) e-mail → link it, sign in.
/// No account → create one (needs the consent to the terms, Nghị định 13/2023), with an unusable random password — the person
/// can set one with "Quên mật khẩu" by e-mail.
/// </summary>
public sealed class GoogleLoginHandler(IApplicationDbContext db, IGoogleTokenVerifier google, SessionService sessions, IPasswordHasher hasher, IClock clock)
    : IRequestHandler<GoogleLoginCommand, AuthResult>
{
    public async Task<AuthResult> Handle(GoogleLoginCommand request, CancellationToken ct)
    {
        if (google.ClientId is null) throw new ConflictException("Đăng nhập bằng Google chưa được bật.", "GOOGLE_DISABLED");
        var identity = await google.VerifyAsync(request.IdToken, ct);
        if (!identity.EmailVerified) throw new AuthenticationFailedException("Email của tài khoản Google chưa được xác minh.", "GOOGLE_EMAIL_UNVERIFIED");
        var now = clock.UtcNow;
        var email = identity.Email.Trim().ToLowerInvariant();

        var linked = await db.UserIdentities.FirstOrDefaultAsync(i => i.Provider == ExternalProvider.Google && i.ProviderKey == identity.Subject, ct);
        var user = linked is not null
            ? await db.Users.FirstOrDefaultAsync(u => u.Id == linked.UserId, ct)
            : await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        var isNew = user is null;
        if (user is null)
        {
            if (!request.AcceptTerms)
                throw new ConflictException("Vui lòng đồng ý Điều khoản sử dụng và Chính sách xử lý dữ liệu cá nhân để tạo tài khoản.", "NEED_CONSENT");
            var name = string.IsNullOrWhiteSpace(identity.Name) ? email.Split('@')[0] : identity.Name.Trim();
            user = User.Register(null, email, hasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), name[..Math.Min(name.Length, 100)], now);
            db.Users.Add(user);
        }
        if (user.Status == UserStatus.Locked)
            throw new AuthenticationFailedException("Tài khoản đã bị khoá. Vui lòng liên hệ bộ phận hỗ trợ.", "ACCOUNT_LOCKED");
        if (user.Status == UserStatus.Deleted) throw new AuthenticationFailedException();
        if (linked is null) db.UserIdentities.Add(new UserIdentity(user.Id, ExternalProvider.Google, identity.Subject, email, now));

        // A new account carries its first sign-in; an existing one is stamped set-based (concurrent sign-ins, L061)
        if (isNew) user.RecordSuccessfulLogin(now);
        // The unique (provider, key) and e-mail indexes decide two first sign-ins at the same moment
        await db.SaveChangesAsync(ct);
        if (!isNew) await LoginStamp.RecordAsync(db, user, now, ct);
        return await sessions.StartAsync(user, request.Device, ct);
    }
}
