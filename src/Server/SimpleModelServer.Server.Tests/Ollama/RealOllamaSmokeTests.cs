using SimpleModelServer.Common.Infrastructure;
using SimpleModelServer.Contracts.Models;
using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Hardware;
using SimpleModelServer.Providers.Common.Utilities;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;


namespace SimpleModelServer.Server.Tests;

using SimpleModelServer.Providers.Ollama;

/// <summary>
///     A true end-to-end smoke test: talks to a REAL local Ollama install with REAL model
///     weights and asserts a real chat request gets a real (non-empty) answer back. Every other
///     test in this project — including PostEndpointTests, which does exercise a real
///     RouterHttpServer over a real socket — is backed by FakeOllamaServer, so none of them can
///     catch "Ollama itself is broken", "the configured models aren't actually pulled", or "the
///     real network path from SimpleModelPairGenerationService to Ollama doesn't work". This test is
///     the one thing in the suite that would.
///
///     This is an integration/smoke test, not a unit test, and behaves accordingly:
///     <list type="bullet">
///         <item>It self-skips (logs why and returns, rather than failing) when `ollama` isn't on
///         PATH, Ollama won't start, or the required models (draft/main/embed — the same
///         defaults SimpleModelPairGenerationService falls back to) aren't already pulled. It
///         deliberately does NOT auto-pull missing models — that's a multi-GB download with no
///         place in a routine test run.</item>
///         <item>It talks to whatever Ollama is already running/installed on the machine running
///         the test, on the real default port (11434) — not an isolated fixture — since the whole
///         point is to prove the real thing works.</item>
///         <item>It allows a generous timeout (model cold-load can take a while on the very first
///         request) and only asserts the response is non-empty, never asserting exact content,
///         since real model output is nondeterministic.</item>
///     </list>
/// </summary>
public sealed class RealOllamaSmokeTests(ITestOutputHelper output)
{
	private const string DraftModel = "qwen2.5-coder-0.5b-chat";
	private const string MainModel = "qwen2.5-coder-3b-chat";
	private const string EmbedModel = "nomic-embed-text";
	private static readonly TimeSpan ChatTimeout = TimeSpan.FromSeconds(90);

	[Fact]
	public async Task RealChatRequest_AgainstRealOllama_ReturnsNonEmptyResponse()
	{
		if (!OllamaProcessManager.CommandExists("ollama"))
		{
			output.WriteLine("SKIPPED: 'ollama' not found on PATH — this machine can't run the real-Ollama smoke test.");
			return;
		}

		AppConfig config = new()
		{
			Global = new GlobalConfig
			{
				DraftModel = DraftModel,
				MainModel = MainModel,
				EmbedModel = EmbedModel
			}
		};

		using HttpClient http = new(new HttpClientHandler { UseProxy = false }) { Timeout = ChatTimeout };
		using OllamaProcessManager process = new(config, NullLogger.Instance, http);

		if (!await process.IsOllamaReadyAsync())
		{
			output.WriteLine("Ollama not responding on the default port — attempting to start it...");
			if (!await process.StartServerAsync())
			{
				output.WriteLine("SKIPPED: could not start a real Ollama server on this machine.");
				return;
			}
		}

		string? missingModel = await FindFirstMissingModelAsync(process);
		if (missingModel is not null)
		{
			output.WriteLine(
				$"SKIPPED: required model '{missingModel}' isn't pulled locally. " +
				$"Run 'ollama pull {missingModel}' (or the SimpleModelServer.Server startup path, which pulls " +
				"draft/main/embed automatically) before running this smoke test.");
			return;
		}

		ResolvedHardwareSettings hardware = new() { GpuLayers = null, CpuThreads = null, Gpu = GpuInfo.None };
		var ollamaHelpers = new OllamaHelpers();
		var consoleHelper = new ConsoleHelper();
		var generationBackend = new OllamaGenerationBackend(process, http, ollamaHelpers);
		SimpleModelPairGenerationService generation = new(
			config,
			NullLogger.Instance,
			generationBackend,
			() => hardware,
			_ => null,
			_ => Task.CompletedTask,
			ollamaHelpers,
			consoleHelper);

		using CancellationTokenSource cts = new(ChatTimeout);
		string response = await generation.GetDraftPairResponseAsync(
			[new ChatMessage("user", "Reply with a short greeting. Keep it to one sentence.")],
			"code",
			cts.Token);

		output.WriteLine($"Real Ollama response: {response}");
		Assert.False(string.IsNullOrWhiteSpace(response), "Expected a real, non-empty response from Ollama.");
	}

	/// <summary>
	///     Checks `ollama list` for draft/main/embed without ever pulling anything itself —
	///     that's the "don't accidentally trigger a multi-GB download from a test run" guard.
	///
	///     This deliberately mirrors OllamaProcessManager.EnsureModelAsync's own
	///     "already installed?" check (a plain `ollama list` substring match) rather than
	///     probing responsiveness via /api/generate — an embedding model like nomic-embed-text
	///     doesn't necessarily answer /api/generate the same way a chat model does, and this
	///     check needs to work for both without caring about that difference. (Confirmed the hard
	///     way: an earlier version of this test used IsModelResponsiveAsync, which sends
	///     /api/generate, and it reported nomic-embed-text as "missing" even though `ollama list`
	///     showed it present.)
	/// </summary>
	private static async Task<string?> FindFirstMissingModelAsync(OllamaProcessManager process)
	{
		string list = await RunOllamaListAsync();
		foreach (string model in new[] { DraftModel, MainModel, EmbedModel })
		{
			if (!list.Contains(model, StringComparison.OrdinalIgnoreCase))
			{
				return model;
			}
		}

		return null;
	}

	private static async Task<string> RunOllamaListAsync()
	{
		System.Diagnostics.ProcessStartInfo startInfo = new()
		{
			FileName = "ollama",
			Arguments = "list",
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};

		using System.Diagnostics.Process listProcess = System.Diagnostics.Process.Start(startInfo)
			?? throw new InvalidOperationException("Could not start 'ollama list'.");
		string output = await listProcess.StandardOutput.ReadToEndAsync();
		await listProcess.WaitForExitAsync();
		return output;
	}
}


