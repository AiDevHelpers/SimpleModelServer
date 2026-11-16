using System.Text.Json.Nodes;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Dtos.Providers;

namespace SimpleModelServer.Config.Configuration;

/// <summary>
///     What the draft (0.5b) model is used for in SimpleModelPairGenerationService before the main (3b)
///     model runs. All three still return the main model's output as the final answer — they only
///     differ in what the draft model contributes to main's prompt.
/// </summary>
public enum DraftMode
{
	/// <summary>
	///     Draft streams a preview *answer* to the original prompt (up to
	///     <see cref="GlobalConfig.DraftHandoffTimeoutMs" />), which is spliced into main's prompt
	///     as "here's a draft answer — verify, correct, and complete it." Risk: an confidently
	///     wrong draft answer can anchor main on the same mistake. This is the original DraftModel
	///     behavior and the default.
	/// </summary>
	Preview,

	/// <summary>
	///     Draft rewrites the user's *prompt* (fixes typos/grammar, expands shorthand, makes an
	///     ambiguous ask explicit) and that rewritten prompt — not any draft answer — is what main
	///     actually generates a response to. No draft answer, so nothing to fall back to if main
	///     comes back empty (falls back to the original prompt's own text isn't meaningful either;
	///     an empty main response stays empty in this mode).
	/// </summary>
	Cleanup,

	/// <summary>
	///     Draft first rewrites the prompt (as in <see cref="Cleanup" />), then drafts a preview
	///     answer to the *cleaned* prompt (as in <see cref="Preview" />), and main receives both.
	///     Highest potential accuracy, at the cost of two sequential draft calls before main starts
	///     (added latency).
	/// </summary>
	Both,

	/// <summary>
	///     Main only: the prompt goes straight to main with no draft call at all. The draft model is not
	///     used, so the router does not need it loaded or healthy.
	/// </summary>
	None,

	/// <summary>
	///     Draft and main answer the same prompt in parallel; the reply is returned only after both finish.
	///     Main's answer is returned, and the draft is used only if main comes back empty. Streamed replies
	///     come from main alone.
	/// </summary>
	Verify
}
