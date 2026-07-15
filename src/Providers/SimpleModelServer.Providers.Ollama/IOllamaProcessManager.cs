namespace SimpleModelServer.Providers.Ollama;

/// <summary>
///     Contract for owning the lifecycle of a local Ollama server process — starting it, killing
///     stuck instances, waiting for readiness, probing health, and ensuring required models are
///     pulled/created. Consumers (<see cref="SimpleModelServer.Server.RouterHttpServer" />,
///     <see cref="SimpleModelServer.Server.ModelHealthMonitor" />,
///     <see cref="SimpleModelServer.Server.SimpleModelPairGenerationService" />) depend on this
///     interface rather than the concrete <see cref="OllamaProcessManager" /> so they aren't
///     coupled to one process-management implementation. <see cref="OllamaProcessManager" /> is
///     currently the only implementation; <c>SimpleModelServer.Providers.LMStudio</c> has no
///     equivalent yet — see LM Studio provider wiring as a separate follow-up.
/// </summary>
public interface IOllamaProcessManager : IDisposable
{
    int ServerPort { get; }
    int? OllamaProcessId { get; }

    void RegisterExistingOllamaProcess();
    void KillStuckOllama();
    Task EnsureModelAsync(string model);
    Task<bool> StartServerAsync();
    Task<bool> WaitForServerAsync();
    Task<bool> IsOllamaReadyAsync();
    Task<bool> IsModelResponsiveAsync(string model);
    Task<bool> IsModelResponsiveAsync(string model, TimeSpan timeout);
    Task<bool> IsModelLoadedInVramAsync(string model);
    Task UnloadModelAsync(string model);
}
