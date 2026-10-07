using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Security;

namespace ShopHub.UnitTests.SourceScan;

/// <summary>
/// Every endpoint chooses exactly one way in: [RequirePermission] (platform admin RBAC), [AllowAnonymous],
/// or [OwnerGuarded("reason")] (signed-in user, ownership filtered in SQL). A bare [Authorize] is not enough.
/// </summary>
public class EndpointAuthorisationTests
{
    private static IEnumerable<(Type Controller, MethodInfo Action)> Endpoints() =>
        typeof(RequirePermissionAttribute).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            // Inherited actions too (L080): an endpoint on a base controller must choose its way in like any other
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any())
                .Select(m => (t, m)));

    [Fact]
    public void There_are_endpoints_to_check()
    {
        Endpoints().Should().NotBeEmpty();
    }

    [Fact]
    public void Every_endpoint_declares_exactly_one_access_mode()
    {
        var problems = new List<string>();
        foreach (var (controller, action) in Endpoints())
        {
            var attrs = action.GetCustomAttributes(true).Concat(controller.GetCustomAttributes(true)).ToList();
            var modes = new[]
            {
                attrs.OfType<RequirePermissionAttribute>().Any(),
                attrs.OfType<AllowAnonymousAttribute>().Any(),
                attrs.OfType<OwnerGuardedAttribute>().Any(),
            }.Count(x => x);

            if (modes != 1)
                problems.Add($"{controller.Name}.{action.Name}: {modes} chế độ truy cập (cần đúng 1)");

            var bareAuthorize = attrs.OfType<AuthorizeAttribute>()
                .Any(a => a is not RequirePermissionAttribute and not OwnerGuardedAttribute);
            if (bareAuthorize)
                problems.Add($"{controller.Name}.{action.Name}: dùng [Authorize] trần — không phải là canh quyền");
        }

        problems.Should().BeEmpty();
    }

    [Fact]
    public void Owner_guarded_endpoints_state_a_reason()
    {
        var missing = Endpoints()
            .SelectMany(e => e.Action.GetCustomAttributes<OwnerGuardedAttribute>().Concat(e.Controller.GetCustomAttributes<OwnerGuardedAttribute>())
                .Where(a => string.IsNullOrWhiteSpace(a.Reason) || a.Reason.Length < 10)
                .Select(_ => $"{e.Controller.Name}.{e.Action.Name}"))
            .ToList();

        missing.Should().BeEmpty("mỗi endpoint tự canh quyền chủ sở hữu phải ghi rõ lý do");
    }
}
