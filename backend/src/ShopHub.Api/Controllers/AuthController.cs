using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShopHub.Api.Common;
using ShopHub.Api.Security;
using ShopHub.Api.Hosting;
using ShopHub.Application.Features.Auth;
using ShopHub.Application.Identity;
using ShopHub.Domain.Iam;
using ShopHub.Application.Features.Cart;

namespace ShopHub.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController : ApiControllerBase
{
    // Browsers get the refresh token as an httpOnly cookie scoped to /api/auth; mobile apps use the body copy
    public const string RefreshCookie = "sh_rt";

    public record SendOtpRequest(string Target, OtpPurpose Purpose);

    /// <summary>Send a 6-digit code (Register / Login / ResetPassword). Same answer whether or not an account exists.</summary>
    [HttpPost("otp/send")]
    [IdentifierRateLimit("Target")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceExtensions.OtpRateLimit)]
    [ProducesResponseType<ApiResponse<OtpIssued>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new SendOtpCommand(body.Target, body.Purpose), ct), "Đã gửi mã xác thực.");

    public record VerifyOtpRequest(string Target, OtpPurpose Purpose, string Code);

    /// <summary>Check a code and receive a one-time ticket for /register or /reset-password.</summary>
    [HttpPost("otp/verify")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceExtensions.AuthRateLimit)]
    [ProducesResponseType<ApiResponse<OtpTicketDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new VerifyOtpCommand(body.Target, body.Purpose, body.Code), ct));

    public record RegisterRequest(string Target, string Ticket, string Password, string FullName, bool AcceptTerms, string? Device);

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceExtensions.AuthRateLimit)]
    [ProducesResponseType<ApiResponse<AuthResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest body, CancellationToken ct) =>
        await SignedInAsync(await Sender.Send(new RegisterCommand(body.Target, body.Ticket, body.Password, body.FullName,
            body.AcceptTerms, DeviceOf(body.Device)), ct), ct, "Đăng ký thành công.");

    /// <summary>Sign-in methods the site offers besides the password (the Google client id when enabled).</summary>
    [HttpGet("providers")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<AuthProvidersDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Providers(CancellationToken ct) => OkData(await Sender.Send(new AuthProvidersQuery(), ct));

    public record GoogleRequest(string IdToken, string? Device, bool AcceptTerms);

    /// <summary>Đăng nhập Google: the ID token from Google Identity Services; a new account needs acceptTerms (409 NEED_CONSENT otherwise).</summary>
    [HttpPost("google")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceExtensions.AuthRateLimit)]
    [ProducesResponseType<ApiResponse<AuthResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Google([FromBody] GoogleRequest body, CancellationToken ct) =>
        await SignedInAsync(await Sender.Send(new GoogleLoginCommand(body.IdToken, DeviceOf(body.Device), body.AcceptTerms), ct), ct);

    public record LoginRequest(string Identifier, string Password, string? Device);

    /// <summary>Phone / email / username + password. Wrong credentials always get the same message.</summary>
    [HttpPost("login")]
    [IdentifierRateLimit("Identifier")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceExtensions.AuthRateLimit)]
    [ProducesResponseType<ApiResponse<AuthResult>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest body, CancellationToken ct) =>
        await SignedInAsync(await Sender.Send(new LoginCommand(body.Identifier, body.Password, DeviceOf(body.Device)), ct), ct);

    public record LoginOtpRequest(string Phone, string Code, string? Device);

    [HttpPost("login-otp")]
    [IdentifierRateLimit("Phone")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceExtensions.AuthRateLimit)]
    [ProducesResponseType<ApiResponse<AuthResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LoginOtp([FromBody] LoginOtpRequest body, CancellationToken ct) =>
        await SignedInAsync(await Sender.Send(new LoginWithOtpCommand(body.Phone, body.Code, DeviceOf(body.Device)), ct), ct);

    public record RefreshRequest(string? RefreshToken, string? Device);

    /// <summary>Rotate the refresh token (body, or the httpOnly cookie). A reused token revokes the whole session.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<AuthResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest? body, CancellationToken ct)
    {
        var token = body?.RefreshToken ?? Request.Cookies[RefreshCookie] ?? string.Empty;
        return SignedIn(await Sender.Send(new RefreshCommand(token, DeviceOf(body?.Device)), ct));
    }

    public record LogoutRequest(string? RefreshToken);

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? body, CancellationToken ct)
    {
        await Sender.Send(new LogoutCommand(body?.RefreshToken ?? Request.Cookies[RefreshCookie]), ct);
        Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTimeOffset.UnixEpoch));
        return OkData<object?>(null, "Đã đăng xuất.");
    }

    public record ForgotPasswordRequest(string Target);

    [HttpPost("forgot-password")]
    [IdentifierRateLimit("Target")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceExtensions.OtpRateLimit)]
    [ProducesResponseType<ApiResponse<OtpIssued>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new SendOtpCommand(body.Target, OtpPurpose.ResetPassword), ct),
            "Nếu tài khoản tồn tại, mã xác thực đã được gửi.");

    public record ResetPasswordRequest(string Target, string Ticket, string NewPassword);

    /// <summary>Set a new password with the ticket from /otp/verify. Every session of the account is ended.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceExtensions.AuthRateLimit)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest body, CancellationToken ct)
    {
        await Sender.Send(new ResetPasswordCommand(body.Target, body.Ticket, body.NewPassword), ct);
        return OkData<object?>(null, "Đã đặt lại mật khẩu. Vui lòng đăng nhập lại.");
    }

    /// <summary>A fresh sign-in also moves the guest cart (cookie sh_cart) into the account's cart (spec 3.4).</summary>
    private async Task<OkObjectResult> SignedInAsync(AuthResult result, CancellationToken ct, string message = "")
    {
        if (Request.Cookies.TryGetValue(CartController.GuestCookie, out var guestToken) && !string.IsNullOrEmpty(guestToken))
        {
            await Sender.Send(new MergeGuestCartCommand(result.User.Id, guestToken), ct);
            Response.Cookies.Delete(CartController.GuestCookie, CartController.GuestCookieOptions(Request));
        }
        return SignedIn(result, message);
    }

    private OkObjectResult SignedIn(AuthResult result, string message = "")
    {
        Response.Cookies.Append(RefreshCookie, result.RefreshToken, CookieOptions(result.RefreshTokenExpiresAt));
        return OkData(result, message);
    }

    private CookieOptions CookieOptions(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = expires,
    };

    private string? DeviceOf(string? device) =>
        string.IsNullOrWhiteSpace(device) ? Request.Headers.UserAgent.ToString() : device;
}
