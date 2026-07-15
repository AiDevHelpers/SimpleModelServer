using Microsoft.AspNetCore.SignalR;

namespace SimpleModelServer.Server;

/// <summary>
///     SignalR hub cooperative clients use to be notified of a model reload/swap and to ack that
///     they've paused. Purely a courtesy channel for clients that support it — the actual
///     enforcement (any caller, SignalR-aware or not) is <see cref="ModelReloadCoordinator.WaitIfPausedAsync" />
///     gating the HTTP request pipeline. Mapped at /hubs/model-status by <see cref="RouterHttpServer.StartAsync" />.
/// </summary>
internal sealed class ModelStatusHub(ModelReloadCoordinator coordinator) : Hub
{
    /// <summary>Registers this connection for pause/resume notifications about <paramref name="modelName" />.</summary>
    public async Task Subscribe(string modelName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, modelName);
        coordinator.Subscribe(modelName, Context.ConnectionId);
    }

    /// <summary>Client's acknowledgement that it has paused work for <paramref name="modelName" />.</summary>
    public void Ack(string modelName)
        => coordinator.RecordAck(modelName, Context.ConnectionId);

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        coordinator.Unsubscribe(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
