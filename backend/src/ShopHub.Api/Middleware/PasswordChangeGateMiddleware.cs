using ShopHub.Api.Common;

namespace ShopHub.Api.Middleware;

/// <summary>
/// An account flagged "must change password" (seeded admin) may only reach sign-in/out, its own profile and the
/// change-password endpoint until it picks a new password. Enforced server-side, not just in the admin UI.
/// </summary>
public sealed class PasswordChangeGateMiddleware(RequestDelegate next)
{
    private static readonly string[] AllowedPrefixes = ["/api/auth/", "/api/account/me", "/api/account/password", "/health"];

    public async Task InvokeAsync(HttpContext context)
    {
        var mustChange = context.User.FindFirst("pcr")?.Value == "true";
        var path = context.Request.Path.Value ?? string.Empty;
        if (mustChange && !AllowedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(ApiResponse.Fail("Bạn cần đổi mật khẩu trước khi tiếp tục."));
            return;
        }
        await next(context);
    }
}
