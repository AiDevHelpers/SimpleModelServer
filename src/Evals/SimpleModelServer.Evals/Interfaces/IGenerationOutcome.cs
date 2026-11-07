namespace SimpleModelServer.Evals.Interfaces;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716",
	Justification = "Error is domain vocabulary consistent with the rest of this result-DTO family (IChatCompletionResult, IRouterCaseResult); renaming for cross-language interop that doesn't apply here would ripple across many call sites for no functional benefit.")]
public interface IGenerationOutcome
{
	string RawResponse { get; }
	string? FinishReason { get; }
	int? CompletionTokens { get; }
	double LatencyMs { get; }
	string? Error { get; }
	bool Succeeded { get; }
}
