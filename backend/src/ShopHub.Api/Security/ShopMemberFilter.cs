using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using ShopHub.Api.Common;
using ShopHub.Application.Abstractions;

namespace ShopHub.Api.Security;

/// <summary>
/// Every <c>/api/seller/shops/{shopId}/…</c> route first checks that the caller is staff of that shop (L084): someone
/// else's shop is "not found" before anything else is looked at — validation, permissions, the request body. The handler
/// still checks the permission the action needs (SellerAccess).
/// </summary>
public sealed class ShopMemberFilter : IAsyncActionFilter
{
    public const string Prefix = "api/seller/shops/{shopId";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor.AttributeRouteInfo?.Template?.StartsWith(Prefix, StringComparison.Ordinal) == true
            && context.HttpContext.User.Identity?.IsAuthenticated == true)
        {
            var services = context.HttpContext.RequestServices;
            var userId = services.GetRequiredService<ICurrentUser>().UserId;
            var member = Guid.TryParse(context.RouteData.Values["shopId"]?.ToString(), out var shopId) && userId is { } uid
                         && await services.GetRequiredService<IApplicationDbContext>().ShopStaff.AsNoTracking()
                             .AnyAsync(s => s.ShopId == shopId && s.UserId == uid, context.HttpContext.RequestAborted);
            if (!member)
            {
                context.Result = new NotFoundObjectResult(ApiResponse.Fail("Không tìm thấy shop."));
                return;
            }
        }
        await next();
    }
}
