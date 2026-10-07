using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Common;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Admin;
using ShopHub.Application.Features.Seller;

namespace ShopHub.Api.Controllers;

[Route("api/site")]
public sealed class SiteController : ApiControllerBase
{
    /// <summary>Platform identity for header/footer (name, hotline, legal entity, social links) — same answer as <c>GET /api/site</c>.</summary>
    [HttpGet("info")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<SiteInfoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInfo(CancellationToken ct) => OkData(await Sender.Send(new SiteInfoQuery(), ct));

    /// <summary>Banks money can be paid out to (shop payouts, buyer refunds) — the one list every form reads.</summary>
    [HttpGet("banks")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<IReadOnlyList<BankCatalogue.Bank>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Banks(CancellationToken ct) => OkData(await Sender.Send(new BanksQuery(), ct));

    /// <summary>Active carriers (code, name, COD) — the seller registration form picks from these.</summary>
    [HttpGet("carriers")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CarrierOptionDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Carriers(CancellationToken ct) => OkData(await Sender.Send(new ActiveCarriersQuery(), ct));
}
