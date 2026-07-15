# TokenSaver SignalR Integration with SimpleModelServer

This guide explains how to implement real-time model reload coordination in TokenSaver.MCP when using SimpleModelServer as its backend.

## Why SignalR Integration?

When TokenSaver connects to a shared SimpleModelServer instance, the server may reload/swap models at any time. Without SignalR integration:

- TokenSaver requests may fail during model reloads
- TokenSaver can't know when the model is unavailable
- HTTP timeouts occur instead of graceful pausing

With SignalR integration:

- TokenSaver receives real-time `PauseForReload` notifications
- TokenSaver can pause token accounting and context management
- Server performs reload while TokenSaver is paused
- TokenSaver receives `ResumeReady` and resumes normal operation
- No requests fail; token accounting stays accurate

## Architecture

```
SimpleModelServer.Loader         TokenSaver.MCP
     Router                      (Local instance)
       |                               |
       |--- HTTP requests ----------->|
       |<--- Responses -------|
       |                      |
       | (when reload needed) |
       |--- SignalR "Pause" --|
       |                      | (pauses work)
       |--- Perform Reload---|
       |<-- SignalR "Resume"-|
       |                      | (resumes work)
```

## Implementation Guide

### 1. Create a SignalR Client Service

Create `TokenSaverSignalRClient.cs` in TokenSaver.MCP:

```csharp
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace TokenSaver.MCP.Infrastructure
{
    /// <summary>
    /// Connects to SimpleModelServer's ModelStatusHub via SignalR to receive
    /// real-time model reload/pause notifications.
    /// </summary>
    public class TokenSaverSignalRClient : IAsyncDisposable
    {
        private readonly string _hubUrl;
        private readonly string _modelName;
        private readonly ILogger _logger;
        private HubConnection? _connection;

        /// <summary>
        /// Fired when SimpleModelServer notifies that a model is about to reload.
        /// TokenSaver should pause work and call Ack() after pausing.
        /// </summary>
        public event Func<string, Task>? OnPauseForReload;

        /// <summary>
        /// Fired when SimpleModelServer indicates the model is ready for requests again.
        /// TokenSaver should resume normal work.
        /// </summary>
        public event Func<string, Task>? OnResumeReady;

        public TokenSaverSignalRClient(
            string routerUrl,
            string modelName,
            ILogger logger)
        {
            _hubUrl = routerUrl.TrimEnd('/') + "/hubs/model-status";
            _modelName = modelName;
            _logger = logger;
        }

        /// <summary>
        /// Connects to the SimpleModelServer SignalR hub and subscribes to model reload events.
        /// </summary>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_connection?.State == HubConnectionState.Connected)
            {
                return; // Already connected
            }

            _connection = new HubConnectionBuilder()
                .WithUrl(_hubUrl)
                .WithAutomaticReconnect(
                    retryDelays: new[] {
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(10),
                    })
                .Build();

            // Register server-to-client method handlers
            _connection.On<string>("PauseForReload", OnPauseForReloadAsync);
            _connection.On<string>("ResumeReady", OnResumeReadyAsync);

            _connection.Reconnected += async (connectionId) =>
            {
                _logger.LogInformation("SignalR reconnected ({ConnectionId}), re-subscribing to model {Model}",
                    connectionId, _modelName);
                await SubscribeAsync(cancellationToken);
            };

            _connection.Closed += async (error) =>
            {
                _logger.LogWarning(error, "SignalR connection closed. Will reconnect automatically.");
            };

            try
            {
                await _connection.StartAsync(cancellationToken);
                _logger.LogInformation("SignalR connected to {HubUrl}", _hubUrl);

                // Subscribe to model reload events
                await SubscribeAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to SignalR hub at {HubUrl}", _hubUrl);
                throw;
            }
        }

        /// <summary>
        /// Subscribes to reload notifications for the specified model.
        /// </summary>
        private async Task SubscribeAsync(CancellationToken cancellationToken = default)
        {
            if (_connection?.State != HubConnectionState.Connected)
            {
                _logger.LogWarning("Cannot subscribe: SignalR not connected");
                return;
            }

            try
            {
                await _connection.InvokeAsync("Subscribe", _modelName, cancellationToken);
                _logger.LogInformation("Subscribed to model {Model} reload events", _modelName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to subscribe to model {Model}", _modelName);
                throw;
            }
        }

        /// <summary>
        /// Acknowledges that TokenSaver has paused work in response to PauseForReload.
        /// Must be called from within the OnPauseForReload event handler.
        /// </summary>
        public async Task AckPauseAsync(CancellationToken cancellationToken = default)
        {
            if (_connection?.State != HubConnectionState.Connected)
            {
                _logger.LogWarning("Cannot ack pause: SignalR not connected");
                return;
            }

            try
            {
                await _connection.InvokeAsync("Ack", _modelName, cancellationToken);
                _logger.LogDebug("Acknowledged pause for model {Model}", _modelName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ack pause for model {Model}", _modelName);
                // Don't throw - the server will proceed with reload even if ack fails
            }
        }

        /// <summary>
        /// Disconnects from the SignalR hub.
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_connection != null)
            {
                try
                {
                    await _connection.StopAsync();
                    await _connection.DisposeAsync();
                    _logger.LogInformation("Disconnected from SignalR hub");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disconnecting from SignalR hub");
                }
            }
        }

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            await DisconnectAsync();
        }

        private async Task OnPauseForReloadAsync(string modelName)
        {
            _logger.LogInformation("Received PauseForReload for model {Model}", modelName);

            try
            {
                // Invoke the event handler (token manager pauses work)
                if (OnPauseForReload != null)
                {
                    await OnPauseForReload.Invoke(modelName);
                }

                // Acknowledge the pause
                await AckPauseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling PauseForReload for model {Model}", modelName);
            }
        }

        private async Task OnResumeReadyAsync(string modelName)
        {
            _logger.LogInformation("Received ResumeReady for model {Model}", modelName);

            try
            {
                // Invoke the event handler (token manager resumes work)
                if (OnResumeReady != null)
                {
                    await OnResumeReady.Invoke(modelName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling ResumeReady for model {Model}", modelName);
            }
        }
    }
}
```

