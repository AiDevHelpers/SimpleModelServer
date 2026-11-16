using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Providers.Ollama;

namespace SimpleModelServer.Providers.LMStudio;

/// <summary>
///     Stubs <see cref="IOllamaProcessManager" /> for the LM Studio backend — LM Studio has no
///     process-lifecycle manager of its own; the user is expected to already have it running with
///     its local server enabled ("health-check only, no process control"). Reusing this interface
///     (rather than introducing a parallel health-probe abstraction) means
///     <see cref="SimpleModelServer.Server.RouterHttpServer" /> needs zero code changes to work
///     against either backend: every method here either delegates to
///     <see cref="ILMStudioProvider.TestConnectionAsync" /> or is an intentional no-op.
/// </summary>
public sealed class LMStudioProcessManagerAdapter(ILMStudioProvider provider, IProviderConfiguration providerConfig) : IOllamaProcessManager
{
	public int ServerPort => providerConfig.Port;

	/// <summary>No owned process — LM Studio's process isn't started or tracked by this adapter.</summary>
	public int? OllamaProcessId => null;

	public void RegisterExistingOllamaProcess()
	{
		// No-op — nothing to register; LM Studio's process isn't ours to track.
	}

	public void KillStuckOllama()
	{
		// No-op — no process control for LM Studio (health-check only, per product decision).
	}

	public Task EnsureModelAsync(string model)
	{
		// No-op — LM Studio manages its own loaded models; SimpleModelServer never pulls/creates
		// models for it the way it does for Ollama.
		return Task.CompletedTask;
	}

	public async Task<bool> StartServerAsync()
	{
		// "Starting" LM Studio means confirming it's already reachable — never actually launched.
		return await provider.TestConnectionAsync();
	}

	public async Task<bool> WaitForServerAsync()
	{
		return await provider.TestConnectionAsync();
	}

	public async Task<bool> IsOllamaReadyAsync()
	{
		return await provider.TestConnectionAsync();
	}

	public async Task<bool> IsModelResponsiveAsync(string model)
	{
		return await provider.TestConnectionAsync();
	}

	public async Task<bool> IsModelResponsiveAsync(string model, TimeSpan timeout)
	{
		using CancellationTokenSource cts = new(timeout);
		return await provider.TestConnectionAsync(cts.Token);
	}

	public Task<bool> IsModelLoadedInVramAsync(string model)
	{
		// No VRAM/idle concept exposed by LM Studio's API — always report "loaded" so callers
		// (ModelHealthMonitor's health-snapshot probe) never see a spurious idle/unloaded state.
		return Task.FromResult(true);
	}

	public Task UnloadModelAsync(string model)
	{
		// No-op — no process control for LM Studio.
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		// No-op — no owned resources.
	}
}
