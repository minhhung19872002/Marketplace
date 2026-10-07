using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ShopHub.Api.Common;
using ShopHub.Application.Identity;
using StackExchange.Redis;

namespace ShopHub.Api.Security;

/// <summary>
/// Spec 6.1 "theo IP và theo tài khoản/SĐT" (L079): on top of the per-IP policy, at most N requests a minute for the
/// same phone / e-mail / username (normalised, so "0912…" and "+84912…" are one), counted in Redis so every API
/// instance shares it. Answers 429 with the same Vietnamese JSON as the IP limiter.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdentifierRateLimitAttribute(string property) : Attribute, IAsyncActionFilter
{
    // SH_RATE_LIMIT_IDENTIFIER (per minute); production default 10
    public static int PermitsPerMinute { get; set; } = 10;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var raw = context.ActionArguments.Values.Select(v => v?.GetType().GetProperty(property)?.GetValue(v) as string).FirstOrDefault(v => v is not null);
        if (string.IsNullOrWhiteSpace(raw))
        {
            await next();
            return;
        }
        var id = Identifiers.NormaliseOtpTarget(raw) ?? raw.Trim().ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..32];
        var key = $"sh:rl:id:{context.ActionDescriptor.AttributeRouteInfo?.Template}:{hash}";
        var redis = context.HttpContext.RequestServices.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        var count = await redis.StringIncrementAsync(key);
        if (count == 1) await redis.KeyExpireAsync(key, TimeSpan.FromMinutes(1));
        if (count > PermitsPerMinute)
        {
            var ttl = await redis.KeyTimeToLiveAsync(key);
            context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling((ttl ?? TimeSpan.FromMinutes(1)).TotalSeconds)).ToString();
            context.Result = new ObjectResult(ApiResponse.Fail("Bạn thao tác quá nhanh, vui lòng thử lại sau giây lát.")) { StatusCode = StatusCodes.Status429TooManyRequests };
            return;
        }
        await next();
    }
}
