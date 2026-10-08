using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Common;
using ShopHub.Api.Security;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Returns;
using ShopHub.Application.Features.Reviews;
using ShopHub.Application.Security;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Sales;

namespace ShopHub.Api.Controllers;

[Route("api")]
public sealed class ReviewsController : ApiControllerBase
{
    /// <summary>Visible reviews of a product with the star summary; filters: rating, with photo/video, with comment.</summary>
    [HttpGet("products/{productId:guid}/reviews")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<ProductReviewsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForProduct(Guid productId, [FromQuery] int? rating = null, [FromQuery] bool withMedia = false,
        [FromQuery] bool withComment = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? variant = null,
        CancellationToken ct = default) =>
        OkData(await Sender.Send(new ProductReviewsQuery(productId, rating, withMedia, withComment, page, pageSize, variant), ct));
}

[Route("api")]
[OwnerGuarded("Đánh giá và trả hàng của chính người mua (buyer_id lọc trong câu SQL); của người khác trả 404.")]
public sealed class BuyerAftercareController : ApiControllerBase
{
    [HttpGet("orders/{code}/reviews")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ReviewableItemDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reviewable(string code, CancellationToken ct) => OkData(await Sender.Send(new ReviewableItemsQuery(code), ct));

    public record ReviewRequest(int Rating, string? Content, IReadOnlyList<string>? Tags, bool Anonymous, IReadOnlyList<Guid>? MediaAssetIds);

    [HttpPost("orders/{code}/items/{orderItemId:guid}/review")]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Write(string code, Guid orderItemId, [FromBody] ReviewRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new WriteReviewCommand(code, orderItemId, body.Rating, body.Content, body.Tags, body.Anonymous, body.MediaAssetIds), ct),
            "Cảm ơn bạn đã đánh giá!");

