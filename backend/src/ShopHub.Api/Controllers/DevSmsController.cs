using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopHub.Api.Common;
using ShopHub.Api.Security;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Identity;
using ShopHub.Application.SystemConfig;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Api.Controllers;

/// <summary>
/// Inbox of the simulated SMS provider (like Mailpit for SMS) so OTP flows can be demoed and end-to-end tested.
/// Open in Development with SH_SMS_PROVIDER=simulated; on a demo site only to the e2e suite's key and numbers
/// (<see cref="E2eOtpAccess"/>); everywhere else it answers 404.
/// </summary>
[Route("api/dev/sms")]
public sealed class DevSmsController(IWebHostEnvironment env, ShopHubSettings settings, ISystemParameters parameters, ShopHubDbContext db)
    : ApiControllerBase
{
    public record SmsDto(string To, string Content, DateTimeOffset CreatedAt);

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SmsDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string to, [FromHeader(Name = E2eOtpAccess.Header)] string? key, CancellationToken ct)
    {
        var phone = Identifiers.NormalisePhone(to) ?? to;
        var demo = !env.IsDevelopment() && await SiteMode.IsDemoAsync(parameters, ct);
        if (!E2eOtpAccess.Allows(env.IsDevelopment(), settings.SmsProvider, demo, settings.E2eOtpKey, key, phone)) return NotFound();
        var messages = await db.SimulatedSms.AsNoTracking()
            .Where(s => s.To == phone)
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Take(20)
            .Select(s => new SmsDto(s.To, s.Content, s.CreatedAt))
            .ToListAsync(ct);
        return OkData(messages);
    }
}
