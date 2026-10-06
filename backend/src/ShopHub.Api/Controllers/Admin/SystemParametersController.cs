using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Common;
using ShopHub.Api.Security;
using ShopHub.Application.Features.SystemConfig;
using ShopHub.Application.Security;

namespace ShopHub.Api.Controllers.Admin;

[Route("api/admin/system-parameters")]
public sealed class SystemParametersController : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.SystemParameterView)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SystemParameterDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? group, CancellationToken ct) =>
        OkData(await Sender.Send(new ListSystemParametersQuery(group), ct));

    public record UpdateParameterRequest(string Value, uint? Version);

    /// <summary>Change one parameter. Send the version you loaded; a stale version returns 409.</summary>
    [HttpPut("{key}")]
    [RequirePermission(Permissions.SystemParameterUpdate)]
    [ProducesResponseType<ApiResponse<SystemParameterDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(string key, [FromBody] UpdateParameterRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new UpdateSystemParameterCommand(key, body.Value, body.Version), ct), "Đã lưu tham số.");
}
