using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Security;

namespace ShopHub.Api.Security;

/// <summary>Platform-admin permission gate. [Authorize] alone is NOT a permission check.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(string permission) : AuthorizeAttribute(PolicyPrefix + permission)
{
    public const string PolicyPrefix = "perm:";
    public string Permission { get; } = permission;
}

/// <summary>
/// Endpoint open to any signed-in user that guards ownership itself (filters by owner inside the SQL query and
/// returns 404 for someone else's data). The reason is mandatory and reviewed by EndpointAuthorisationTests.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class OwnerGuardedAttribute(string reason) : AuthorizeAttribute
{
    public string Reason { get; } = reason;
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (PermissionClaims.Has(context.User, requirement.Permission)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

// Builds "perm:XYZ" policies on demand instead of registering one per permission
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return await base.GetPolicyAsync(policyName);

        var permission = policyName[RequirePermissionAttribute.PolicyPrefix.Length..];
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();
    }
}

public static class PermissionClaims
{
    public static bool Has(ClaimsPrincipal user, string permission) =>
        user.Identity?.IsAuthenticated == true &&
        user.FindAll(Permissions.ClaimType).Any(c => c.Value == permission || c.Value == Permissions.All);
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private HttpContext? Context => accessor.HttpContext;

    public Guid? UserId =>
        Guid.TryParse(Context?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context?.User.FindFirstValue("sub"), out var id)
            ? id
            : null;

    public Guid? SessionId => Guid.TryParse(Context?.User.FindFirstValue("sid"), out var sid) ? sid : null;

    // Only trustworthy after ForwardedHeaders has processed the request with the configured proxy ranges
    public string? IpAddress => Context?.Connection.RemoteIpAddress is { } ip
        ? (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString()
        : null;

    public string? UserAgent => Context?.Request.Headers.UserAgent.ToString();

    public bool HasPermission(string permission) => Context is not null && PermissionClaims.Has(Context.User, permission);
}
