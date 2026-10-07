using System.Security.Cryptography;
using System.Text;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.DataProtection;
using ShopHub.Application.Identity;
using ShopHub.Application.Security;
using StackExchange.Redis;

namespace ShopHub.Api.Security;

/// <summary>
/// Opening the Hangfire dashboard from a browser (L078): a page load sends no Authorization header, so the admin app
/// asks for a one-use ticket (60 s, kept hashed in Redis), opens <c>/api/admin/jobs?ticket=…</c>, and the ticket is
/// traded for an httpOnly cookie scoped to <c>/api/admin/jobs</c> (30 min, data-protected). Every dashboard request
/// re-checks that the person is still active and still holds SYS.JOB.VIEW.
/// </summary>
public sealed class JobDashboardAccess(IConnectionMultiplexer redis, IDataProtectionProvider protection)
{
    public const string Path = "/api/admin/jobs";
    public const string CookieName = "sh_jobs";
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan CookieLifetime = TimeSpan.FromMinutes(30);

    private IDataProtector Protector => protection.CreateProtector("ShopHub.JobDashboard.v1");

    private static string TicketKey(string ticket) => $"sh:jobs-ticket:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ticket)))}";

    public async Task<string> IssueTicketAsync(Guid userId)
    {
        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await redis.GetDatabase().StringSetAsync(TicketKey(ticket), userId.ToString("N"), TicketLifetime);
        return ticket;
    }

    /// <summary>The user the ticket was issued to; a ticket works once.</summary>
    public async Task<Guid?> RedeemAsync(string ticket)
    {
        if (ticket.Length != 64) return null;
        var value = await redis.GetDatabase().StringGetDeleteAsync(TicketKey(ticket));
        return Guid.TryParseExact(value.ToString(), "N", out var id) ? id : null;
    }

    public string CookieFor(Guid userId, DateTimeOffset now) =>
        Protector.Protect($"{userId:N}|{now.Add(CookieLifetime).ToUnixTimeSeconds()}");

    public Guid? UserOf(string? cookie, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(cookie)) return null;
        try
        {
            var parts = Protector.Unprotect(cookie).Split('|');
            return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out var id) && long.TryParse(parts[1], out var exp)
                   && DateTimeOffset.FromUnixTimeSeconds(exp) > now ? id : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>Swaps <c>?ticket=</c> for the dashboard cookie, then redirects to the same page without the ticket in the URL.</summary>
public sealed class JobDashboardTicketMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, JobDashboardAccess access)
    {
        if (http.Request.Path.StartsWithSegments(JobDashboardAccess.Path) && http.Request.Query.TryGetValue("ticket", out var ticket))
        {
            if (await access.RedeemAsync(ticket.ToString()) is not { } userId)
            {
                http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            http.Response.Cookies.Append(JobDashboardAccess.CookieName, access.CookieFor(userId, DateTimeOffset.UtcNow), new CookieOptions
            {
                HttpOnly = true,
                Secure = http.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                Path = JobDashboardAccess.Path,
                MaxAge = JobDashboardAccess.CookieLifetime,
            });
            http.Response.Redirect($"{http.Request.PathBase}{http.Request.Path}");
            return;
        }
        await next(http);
    }
}

/// <summary>Hangfire dashboard: platform admins holding SYS.JOB.VIEW — by bearer token (API clients) or the ticket cookie (browser).</summary>
public sealed class JobDashboardAuthorizationFilter : IDashboardAsyncAuthorizationFilter
{
    public async Task<bool> AuthorizeAsync(DashboardContext context)
    {
        var http = context.GetHttpContext();
        if (PermissionClaims.Has(http.User, Permissions.JobDashboardView)) return true;
        var access = http.RequestServices.GetRequiredService<JobDashboardAccess>();
        if (access.UserOf(http.Request.Cookies[JobDashboardAccess.CookieName], DateTimeOffset.UtcNow) is not { } userId) return false;
        using var scope = http.RequestServices.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        if (!await sessions.IsActiveAsync(userId, http.RequestAborted)) return false;
        var permissions = await sessions.GetPermissionsAsync(userId, http.RequestAborted);
        return permissions.Contains(Permissions.JobDashboardView) || permissions.Contains(Permissions.All);
    }
}
