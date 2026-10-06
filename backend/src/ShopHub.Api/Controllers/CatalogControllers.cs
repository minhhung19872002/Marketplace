using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using ShopHub.Api.Common;
using ShopHub.Api.Security;
using ShopHub.Application.Features.Catalog;
using ShopHub.Application.Features.Media;

namespace ShopHub.Api.Controllers;

[Route("api/media")]
[OwnerGuarded("Tệp tải lên thuộc về người tải; khi gắn vào sản phẩm/hồ sơ, chỉ dùng được tệp của chính mình.")]
public sealed class MediaController : ApiControllerBase
{
    // Above the largest allowed upload (video 30 MB); the handler applies the per-purpose limits
    private const long MaxRequestBytes = 32L * 1024 * 1024;

    /// <summary>
    /// Upload one file (multipart field "file"). The type is decided from the bytes, not the declared content type.
    /// Images are re-encoded to WebP in 3 sizes (metadata removed); KYC files go to a private bucket.
    /// </summary>
    [HttpPost("{purpose}")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [EnableRateLimiting(Hosting.ApiServiceExtensions.UploadRateLimit)]
    [ProducesResponseType<ApiResponse<MediaAssetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Upload(MediaPurpose purpose, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("Vui lòng chọn tệp.", [new ApiError("file", "Vui lòng chọn tệp.")]));
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return OkData(await Sender.Send(new UploadMediaCommand(ms.ToArray(), purpose), ct));
    }
}

[Route("api/account/avatar")]
[OwnerGuarded("Đặt ảnh đại diện cho chính người gọi, chỉ từ tệp do người đó tải lên.")]
public sealed class AvatarController : ApiControllerBase
{
    public record AvatarRequest(Guid AssetId);

    [HttpPut]
    [ProducesResponseType<ApiResponse<string>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Set([FromBody] AvatarRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new SetAvatarCommand(body.AssetId), ct), "Đã cập nhật ảnh đại diện.");
}

[Route("api")]
public sealed class CatalogController : ApiControllerBase
{
    [HttpGet("categories")]
    [OutputCache(PolicyName = OutputCachePolicies.Storefront)]
    [AllowAnonymous]
    [ResponseCache(Duration = 300)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CategoryNodeDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Categories(CancellationToken ct) => OkData(await Sender.Send(new GetCategoryTreeQuery(), ct));

    [HttpGet("categories/{id:guid}/attributes")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CategoryAttributeDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Attributes(Guid id, CancellationToken ct) => OkData(await Sender.Send(new GetCategoryAttributesQuery(id), ct));

    [HttpGet("brands")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<IReadOnlyList<BrandDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Brands([FromQuery] string? q, CancellationToken ct) => OkData(await Sender.Send(new SearchBrandsQuery(q), ct));
}
