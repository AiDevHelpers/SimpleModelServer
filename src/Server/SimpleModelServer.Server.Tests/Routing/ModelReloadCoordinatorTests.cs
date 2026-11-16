using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Dtos.Providers;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Providers.Ollama;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Covers <see cref="ModelReloadCoordinator" />'s gate/ack mechanics directly, without a real
///     Kestrel host or SignalR connection. <see cref="ModelReloadCoordinator.Hub" /> is left null
///     throughout — that's the exact fallback path the class is designed for (see its doc comment),
///     and it's what these unit tests can exercise deterministically; the SignalR-attached
///     broadcast path is wired up by <see cref="RouterHttpServer.StartAsync" /> and exercised by
///     the real router in <c>RouterTestFixture</c>-based tests at the HTTP layer.
/// </summary>
public sealed class ModelReloadCoordinatorTests
{
    private static ModelReloadCoordinator CreateCoordinator(StubProcessManager process, out ModelHealthMonitor health)
    {
        AppConfig config = new()
        {
            Global = new GlobalConfig
            {
                // LMStudio short-circuits ModelHealthMonitor.EnsureModelResponsiveAsync's network
                // probe entirely, so the stub process manager only needs to answer
                // UnloadModelAsync/EnsureModelAsync — see ModelHealthMonitor.EnsureModelResponsiveAsync.
                Provider = new ProviderConfiguration { Type = Provider.LMStudio }
            }
        };
        health = new ModelHealthMonitor(config, NullLogger.Instance, process, new HttpClient(), _ => null);
        return new ModelReloadCoordinator(process, health, NullLogger.Instance, TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task WaitIfPausedAsync_NoReloadInProgress_CompletesImmediately()
    {
        ModelReloadCoordinator coordinator = CreateCoordinator(new StubProcessManager(), out _);

        // Must not hang — an unseen model's gate defaults to open.
        await coordinator.WaitIfPausedAsync("some-model", TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EnsureModelResponsiveAsync_ModelBusyWithGeneration_IsNotUnloaded()
    {
        StubProcessManager process = new() { Responsive = false };
        AppConfig config = new() { Global = new GlobalConfig { Provider = new ProviderConfiguration { Type = Provider.Ollama } } };
        InFlightGenerations inFlight = new();
        ModelHealthMonitor health = new(config, NullLogger.Instance, process, new HttpClient(), _ => null, inFlight);

        using IDisposable generation = inFlight.Track("draft-model");
        await health.EnsureModelResponsiveAsync("draft-model");

        Assert.Empty(process.UnloadedModels);
    }

    [Fact]
    public async Task EnsureModelResponsiveAsync_ModelIdleAndUnresponsive_IsUnloaded()
    {
        StubProcessManager process = new() { Responsive = false };
        AppConfig config = new() { Global = new GlobalConfig { Provider = new ProviderConfiguration { Type = Provider.Ollama } } };
        ModelHealthMonitor health = new(config, NullLogger.Instance, process, new HttpClient(), _ => null, new InFlightGenerations());

        await health.EnsureModelResponsiveAsync("draft-model");

        Assert.Contains("draft-model", process.UnloadedModels);
    }

    [Fact]
    public async Task BeginReloadAsync_ClosesGateDuringReloadAndReopensAfter()
    {
        StubProcessManager process = new() { UnloadDelay = TimeSpan.FromMilliseconds(150) };
        ModelReloadCoordinator coordinator = CreateCoordinator(process, out _);

        Task reload = coordinator.BeginReloadAsync("draft-model", TestContext.Current.CancellationToken);

        // Give BeginReloadAsync a moment to close the gate before we start waiting on it.
        await Task.Delay(20, TestContext.Current.CancellationToken);
        Task waiter = coordinator.WaitIfPausedAsync("draft-model", TestContext.Current.CancellationToken);

        Assert.False(waiter.IsCompleted, "the gate should still be closed while the reload is in flight");

        await reload;
        await waiter.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.True(waiter.IsCompletedSuccessfully, "the gate must reopen once the reload finishes");
    }

    [Fact]
    public async Task BeginReloadAsync_UnloadsThenReEnsuresTheModel()
    {
        StubProcessManager process = new();
        ModelReloadCoordinator coordinator = CreateCoordinator(process, out _);

        await coordinator.BeginReloadAsync("main-model", TestContext.Current.CancellationToken);

        Assert.Equal(["main-model"], process.UnloadedModels);
        Assert.Equal(["main-model"], process.EnsuredModels);
    }

    [Fact]
    public async Task BeginReloadAsync_ReopensGateEvenWhenTheSwapThrows()
    {
        StubProcessManager process = new() { ThrowOnUnload = true };
        ModelReloadCoordinator coordinator = CreateCoordinator(process, out _);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.BeginReloadAsync("main-model", TestContext.Current.CancellationToken));

        // A failed reload must not leave the model permanently paused.
        await coordinator.WaitIfPausedAsync("main-model", TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Subscribe_ThenUnsubscribe_RemovesConnectionFromEveryModel()
    {
        ModelReloadCoordinator coordinator = CreateCoordinator(new StubProcessManager(), out _);

        coordinator.Subscribe("draft-model", "conn-1");
        coordinator.Subscribe("main-model", "conn-1");

        // No exception/no-op is the observable contract here — RecordAck against a model with no
        // active reload round must be a harmless no-op both before and after unsubscribing.
        coordinator.Unsubscribe("conn-1");
        coordinator.RecordAck("draft-model", "conn-1");
    }

    private sealed class StubProcessManager : IOllamaProcessManager
    {
        public List<string> UnloadedModels { get; } = [];
        public List<string> EnsuredModels { get; } = [];
        public TimeSpan UnloadDelay { get; init; } = TimeSpan.Zero;
        public bool ThrowOnUnload { get; init; }

        public int ServerPort => 0;
        public int? OllamaProcessId => null;

        public void RegisterExistingOllamaProcess() { }
        public void KillStuckOllama() { }

        public async Task UnloadModelAsync(string model)
        {
            if (UnloadDelay > TimeSpan.Zero)
            {
                await Task.Delay(UnloadDelay);
            }

            if (ThrowOnUnload)
            {
                throw new InvalidOperationException("simulated unload failure");
            }

            UnloadedModels.Add(model);
        }

        public Task EnsureModelAsync(string model)
        {
            EnsuredModels.Add(model);
            return Task.CompletedTask;
        }

        public Task<bool> StartServerAsync() => Task.FromResult(true);
        public Task<bool> WaitForServerAsync() => Task.FromResult(true);
        public Task<bool> IsOllamaReadyAsync() => Task.FromResult(true);
        public bool Responsive { get; init; } = true;

        public Task<bool> IsModelResponsiveAsync(string model) => Task.FromResult(Responsive);
        public Task<bool> IsModelResponsiveAsync(string model, TimeSpan timeout) => Task.FromResult(true);
        public Task<bool> IsModelLoadedInVramAsync(string model) => Task.FromResult(true);

        public void Dispose() { }
    }
}
