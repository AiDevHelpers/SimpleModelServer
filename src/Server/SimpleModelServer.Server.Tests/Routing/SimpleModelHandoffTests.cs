using System.Net.Http.Json;
using System.Text.Json;

using SimpleModelServer.Config.Configuration;

using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Covers the actual draft (0.5b) → main (3b) handoff in <see cref="DraftMode.Preview" /> (the
///     "spec mode" — draft streams a preview answer, main gets it spliced into its prompt and
///     verifies/completes it, main's output is always what's returned). SimpleModelPairMergeTests only
///     covers ChooseFinalText's selection logic; these tests cover the handoff mechanics
///     ChooseFinalText sits on top of — specifically, that main's prompt actually receives draft's
///     text, and that a slow/incomplete draft doesn't block or corrupt the handoff.
/// </summary>
public sealed class SimpleModelHandoffTests
{
	private const string DraftModel = "draft-test-model";
	private const string MainModel = "main-test-model";

	private static Task<RouterTestFixture> CreateFixtureAsync(int? draftHandoffTimeoutMs = null)
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
				DraftMode = DraftMode.Preview,
				DraftHandoffTimeoutMs = draftHandoffTimeoutMs ?? 900
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

	/// <summary>
	///     Simple case: the draft model resolves quickly and completely (well inside the default
	///     900ms handoff window), so its full answer should be spliced into main's prompt, and
	///     main's own answer — not draft's — is what comes back to the caller.
	/// </summary>
	[Fact]
	public async Task FastDraft_FullDraftTextSplicedIntoMainPrompt_MainAnswerReturned()
	{
		await using RouterTestFixture fixture = await CreateFixtureAsync();
		fixture.Ollama.ResponseTextByModel[DraftModel] = "quick draft answer";
		fixture.Ollama.ResponseTextByModel[MainModel] = "final main answer";

		string final = await GenerateAsync(fixture, "write me a method to load the weather");

		Assert.Equal("final main answer", final);
		Assert.True(fixture.Ollama.LastPromptByModel.TryGetValue(MainModel, out string? mainPrompt));
		Assert.Contains("quick draft answer", mainPrompt, StringComparison.Ordinal);
		Assert.Contains("write me a method to load the weather", mainPrompt, StringComparison.Ordinal);
	}

	/// <summary>
	///     Complex case: the draft model streams slowly (300ms between chunks) against a short
	///     50ms handoff timeout, so only the first chunk should make it into main's prompt before
	///     handoff — the second chunk arrives well after the cutoff and must NOT appear in main's
	///     prompt, and must not delay main starting or the final answer coming back. This is the
	///     "0.5b starts, hands off to 3b whatever it has so far" behavior, not "wait for 0.5b to
	///     finish."
	/// </summary>
	[Fact]
	public async Task SlowDraftStream_OnlyTextBeforeHandoffCutoffIsSpliced_LaterChunkNeverAppears()
	{
		await using RouterTestFixture fixture = await CreateFixtureAsync(draftHandoffTimeoutMs: 50);
		fixture.Ollama.StreamedTokensByModel[DraftModel] =
			(["First chunk. ", "Second chunk should never reach main. "], TimeSpan.FromMilliseconds(300));
		fixture.Ollama.ResponseTextByModel[MainModel] = "final main answer";

		string final = await GenerateAsync(fixture, "write me a method to load the weather");

		Assert.Equal("final main answer", final);
		Assert.True(fixture.Ollama.LastPromptByModel.TryGetValue(MainModel, out string? mainPrompt));
		Assert.Contains("First chunk.", mainPrompt, StringComparison.Ordinal);
		Assert.DoesNotContain("Second chunk should never reach main.", mainPrompt, StringComparison.Ordinal);
	}
}
