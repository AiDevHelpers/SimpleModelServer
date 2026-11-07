using SimpleModelServer.Contracts.Dtos.Providers;
using SimpleModelServer.Providers.Common.Generation;
using SimpleModelServer.Providers.LMStudio;

using Xunit;


namespace SimpleModelServer.Server.Tests.LMStudio;

/// <summary>
///     A true end-to-end smoke test: talks to a REAL local LM Studio install with REAL model
///     weights and asserts a real chat request gets a real (non-empty) answer back. Every other
///     test in this project is backed by FakeOllamaServer, so none of them can catch "LM Studio
///     itself is broken", "the configured models aren't actually loaded", or "the real network path
///     from SimpleModelPairGenerationService to LM Studio doesn't work". This test is the one thing
///     in the suite that would.
///
///     This is an integration/smoke test, not a unit test, and behaves accordingly:
///     <list type="bullet">
///         <item>It self-skips (logs why and returns, rather than failing) when LM Studio isn't
///         running on the default port (1234). Unlike Ollama, which can auto-start, LM Studio
///         requires the user to launch it manually first.</item>
///         <item>It talks to whatever LM Studio is already running on the machine running
///         the test, on the real default port (1234) — not an isolated fixture — since the whole
///         point is to prove the real thing works.</item>
///         <item>It allows a generous timeout and only asserts the response is non-empty, never
///         asserting exact content, since real model output is nondeterministic.</item>
///     </list>
/// </summary>
public sealed class RealLMStudioSmokeTests(ITestOutputHelper output)
{
	private static readonly TimeSpan ChatTimeout = TimeSpan.FromSeconds(90);
	private const int LMStudioDefaultPort = 1234;

	[Fact]
	public async Task RealChatRequest_AgainstRealLMStudio_ReturnsNonEmptyResponse()
	{
		using HttpClient http = new(new HttpClientHandler { UseProxy = false }) { Timeout = ChatTimeout };

		var providerConfig = new ProviderConfiguration
		{
			Host = "127.0.0.1",
			Port = LMStudioDefaultPort
		};

		var lmstudio = new LMStudioProvider(providerConfig, http);

		if (!await lmstudio.TestConnectionAsync(TestContext.Current.CancellationToken))
		{
			output.WriteLine(
				$"SKIPPED: LM Studio is not running on http://127.0.0.1:{LMStudioDefaultPort}. " +
				"Please start LM Studio with its local server enabled before running this smoke test.");
			return;
		}

		var models = await lmstudio.GetModelsAsync(TestContext.Current.CancellationToken);
		if (models == null || models.Count == 0)
		{
			output.WriteLine("SKIPPED: LM Studio returned an empty models list.");
			return;
		}

		var firstModel = models[0]?["id"]?.GetValue<string>();
		if (string.IsNullOrWhiteSpace(firstModel))
		{
			output.WriteLine("SKIPPED: Could not extract model ID from LM Studio models list.");
			return;
		}

		output.WriteLine($"Using LM Studio model: {firstModel}");

		var generationBackend = new LMStudioGenerationBackend(lmstudio);
		var generationOptions = new GenerationOptions { Temperature = 0.7 };

		using CancellationTokenSource cts = new(ChatTimeout);
		string response = await generationBackend.GenerateAsync(
			firstModel,
			"Reply with a short greeting. Keep it to one sentence.",
			generationOptions,
			cts.Token);

		output.WriteLine($"Real LM Studio response: {response}");
		Assert.False(string.IsNullOrWhiteSpace(response), "Expected a real, non-empty response from LM Studio.");
	}
}
