using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SimpleModelServer.Contracts.Dtos.Providers;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Providers.Ollama;
using Xunit;

namespace SimpleModelServer.Providers.LMStudio.Tests;

/// <summary>
///     Contract tests for <see cref="LMStudioProcessManagerAdapter" /> — confirms it satisfies
///     <see cref="IOllamaProcessManager" /> the way the plan intends: reachability-probe delegation
///     for the "is it up" queries, and true no-ops for every process-control operation (LM Studio
///     gets health-check-only treatment, never start/kill/pull). Mirrors the thin
///     <see cref="OllamaProviderTests" /> pattern used for the Ollama side.
/// </summary>
public class LMStudioProcessManagerAdapterTests
{
	private static (LMStudioProcessManagerAdapter Adapter, RespondingHandler Handler) CreateAdapter(HttpStatusCode statusCode)
	{
		var config = new ProviderConfiguration { Host = "localhost", Port = 1234 };
		var handler = new RespondingHandler(statusCode);
		var httpClient = new HttpClient(handler);
		var provider = new LMStudioProvider(config, httpClient);
		var adapter = new LMStudioProcessManagerAdapter(provider, config);
		return (adapter, handler);
	}

	[Fact]
	public void IOllamaProcessManager_ImplementedByAdapter()
	{
		Assert.Contains(typeof(IOllamaProcessManager), typeof(LMStudioProcessManagerAdapter).GetInterfaces());
	}

	[Fact]
	public void ServerPort_ReturnsConfiguredProviderPort()
	{
		(LMStudioProcessManagerAdapter adapter, _) = CreateAdapter(HttpStatusCode.OK);
		Assert.Equal(1234, adapter.ServerPort);
	}

	[Fact]
	public void OllamaProcessId_AlwaysNull()
	{
		(LMStudioProcessManagerAdapter adapter, _) = CreateAdapter(HttpStatusCode.OK);
		Assert.Null(adapter.OllamaProcessId);
	}

	[Fact]
	public async Task IsOllamaReadyAsync_ReflectsBackendReachability()
	{
		(LMStudioProcessManagerAdapter reachable, _) = CreateAdapter(HttpStatusCode.OK);
		Assert.True(await reachable.IsOllamaReadyAsync());

		(LMStudioProcessManagerAdapter unreachable, _) = CreateAdapter(HttpStatusCode.ServiceUnavailable);
		Assert.False(await unreachable.IsOllamaReadyAsync());
	}

	[Fact]
	public async Task StartServerAsync_AndWaitForServerAsync_ReflectBackendReachability()
	{
		(LMStudioProcessManagerAdapter reachable, _) = CreateAdapter(HttpStatusCode.OK);
		Assert.True(await reachable.StartServerAsync());
		Assert.True(await reachable.WaitForServerAsync());

		(LMStudioProcessManagerAdapter unreachable, _) = CreateAdapter(HttpStatusCode.ServiceUnavailable);
		Assert.False(await unreachable.StartServerAsync());
		Assert.False(await unreachable.WaitForServerAsync());
	}

	[Fact]
	public async Task IsModelResponsiveAsync_BothOverloads_ReflectBackendReachability()
	{
		(LMStudioProcessManagerAdapter adapter, _) = CreateAdapter(HttpStatusCode.OK);
		Assert.True(await adapter.IsModelResponsiveAsync("any-model"));
		Assert.True(await adapter.IsModelResponsiveAsync("any-model", TimeSpan.FromSeconds(5)));
	}

	[Fact]
	public async Task IsModelLoadedInVramAsync_AlwaysTrue_RegardlessOfBackendReachability()
	{
		// No VRAM/idle concept exposed by LM Studio's API — always "loaded" so ModelHealthMonitor's
		// health snapshot never reports a spurious idle/unloaded state.
		(LMStudioProcessManagerAdapter unreachable, _) = CreateAdapter(HttpStatusCode.ServiceUnavailable);
		Assert.True(await unreachable.IsModelLoadedInVramAsync("any-model"));
	}

	[Fact]
	public async Task ProcessControlMethods_AreNoOps()
	{
		(LMStudioProcessManagerAdapter adapter, RespondingHandler handler) = CreateAdapter(HttpStatusCode.OK);

		adapter.RegisterExistingOllamaProcess();
		adapter.KillStuckOllama();
		await adapter.EnsureModelAsync("any-model");
		await adapter.UnloadModelAsync("any-model");
		adapter.Dispose();

		// None of the no-op calls above should have made an HTTP request to the backend.
		Assert.Equal(0, handler.RequestCount);
	}

	private sealed class RespondingHandler(HttpStatusCode statusCode) : HttpMessageHandler
	{
		public int RequestCount { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			RequestCount++;
			return Task.FromResult(new HttpResponseMessage(statusCode));
		}
	}
}
