using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Common;
using ShopHub.Api.Security;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Audit;
using ShopHub.Application.Security;

namespace ShopHub.Api.Controllers.Admin;

[Route("api/admin/audit-logs")]
public sealed class AuditLogsController : ApiControllerBase
{
    /// <summary>Search the change journal by user, action, entity and time range (newest first).</summary>
    [HttpGet]
    [RequirePermission(Permissions.AuditLogView)]
    [ProducesResponseType<ApiResponse<PagedResult<AuditLogDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] ListAuditLogsQuery query, CancellationToken ct) =>
        OkData(await Sender.Send(query, ct));
}
