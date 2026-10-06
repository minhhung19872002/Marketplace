using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Common;
using ShopHub.Api.Security;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Admin;
using ShopHub.Application.Security;

namespace ShopHub.Api.Controllers.Admin;

[Route("api/admin")]
public sealed class IamController : ApiControllerBase
{
    [HttpGet("permissions")]
    [RequirePermission(Permissions.RoleView)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PermissionDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Permissions_(CancellationToken ct) => OkData(await Sender.Send(new ListPermissionsQuery(), ct));

    [HttpGet("roles")]
    [RequirePermission(Permissions.RoleView)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<RoleDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Roles(CancellationToken ct) => OkData(await Sender.Send(new ListRolesQuery(), ct));

    public record RoleRequest(string Code, string Name, string Description, IReadOnlyList<string> Permissions);

    [HttpPost("roles")]
    [RequirePermission(Permissions.RoleManage)]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateRole([FromBody] RoleRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new SaveRoleCommand(null, body.Code, body.Name, body.Description, body.Permissions), ct), "Đã tạo vai trò.");

    [HttpPut("roles/{id:guid}")]
    [RequirePermission(Permissions.RoleManage)]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] RoleRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new SaveRoleCommand(id, body.Code, body.Name, body.Description, body.Permissions), ct), "Đã lưu vai trò.");

    [HttpDelete("roles/{id:guid}")]
    [RequirePermission(Permissions.RoleManage)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteRole(Guid id, CancellationToken ct)
    {
        await Sender.Send(new DeleteRoleCommand(id), ct);
        return OkData<object?>(null, "Đã xoá vai trò.");
    }

    [HttpGet("users")]
    [RequirePermission(Permissions.UserView)]
    [ProducesResponseType<ApiResponse<PagedResult<AdminUserDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Users([FromQuery] ListUsersQuery query, CancellationToken ct) => OkData(await Sender.Send(query, ct));

    public record UserRolesRequest(IReadOnlyList<Guid> RoleIds);

    [HttpPut("users/{id:guid}/roles")]
    [RequirePermission(Permissions.UserAssignRole)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetRoles(Guid id, [FromBody] UserRolesRequest body, CancellationToken ct)
    {
        await Sender.Send(new SetUserRolesCommand(id, body.RoleIds), ct);
        return OkData<object?>(null, "Đã cập nhật vai trò.");
    }

    public record LockRequest(string Reason);

    /// <summary>Lock an account. Its open sessions stop working on the very next request.</summary>
    [HttpPost("users/{id:guid}/lock")]
    [RequirePermission(Permissions.UserLock)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Lock(Guid id, [FromBody] LockRequest body, CancellationToken ct)
    {
        await Sender.Send(new LockUserCommand(id, body.Reason), ct);
        return OkData<object?>(null, "Đã khoá tài khoản.");
    }

    [HttpPost("users/{id:guid}/unlock")]
    [RequirePermission(Permissions.UserLock)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken ct)
    {
        await Sender.Send(new UnlockUserCommand(id), ct);
        return OkData<object?>(null, "Đã mở khoá tài khoản.");
    }
}