### 2. Integrate into TokenSaver's Token Manager

In TokenSaver's main request handler or token manager, wire up the SignalR client:

```csharp
public class TokenManager
{
    private readonly TokenSaverSignalRClient _signalRClient;
    private bool _isPaused = false;
    private readonly object _pauseLock = new object();

    public TokenManager(ILogger logger, TokenSaverConfig config)
    {
        // Create SignalR client for model reload coordination
        var routerUrl = config.OllamaUrl; // e.g., "http://192.168.1.50:11435"
        var modelName = config.OllamaModel; // e.g., "HelperModel"

        _signalRClient = new TokenSaverSignalRClient(routerUrl, modelName, logger);

        // Wire up pause/resume handlers
        _signalRClient.OnPauseForReload += HandlePauseForReloadAsync;
        _signalRClient.OnResumeReady += HandleResumeReadyAsync;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Connect to SimpleModelServer's SignalR hub for real-time reload notifications
            await _signalRClient.ConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Log but don't fail - work can still proceed without SignalR
            _logger.LogWarning(ex, "Failed to connect SignalR for model reload coordination. " +
                "TokenSaver will work but won't be notified of model reloads.");
        }
    }

    public async Task ProcessRequestAsync(ContextRequest request, CancellationToken cancellationToken = default)
    {
        // Wait if paused (model reload in progress)
        await WaitIfPausedAsync(cancellationToken);

        lock (_pauseLock)
        {
            if (_isPaused)
            {
                throw new OperationCanceledException("Model reload in progress");
            }
        }

        // Process request normally
        // ... token accounting, context management, etc.
    }

    private async Task WaitIfPausedAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            lock (_pauseLock)
            {
                if (!_isPaused)
                {
                    return;
                }
            }

            // Wait a bit before checking again
            await Task.Delay(100, cancellationToken);
        }
    }

    private async Task HandlePauseForReloadAsync(string modelName)
    {
        _logger.LogInformation("Pausing TokenSaver work due to model reload: {Model}", modelName);

        lock (_pauseLock)
        {
            _isPaused = true;
        }

        // Pause any in-flight work here
        // - Stop accepting new requests
        // - Wait for in-flight requests to complete gracefully
        // - Preserve token accounting state
        await Task.Delay(100); // Give in-flight work time to pause

        _logger.LogInformation("TokenSaver paused and ready for reload");
    }

    private async Task HandleResumeReadyAsync(string modelName)
    {
        _logger.LogInformation("Resuming TokenSaver work after model reload: {Model}", modelName);

        lock (_pauseLock)
        {
            _isPaused = false;
        }

        // Resume normal operation
        _logger.LogInformation("TokenSaver resumed");

        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _signalRClient.DisposeAsync();
    }
}
```

### 3. Project Dependencies

Add to TokenSaver.MCP.csproj:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.0" />
</ItemGroup>
```

## Benefits When Enabled

| Without SignalR | With SignalR |
|---|---|
| Requests fail during reload | Requests pause gracefully |
| Token accounting may be inaccurate | Token accounting is accurate |
| User sees error messages | User sees brief pause, no error |
| Unclear when service is unavailable | Real-time status notifications |
| Manual reload coordination needed | Automatic reload coordination |

## Graceful Degradation

SignalR integration is **optional** — TokenSaver works fine without it:

- If `OllamaUrl` points to a SimpleModelServer without SignalR (old version), requests still work
- SignalR connection failures don't crash TokenSaver
- If SignalR hub becomes unavailable, requests continue (but won't pause for reloads)
- Fallback behavior: requests timeout normally if model is reloading

## Testing

Mock test for pause/resume handling:

```csharp
[Test]
public async Task TokenManager_PausesOnPauseForReload()
{
    var logger = new Mock<ILogger>();
    var manager = new TokenManager(logger.Object, config);

    // Simulate pause notification
    var pauseTask = manager.HandlePauseForReloadAsync("HelperModel");
    await pauseTask;

    // Attempt to process request should block
    var processTask = manager.ProcessRequestAsync(request, CancellationToken.None);
    processTask.Should().NotBeCompleted();

    // Simulate resume
    var resumeTask = manager.HandleResumeReadyAsync("HelperModel");
    await resumeTask;

    // Request should complete now
    await Task.WhenAll(
        processTask.CompleteWithin(TimeSpan.FromSeconds(1)),
        resumeTask
    );
}
```

## Related Documentation

- **[ModelStatusHub](../Server/ModelStatusHub.cs)** — SimpleModelServer's SignalR hub implementation
- **[ModelReloadCoordinator](../Server/ModelReloadCoordinator.cs)** — Reload choreography engine
- **[TokenSaver Integration](TOKENSAVER_INTEGRATION.md)** — TokenSaver HTTP connection setup
