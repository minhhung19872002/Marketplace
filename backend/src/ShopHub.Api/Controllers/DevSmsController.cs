using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopHub.Api.Common;
using ShopHub.Application.Identity;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Api.Controllers;

/// <summary>
/// Inbox of the simulated SMS provider (like Mailpit for SMS) so OTP flows can be demoed and end-to-end tested.
/// Exists only in the Development environment with SH_SMS_PROVIDER=simulated; everywhere else it answers 404.
/// </summary>
[Route("api/dev/sms")]
public sealed class DevSmsController(IWebHostEnvironment env, ShopHubSettings settings, ShopHubDbContext db) : ApiControllerBase
{
    public record SmsDto(string To, string Content, DateTimeOffset CreatedAt);

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SmsDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string to, CancellationToken ct)
    {
        if (!env.IsDevelopment() || settings.SmsProvider != "simulated") return NotFound();
        var phone = Identifiers.NormalisePhone(to) ?? to;
        var messages = await db.SimulatedSms.AsNoTracking()
            .Where(s => s.To == phone)
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Take(20)
            .Select(s => new SmsDto(s.To, s.Content, s.CreatedAt))
            .ToListAsync(ct);
        return OkData(messages);
    }
}
