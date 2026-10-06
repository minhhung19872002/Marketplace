using Hangfire;
using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Common;
using ShopHub.Api.Security;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Promo;
using ShopHub.Application.Security;
using ShopHub.Infrastructure.Jobs;

namespace ShopHub.Api.Controllers.Admin;

[Route("api/admin/vouchers")]
public sealed class VouchersAdminController : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.VoucherManage)]
    [ProducesResponseType<ApiResponse<PagedResult<VoucherDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ListPlatformVouchersQuery query, CancellationToken ct) => OkData(await Sender.Send(query, ct));

    [HttpPost]
    [RequirePermission(Permissions.VoucherManage)]
    [ProducesResponseType<ApiResponse<VoucherDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] VoucherInput body, CancellationToken ct) =>
        OkData(await Sender.Send(new SavePlatformVoucherCommand(null, body), ct), "Đã tạo voucher.");

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.VoucherManage)]
    [ProducesResponseType<ApiResponse<VoucherDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] VoucherInput body, CancellationToken ct) =>
        OkData(await Sender.Send(new SavePlatformVoucherCommand(id, body), ct), "Đã lưu voucher.");

    [HttpPost("{id:guid}/stop")]
    [RequirePermission(Permissions.VoucherManage)]
    [ProducesResponseType<ApiResponse<VoucherDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Stop(Guid id, CancellationToken ct) =>
        OkData(await Sender.Send(new StopPlatformVoucherCommand(id), ct), "Đã dừng voucher.");
}

[Route("api/admin/users/{userId:guid}/coins")]
public sealed class CoinsAdminController : ApiControllerBase
{
    public record GrantRequest(long Delta, string Reason);

    /// <summary>Add (or take back) ShopHub Xu; returns the new balance.</summary>
    [HttpPost]
    [RequirePermission(Permissions.CoinGrant)]
    [ProducesResponseType<ApiResponse<long>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Grant(Guid userId, [FromBody] GrantRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new GrantCoinsCommand(userId, body.Delta, body.Reason), ct), "Đã cập nhật ShopHub Xu.");
}

// Not under /api/admin/jobs: that prefix belongs to the Hangfire dashboard
[Route("api/admin/job-runs")]
public sealed class JobsAdminController(IRecurringJobManager jobs) : ApiControllerBase
{
    /// <summary>Run a recurring job now (outbox, counters, unpaid-order expiry) instead of waiting for its schedule.</summary>
    [HttpPost("{id}")]
    [RequirePermission(Permissions.JobRun)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public IActionResult Run(string id)
    {
        if (!JobIds.Runnable.Contains(id)) throw new NotFoundException("Không tìm thấy việc nền.");
        jobs.Trigger(id);
        return OkData<object?>(null, "Đã đưa việc nền vào hàng đợi.");
    }
}
