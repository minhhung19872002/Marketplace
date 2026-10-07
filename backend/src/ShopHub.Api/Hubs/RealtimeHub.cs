using System.Collections.Concurrent;
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
public sealed class RealtimeHub(ChatTyping typing, HubConnections connections) : Hub
{
    public static string UserGroup(Guid userId) => $"user:{userId:N}";

    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
            if (Guid.TryParse(Context.User?.FindFirst("sid")?.Value, out var sessionId)) connections.Add(Context, userId, sessionId);
        }
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>"Đang gõ…": relayed to the other side of the conversation if the caller takes part in it.</summary>
    public async Task Typing(Guid conversationId)
    {
        if (Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var userId))
            await typing.RelayAsync(userId, conversationId, Context.ConnectionAborted);
    }
}

/// <summary>
/// The hub connections open on this instance with the session behind each. The token is only checked at the handshake,
/// so when a session or a user is invalidated (lock, logout, password change — on any instance, through the
/// "iam.sessions.changed" outbox message) the affected connections are re-checked and the ones no longer allowed are
/// closed at once (L128).
/// </summary>
public sealed class HubConnections(IServiceProvider services, ILogger<HubConnections> logger) : ISessionEndListener
{
    private readonly ConcurrentDictionary<string, (Guid UserId, Guid SessionId, HubCallerContext Context)> _open = new();

    public void Add(HubCallerContext context, Guid userId, Guid sessionId) => _open[context.ConnectionId] = (userId, sessionId, context);

    public void Remove(string connectionId) => _open.TryRemove(connectionId, out _);

    public void SessionsChanged(Guid? userId, Guid? sessionId)
    {
        var affected = _open.Values.Where(c => c.UserId == userId || c.SessionId == sessionId).ToList();
        if (affected.Count == 0) return;
        _ = Task.Run(async () =>
        {
            // Resolved here: the validator itself depends on this listener
            var validator = services.GetRequiredService<ISessionValidator>();
            foreach (var c in affected)
            {
                try
                {
                    if (!await validator.IsValidAsync(c.UserId, c.SessionId)) c.Context.Abort();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not re-check realtime connection {ConnectionId}", c.Context.ConnectionId);
                }
            }
        });
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
