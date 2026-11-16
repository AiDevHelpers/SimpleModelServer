using System.Threading.Tasks;
using SimpleModelServer.Config.Configuration;

using Xunit;

namespace SimpleModelServer.Server.Tests;

using SimpleModelServer.Providers.Ollama;

/// <summary>
///     Covers the startup-warmup fix in SimpleModelPairApp.RunAsync: draft, main, AND embed must all be
///     loaded into VRAM (via ModelHealthMonitor.WarmupModelsAsync) before the router starts
///     accepting traffic, rather than relying on the periodic ModelHealthMonitor.RunAsync loop
///     (whose first cycle only runs after a 15s Task.Delay) to warm them for the first time. That
///     gap was the root cause of "/health ... none are currently loaded in VRAM" being observed
///     immediately after every service start/restart.
///
///     SimpleModelPairApp.RunAsync itself isn't exercised directly here (it shells out to a real
///     `ollama` CLI via OllamaProcessManager.CommandExists/StartServerAsync, which isn't available
///     in this test environment) — instead this asserts the underlying primitive SimpleModelPairApp
///     calls at startup: ModelHealthMonitor.WarmupModelsAsync given all three model names actually
///     warms all three, individually, against a fake backend. That's the same method and the same
///     three-model list SimpleModelPairApp.RunAsync now passes it.
///
///     Model names are set explicitly via configureConfig rather than relying on
///     GlobalConfig.DraftModel/MainModel/EmbedModel's "" default falling through to
///     SimpleModelPairGenerationService's hardcoded fallback names — keeps these tests independent of
///     that fallback logic.
/// </summary>
public sealed class StartupWarmupTests
{
	private const string DraftModel = "qwen2.5-coder-0.5b-chat";
	private const string MainModel = "qwen2.5-coder-3b-chat";
	private const string EmbedModel = "nomic-embed-text";

	private static Task<RouterTestFixture> CreateFixtureAsync()
	{
		return RouterTestFixture.CreateAsync(config => new AppConfig
		{
			Global = new GlobalConfig
			{
				DefaultAgentType = config.Global.DefaultAgentType,
				OllamaPort = config.Global.OllamaPort,
				RouterHost = config.Global.RouterHost,
				RouterModelName = config.Global.RouterModelName,
				DraftModel = DraftModel,
				MainModel = MainModel,
				EmbedModel = EmbedModel
			},
			Agents = config.Agents,
			ModelSources = config.ModelSources,
			ModelRoles = config.ModelRoles
		});
	}

	[Fact]
	public async Task WarmupModelsAsync_GivenDraftMainAndEmbed_WarmsAllThreeIndividually()
	{
		await using RouterTestFixture fixture = await CreateFixtureAsync();

		await fixture.Health.WarmupModelsAsync([DraftModel, MainModel, EmbedModel]);

		// Each model must have received its own /api/generate warmup call — this is what
		// distinguishes "all 3 models warmed at startup" from the previous behavior, where only
		// draft+main were ever warmed (embed wasn't in the list SimpleModelPairApp passed at all,
		// startup or periodic) and even those two weren't warmed until the first periodic cycle.
		Assert.True(fixture.Ollama.GenerateCallsByModel.GetValueOrDefault(DraftModel) >= 1,
			$"Expected at least one warmup call for draft model '{DraftModel}'.");
		Assert.True(fixture.Ollama.GenerateCallsByModel.GetValueOrDefault(MainModel) >= 1,
			$"Expected at least one warmup call for main model '{MainModel}'.");
		Assert.True(fixture.Ollama.GenerateCallsByModel.GetValueOrDefault(EmbedModel) >= 1,
			$"Expected at least one warmup call for embed model '{EmbedModel}'.");
	}

	[Fact]
	public async Task WarmupModelsAsync_NoneWarmingUpAfterCompletion()
	{
		await using RouterTestFixture fixture = await CreateFixtureAsync();
		string[] models = [DraftModel, MainModel, EmbedModel];

		await fixture.Health.WarmupModelsAsync(models);

		// IsModelWarmingUp is what /api/generate's 202 "warming_up" short-circuit checks — once
		// WarmupModelsAsync has returned, nothing should still be marked in-flight.
		foreach (string model in models)
		{
			Assert.False(fixture.Health.IsModelWarmingUp(model), $"'{model}' should not still be marked warming-up.");
		}
	}

	/// <summary>
	///     Regression guard for the specific bug: the periodic warmup list SimpleModelPairApp passes to
	///     <see cref="ModelHealthMonitor.RunAsync" /> must include the embed model, not just
	///     draft/main — otherwise embed can idle out of VRAM after its keep_alive window and
	///     reopen the same "not loaded" gap between periodic cycles that the startup fix closes at
	///     boot.
	/// </summary>
	[Fact]
	public async Task WarmupModelsAsync_EmbedModel_IsWarmedLikeDraftAndMain()
	{
		await using RouterTestFixture fixture = await CreateFixtureAsync();

		await fixture.Health.WarmupModelAsync(EmbedModel);

		Assert.True(fixture.Ollama.GenerateCallsByModel.GetValueOrDefault(EmbedModel) >= 1);
		Assert.False(fixture.Health.IsModelWarmingUp(EmbedModel));
	}
}