    [HttpPut("reviews/{reviewId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Edit(Guid reviewId, [FromBody] ReviewRequest body, CancellationToken ct)
    {
        await Sender.Send(new EditReviewCommand(reviewId, body.Rating, body.Content, body.Tags, body.Anonymous, body.MediaAssetIds), ct);
        return OkData<object?>(null, "Đã cập nhật đánh giá.");
    }

    public record ReasonRequest(string Reason);

    /// <summary>"Hữu ích" on a review (G2-B2): one vote per buyer; DELETE takes it back. Returns the new count.</summary>
    [HttpPost("reviews/{reviewId:guid}/helpful")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Helpful(Guid reviewId, CancellationToken ct) =>
        OkData(await Sender.Send(new HelpfulReviewCommand(reviewId, true), ct));

    [HttpDelete("reviews/{reviewId:guid}/helpful")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> NotHelpful(Guid reviewId, CancellationToken ct) =>
        OkData(await Sender.Send(new HelpfulReviewCommand(reviewId, false), ct));

    [HttpPost("reviews/{reviewId:guid}/report")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Report(Guid reviewId, [FromBody] ReasonRequest body, CancellationToken ct)
    {
        await Sender.Send(new ReportReviewCommand(reviewId, body.Reason ?? string.Empty), ct);
        return OkData<object?>(null, "Đã gửi báo cáo, sàn sẽ xem xét.");
    }

    [HttpGet("orders/{code}/returnable")]
    [ProducesResponseType<ApiResponse<ReturnableDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Returnable(string code, CancellationToken ct) => OkData(await Sender.Send(new ReturnableQuery(code), ct));

    public record ReturnRequestBody(ReturnType Type, ReturnReason Reason, string Description, IReadOnlyList<ReturnLineInput> Lines, IReadOnlyList<Guid> EvidenceAssetIds);

    [HttpPost("orders/{code}/returns")]
    [ProducesResponseType<ApiResponse<ReturnDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateReturn(string code, [FromBody] ReturnRequestBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new CreateReturnCommand(code, body.Type, body.Reason, body.Description ?? string.Empty, body.Lines ?? [], body.EvidenceAssetIds ?? []), ct),
            "Đã gửi yêu cầu trả hàng / hoàn tiền.");

    [HttpGet("returns")]
    [ProducesResponseType<ApiResponse<PagedResult<ReturnDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyReturns([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        OkData(await Sender.Send(new MyReturnsQuery(page, pageSize), ct));

    [HttpGet("returns/{code}")]
    [ProducesResponseType<ApiResponse<ReturnDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyReturn(string code, CancellationToken ct) => OkData(await Sender.Send(new GetMyReturnQuery(code), ct));

    public record ReturnActionBody(string? Reason);

    /// <summary><c>cancel</c> · <c>accept-offer</c> · <c>dispute</c> (needs a reason).</summary>
    // Not "{action}": that name is reserved by MVC routing (sổ lỗi L016)
    [HttpPost("returns/{code}/{operation}")]
    [ProducesResponseType<ApiResponse<ReturnDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReturnAction(string code, string operation, [FromBody] ReturnActionBody? body, CancellationToken ct)
    {
        var parsed = operation switch
        {
            "cancel" => BuyerReturnAction.Cancel,
            "accept-offer" => BuyerReturnAction.AcceptOffer,
            "dispute" => BuyerReturnAction.Dispute,
            _ => throw new NotFoundException("Không tìm thấy thao tác."),
        };
        var result = await Sender.Send(new BuyerReturnActionCommand(code, parsed, body?.Reason), ct);
        return OkData(result, parsed switch
        {
            BuyerReturnAction.Cancel => "Đã huỷ yêu cầu trả hàng.",
            BuyerReturnAction.AcceptOffer => "Đã đồng ý mức hoàn, tiền đang được hoàn.",
            _ => "Đã gửi khiếu nại lên sàn.",
        });
    }
}

[Route("api/seller/shops/{shopId:guid}")]
[OwnerGuarded("Đánh giá và yêu cầu trả hàng của shop mà người gọi là nhân viên; shop khác trả 404.")]
public sealed class SellerAftercareController : ApiControllerBase
{
    [HttpGet("reviews")]
    [ProducesResponseType<ApiResponse<PagedResult<ShopReviewDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reviews(Guid shopId, [FromQuery] int? rating = null, [FromQuery] bool? replied = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        OkData(await Sender.Send(new ShopReviewsQuery(shopId, rating, replied, page, pageSize), ct));

    public record ReplyBody(string Text);

    [HttpPost("reviews/{reviewId:guid}/reply")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reply(Guid shopId, Guid reviewId, [FromBody] ReplyBody body, CancellationToken ct)
    {
        await Sender.Send(new ReplyReviewCommand(shopId, reviewId, body.Text ?? string.Empty), ct);
        return OkData<object?>(null, "Đã trả lời đánh giá.");
    }

    [HttpGet("returns")]
    [ProducesResponseType<ApiResponse<PagedResult<ReturnDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Returns(Guid shopId, [FromQuery] ReturnStatus? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => OkData(await Sender.Send(new ShopReturnsQuery(shopId, status, page, pageSize), ct));

    public record ReturnActionBody(ShopReturnAction Action, string? Note, long? Amount, bool Restock = true, IReadOnlyList<Guid>? EvidenceAssetIds = null);

    [HttpPost("returns/{returnId:guid}/actions")]
    [ProducesResponseType<ApiResponse<ReturnDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReturnAction(Guid shopId, Guid returnId, [FromBody] ReturnActionBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new ShopReturnActionCommand(shopId, returnId, body.Action, body.Note, body.Amount, body.Restock, body.EvidenceAssetIds), ct),
            "Đã cập nhật yêu cầu trả hàng.");
}

[Route("api/admin")]
public sealed class AftercareAdminController : ApiControllerBase
{
    [HttpGet("disputes")]
    [RequirePermission(Permissions.DisputeResolve)]
    [ProducesResponseType<ApiResponse<PagedResult<ReturnDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Disputes([FromQuery] bool open = true, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        OkData(await Sender.Send(new DisputesQuery(open, page, pageSize), ct));

    public record DecideBody(DisputeDecision Decision, string Reason, long? RefundAmount, bool RequireReturn);

    [HttpPost("disputes/{returnId:guid}/decide")]
    [RequirePermission(Permissions.DisputeResolve)]
    [ProducesResponseType<ApiResponse<ReturnDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Decide(Guid returnId, [FromBody] DecideBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new DecideDisputeCommand(returnId, body.Decision, body.Reason ?? string.Empty, body.RefundAmount, body.RequireReturn), ct),
            "Đã phân xử khiếu nại.");

    [HttpGet("review-reports")]
    [RequirePermission(Permissions.ReviewModerate)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewReportDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReviewReports([FromQuery] ReviewReportStatus status = ReviewReportStatus.Pending, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) => OkData(await Sender.Send(new ReviewReportsQuery(status, page, pageSize), ct));

    public record ResolveBody(bool Hide, string? Reason);

    [HttpPost("review-reports/{reportId:guid}/resolve")]
    [RequirePermission(Permissions.ReviewModerate)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Resolve(Guid reportId, [FromBody] ResolveBody body, CancellationToken ct)
    {
        await Sender.Send(new ResolveReviewReportCommand(reportId, body.Hide, body.Reason), ct);
        return OkData<object?>(null, body.Hide ? "Đã ẩn đánh giá." : "Đã bỏ qua báo cáo.");
    }
}
