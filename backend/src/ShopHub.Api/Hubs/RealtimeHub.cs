using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Chat;

namespace ShopHub.Api.Hubs;

/// <summary>
/// One SignalR hub for everything live (spec 1): chat messages, read receipts, typing, notifications. Each connection
/// joins the group of its user, so every tab and device of the user gets the event; the Redis backplane carries the
/// events between API instances. Clients only send "Typing" — writing goes through the REST API (validation, rate
/// limit, storage).
/// </summary>
[Authorize]
public sealed class RealtimeHub(ChatTyping typing) : Hub
{
    public static string UserGroup(Guid userId) => $"user:{userId:N}";

    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        await base.OnConnectedAsync();
    }

    /// <summary>"Đang gõ…": relayed to the other side of the conversation if the caller takes part in it.</summary>
    public async Task Typing(Guid conversationId)
    {
        if (Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var userId))
            await typing.RelayAsync(userId, conversationId, Context.ConnectionAborted);
    }
}

public sealed class SignalRRealtime(IHubContext<RealtimeHub> hub, ILogger<SignalRRealtime> logger) : IRealtime
{
    public async Task ToUsersAsync(IReadOnlyCollection<Guid> userIds, string eventName, object payload, CancellationToken ct)
    {
        if (userIds.Count == 0) return;
        try
        {
            await hub.Clients.Groups(userIds.Select(RealtimeHub.UserGroup).ToList()).SendAsync(eventName, payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Live delivery is best effort: the data is stored and the clients re-read it
            logger.LogWarning(ex, "Realtime {Event} could not be sent", eventName);
        }
    }
}
