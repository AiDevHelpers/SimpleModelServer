using System.Net.Http.Json;
using System.Text.Json;

using SimpleModelServer.Config.Configuration;

using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Covers SimpleModelPairGenerationService.ChooseFinalText — the private selection logic
///     GetDraftPairResponseAsync uses to pick between the draft and main model's output: main
///     wins whenever it produced anything, and only falls back to draft's text when main comes
///     back empty/whitespace. This had no direct coverage before (existing PostEndpointTests only
///     asserted "some text came back", using the same canned response for both models, which
///     could never actually exercise the fallback branch).
///
///     ChooseFinalText itself is private, so this drives it the only way available without
///     reflection: through POST /api/generate (non-streaming, no research trigger), which is the
///     real production call path into GetDraftPairResponseAsync. FakeOllamaServer's
///     ResponseTextByModel lets draft and main return distinct text so the two branches are
///     independently observable.
/// </summary>
public sealed class SimpleModelPairMergeTests
{
	private const string DraftModel = "draft-test-model";
	private const string MainModel = "main-test-model";

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
				MainModel = MainModel
			},
			Agents = config.Agents,
			ModelSources = config.ModelSources,
			ModelRoles = config.ModelRoles
		});
	}

	private static async Task<string> GenerateAsync(RouterTestFixture fixture, string prompt)
	{
		using HttpResponseMessage response = await fixture.Client.PostAsJsonAsync(
			"/api/generate", new { prompt, stream = false });
		string body = await response.Content.ReadAsStringAsync();
		using JsonDocument doc = JsonDocument.Parse(body);
		return doc.RootElement.GetProperty("response").GetString() ?? "";
	}

	[Fact]
	public async Task BothModelsRespond_MainModelTextWins()
	{
		await using RouterTestFixture fixture = await CreateFixtureAsync();
		fixture.Ollama.ResponseTextByModel[DraftModel] = "draft answer";
		fixture.Ollama.ResponseTextByModel[MainModel] = "main answer";

		string final = await GenerateAsync(fixture, "say something");

		Assert.Equal("main answer", final);
	}

	[Fact]
	public async Task MainModelReturnsEmpty_FallsBackToDraftText()
	{
		await using RouterTestFixture fixture = await CreateFixtureAsync();
		fixture.Ollama.ResponseTextByModel[DraftModel] = "draft answer";
		fixture.Ollama.ResponseTextByModel[MainModel] = "";

		string final = await GenerateAsync(fixture, "say something");

		Assert.Equal("draft answer", final);
	}

	[Fact]
	public async Task MainModelReturnsWhitespaceOnly_StillFallsBackToDraftText()
	{
		await using RouterTestFixture fixture = await CreateFixtureAsync();
		fixture.Ollama.ResponseTextByModel[DraftModel] = "draft answer";
		fixture.Ollama.ResponseTextByModel[MainModel] = "   ";

		string final = await GenerateAsync(fixture, "say something");

		Assert.Equal("draft answer", final);
	}

	[Fact]
	public async Task BothModelsReturnEmpty_ResultIsEmpty()
	{
		await using RouterTestFixture fixture = await CreateFixtureAsync();
		fixture.Ollama.ResponseTextByModel[DraftModel] = "";
		fixture.Ollama.ResponseTextByModel[MainModel] = "";

		string final = await GenerateAsync(fixture, "say something");

		Assert.Equal("", final);
	}

	[Fact]
	public async Task DraftModelEmpty_MainModelText_MainStillWins()
	{
		// Draft coming back empty must not somehow suppress main's text — main is the
		// preferred/authoritative answer whenever it has anything at all.
		await using RouterTestFixture fixture = await CreateFixtureAsync();
		fixture.Ollama.ResponseTextByModel[DraftModel] = "";
		fixture.Ollama.ResponseTextByModel[MainModel] = "main answer";

		string final = await GenerateAsync(fixture, "say something");

		Assert.Equal("main answer", final);
	}
}
