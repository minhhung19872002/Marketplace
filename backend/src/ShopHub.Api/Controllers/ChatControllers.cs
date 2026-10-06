using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShopHub.Api.Common;
using ShopHub.Api.Hosting;
using ShopHub.Api.Security;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Chat;
using ShopHub.Application.Security;
using ShopHub.Domain.Engage;

namespace ShopHub.Api.Controllers;

public record SendMessageBody(MessageType Type, string? Text, Guid? ProductId, string? OrderCode, Guid? VoucherId, Guid? ImageAssetId);

[Route("api/chat")]
[OwnerGuarded("Hội thoại của chính người mua (buyer_id lọc trong câu SQL); hội thoại của người khác trả 404.")]
public sealed class ChatController : ApiControllerBase
{
    public record StartBody(Guid ShopId);

    /// <summary>"Chat ngay": the conversation with the shop (created on first use).</summary>
    [HttpPost("conversations")]
    [ProducesResponseType<ApiResponse<ConversationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Start([FromBody] StartBody body, CancellationToken ct) => OkData(await Sender.Send(new StartConversationCommand(body.ShopId), ct));

    [HttpGet("conversations")]
    [ProducesResponseType<ApiResponse<PagedResult<ConversationDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine([FromQuery] string? q = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default) =>
        OkData(await Sender.Send(new MyConversationsQuery(q, page, pageSize), ct));

    [HttpGet("unread")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Unread(CancellationToken ct) => OkData(await Sender.Send(new ChatUnreadQuery(), ct));

    [HttpGet("conversations/{conversationId:guid}/messages")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MessageDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Messages(Guid conversationId, [FromQuery] DateTimeOffset? before = null, [FromQuery] int limit = 30,
        CancellationToken ct = default) =>
        OkData(await Sender.Send(new ConversationMessagesQuery(conversationId, null, before, limit), ct));

    [HttpPost("conversations/{conversationId:guid}/messages")]
    [EnableRateLimiting(ApiServiceExtensions.ChatRateLimit)]
    [ProducesResponseType<ApiResponse<MessageDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Send(Guid conversationId, [FromBody] SendMessageBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new SendMessageCommand(conversationId, null, body.Type, body.Text, body.ProductId, body.OrderCode, body.VoucherId,
            body.ImageAssetId), ct));

    [HttpPost("conversations/{conversationId:guid}/read")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Read(Guid conversationId, CancellationToken ct)
    {
        await Sender.Send(new MarkConversationReadCommand(conversationId, null), ct);
        return OkData<object?>(null);
    }

    public record BlockBody(bool Blocked);

    [HttpPost("conversations/{conversationId:guid}/block")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Block(Guid conversationId, [FromBody] BlockBody body, CancellationToken ct)
    {
        await Sender.Send(new BlockShopCommand(conversationId, body.Blocked), ct);
        return OkData<object?>(null, body.Blocked ? "Đã chặn shop." : "Đã bỏ chặn shop.");
    }

    public record ReportBody(string Reason);

    [HttpPost("conversations/{conversationId:guid}/report")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Report(Guid conversationId, [FromBody] ReportBody body, CancellationToken ct)
    {
        await Sender.Send(new ReportShopCommand(conversationId, body.Reason), ct);
        return OkData<object?>(null, "Đã gửi báo cáo, sàn sẽ xem xét.");
    }
}

[Route("api/shops/{shopId:guid}/chat-stats")]
public sealed class ShopChatStatsController : ApiControllerBase
{
    /// <summary>Response rate / time and last activity of the shop's chat (real numbers for the shop block).</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<ShopChatStatsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new ShopChatStatsQuery(shopId), ct));
}

[Route("api/seller/shops/{shopId:guid}/chat")]
[OwnerGuarded("Hộp chat của shop mà người gọi là nhân viên có quyền CHAT.MANAGE; shop khác trả 404.")]
public sealed class SellerChatController : ApiControllerBase
{
    [HttpGet("conversations")]
    [ProducesResponseType<ApiResponse<PagedResult<ConversationDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Inbox(Guid shopId, [FromQuery] ShopInboxFilter filter = ShopInboxFilter.All, [FromQuery] string? q = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default) =>
        OkData(await Sender.Send(new ShopConversationsQuery(shopId, filter, q, page, pageSize), ct));

    [HttpGet("conversations/{conversationId:guid}/messages")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MessageDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Messages(Guid shopId, Guid conversationId, [FromQuery] DateTimeOffset? before = null, [FromQuery] int limit = 30,
        CancellationToken ct = default) =>
        OkData(await Sender.Send(new ConversationMessagesQuery(conversationId, shopId, before, limit), ct));

    [HttpPost("conversations/{conversationId:guid}/messages")]
    [EnableRateLimiting(ApiServiceExtensions.ChatRateLimit)]
    [ProducesResponseType<ApiResponse<MessageDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Send(Guid shopId, Guid conversationId, [FromBody] SendMessageBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new SendMessageCommand(conversationId, shopId, body.Type, body.Text, body.ProductId, body.OrderCode, body.VoucherId,
            body.ImageAssetId), ct));

    [HttpPost("conversations/{conversationId:guid}/read")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Read(Guid shopId, Guid conversationId, CancellationToken ct)
    {
        await Sender.Send(new MarkConversationReadCommand(conversationId, shopId), ct);
        return OkData<object?>(null);
    }

    public record AssignBody(Guid? StaffUserId);

    [HttpPost("conversations/{conversationId:guid}/assign")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Assign(Guid shopId, Guid conversationId, [FromBody] AssignBody body, CancellationToken ct)
    {
        await Sender.Send(new AssignConversationCommand(shopId, conversationId, body.StaffUserId), ct);
        return OkData<object?>(null, "Đã giao hội thoại.");
    }

    [HttpGet("staff")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ChatStaffDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Staff(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new ChatStaffQuery(shopId), ct));

    [HttpGet("quick-replies")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<QuickReplyDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> QuickReplies(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new QuickRepliesQuery(shopId), ct));

    public record QuickReplyBody(Guid? Id, string Shortcut, string Content);

    [HttpPost("quick-replies")]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveQuickReply(Guid shopId, [FromBody] QuickReplyBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new SaveQuickReplyCommand(shopId, body.Id, body.Shortcut, body.Content), ct), "Đã lưu tin trả lời nhanh.");

    [HttpDelete("quick-replies/{replyId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteQuickReply(Guid shopId, Guid replyId, CancellationToken ct)
    {
        await Sender.Send(new DeleteQuickReplyCommand(shopId, replyId), ct);
        return OkData<object?>(null, "Đã xoá.");
    }

    [HttpGet("settings")]
    [ProducesResponseType<ApiResponse<ChatSettingsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Settings(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new ChatSettingsQuery(shopId), ct));

    public record SettingsBody(bool AutoReplyEnabled, string AutoReplyText, TimeOnly OpenFrom, TimeOnly OpenTo);

    [HttpPut("settings")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveSettings(Guid shopId, [FromBody] SettingsBody body, CancellationToken ct)
    {
        await Sender.Send(new SaveChatSettingsCommand(shopId, body.AutoReplyEnabled, body.AutoReplyText, body.OpenFrom, body.OpenTo), ct);
        return OkData<object?>(null, "Đã lưu cài đặt chat.");
    }
}

[Route("api/notifications")]
[OwnerGuarded("Cài đặt thông báo và thiết bị của chính người gọi (user id từ token).")]
public sealed class NotificationSettingsController : ApiControllerBase
{
    [HttpGet("prefs")]
    [ProducesResponseType<ApiResponse<NotificationPrefsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Prefs(CancellationToken ct) => OkData(await Sender.Send(new NotificationPrefsQuery(), ct));

    public record PrefsBody(IReadOnlyList<PrefInput> Prefs);

    [HttpPut("prefs")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SavePrefs([FromBody] PrefsBody body, CancellationToken ct)
    {
        await Sender.Send(new SaveNotificationPrefsCommand(body.Prefs ?? []), ct);
        return OkData<object?>(null, "Đã lưu cài đặt thông báo.");
    }

    public record DeviceBody(string Platform, string Token);

    /// <summary>FCM token of the app on this device (push channel).</summary>
    [HttpPost("devices")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Device([FromBody] DeviceBody body, CancellationToken ct)
    {
        await Sender.Send(new RegisterDeviceCommand(body.Platform, body.Token), ct);
        return OkData<object?>(null);
    }
}

[Route("api/admin/marketing/broadcasts")]
public sealed class BroadcastAdminController : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.MarketingManage)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<BroadcastDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => OkData(await Sender.Send(new BroadcastsQuery(), ct));

    [HttpPost]
    [RequirePermission(Permissions.MarketingManage)]
    [ProducesResponseType<ApiResponse<BroadcastDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Send([FromBody] SendBroadcastCommand body, CancellationToken ct) => OkData(await Sender.Send(body, ct), "Đã gửi thông báo.");
}
