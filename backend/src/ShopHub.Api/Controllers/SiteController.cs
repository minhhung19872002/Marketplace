using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Common;
using ShopHub.Application.Features.SystemConfig;

namespace ShopHub.Api.Controllers;

[Route("api/site")]
public sealed class SiteController : ApiControllerBase
{
    /// <summary>Platform identity for header/footer (name, hotline, legal entity) — all from system parameters.</summary>
    [HttpGet("info")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<SiteInfoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInfo(CancellationToken ct) => OkData(await Sender.Send(new GetSiteInfoQuery(), ct));
}
