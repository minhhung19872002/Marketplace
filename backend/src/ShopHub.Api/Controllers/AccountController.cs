using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShopHub.Api.Common;
using ShopHub.Api.Hosting;
using ShopHub.Api.Security;
using ShopHub.Application.Features.Account;
using ShopHub.Application.Identity;
using ShopHub.Domain.Iam;

namespace ShopHub.Api.Controllers;

[Route("api/account")]
[OwnerGuarded("Mọi thao tác chỉ trên tài khoản của chính người gọi (user id lấy từ token, lọc trong câu SQL).")]
public sealed class AccountController : ApiControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<ApiResponse<MeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct) => OkData(await Sender.Send(new GetMeQuery(), ct));

    [HttpPut("profile")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileCommand body, CancellationToken ct)
    {
        await Sender.Send(body, ct);
        return OkData<object?>(null, "Đã lưu hồ sơ.");
    }

    /// <summary>Change password; every OTHER device is signed out.</summary>
    [HttpPut("password")]
    [EnableRateLimiting(ApiServiceExtensions.AuthRateLimit)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand body, CancellationToken ct)
    {
        await Sender.Send(body, ct);
        return OkData<object?>(null, "Đã đổi mật khẩu. Các thiết bị khác đã được đăng xuất.");
    }

    [HttpGet("sessions")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SessionDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Sessions(CancellationToken ct) => OkData(await Sender.Send(new ListSessionsQuery(), ct));

    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken ct)
    {
        await Sender.Send(new RevokeSessionCommand(sessionId), ct);
        return OkData<object?>(null, "Đã đăng xuất thiết bị.");
    }

    public record ContactRequest(string NewValue);

    /// <summary>Send an OTP to the NEW phone number or email.</summary>
    [HttpPost("contact/otp")]
    [EnableRateLimiting(ApiServiceExtensions.OtpRateLimit)]
    [ProducesResponseType<ApiResponse<OtpIssued>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestContactChange([FromBody] ContactRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new RequestContactChangeCommand(body.NewValue), ct), "Đã gửi mã xác thực.");

    [HttpPut("contact")]
    [EnableRateLimiting(ApiServiceExtensions.AuthRateLimit)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmContactChange([FromBody] ConfirmContactChangeCommand body, CancellationToken ct)
    {
        await Sender.Send(body, ct);
        return OkData<object?>(null, "Đã cập nhật thông tin liên hệ.");
    }

    /// <summary>Download my personal data (Decree 13/2023).</summary>
    [HttpGet("export")]
    [ProducesResponseType<ApiResponse<MyDataExport>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Export(CancellationToken ct) => OkData(await Sender.Send(new ExportMyDataQuery(), ct));

    /// <summary>Delete my account: blocked while orders/balances are pending; personal data is anonymised.</summary>
    [HttpPost("delete")]
    [EnableRateLimiting(ApiServiceExtensions.AuthRateLimit)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete([FromBody] DeleteMyAccountCommand body, CancellationToken ct)
    {
        await Sender.Send(body, ct);
        Response.Cookies.Delete(AuthController.RefreshCookie, new CookieOptions { Path = "/api/auth" });
        return OkData<object?>(null, "Tài khoản đã được xoá.");
    }
}

[Route("api/account/addresses")]
[OwnerGuarded("Sổ địa chỉ của chính người gọi; truy vấn luôn lọc theo user id, địa chỉ của người khác trả 404.")]
public sealed class AddressesController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<AddressDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => OkData(await Sender.Send(new ListAddressesQuery(), ct));

    [HttpPost]
    [ProducesResponseType<ApiResponse<AddressDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] AddressInput body, CancellationToken ct) =>
        OkData(await Sender.Send(new CreateAddressCommand(body), ct), "Đã thêm địa chỉ.");

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ApiResponse<AddressDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] AddressInput body, CancellationToken ct) =>
        OkData(await Sender.Send(new UpdateAddressCommand(id, body), ct), "Đã cập nhật địa chỉ.");

    [HttpPost("{id:guid}/default")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct)
    {
        await Sender.Send(new SetDefaultAddressCommand(id), ct);
        return OkData<object?>(null, "Đã đặt làm địa chỉ mặc định.");
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await Sender.Send(new DeleteAddressCommand(id), ct);
        return OkData<object?>(null, "Đã xoá địa chỉ.");
    }
}

[Route("api/admin-divisions")]
public sealed class AdminDivisionsController : ApiControllerBase
{
    /// <summary>Provinces (no parent) or the children of a province/district.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(Duration = 3600)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<AdminDivisionDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? parent, CancellationToken ct) =>
        OkData(await Sender.Send(new ListAdminDivisionsQuery(parent), ct));
}
