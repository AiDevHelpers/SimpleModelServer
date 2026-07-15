namespace SimpleModelServer.Evals.Http;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716",
    Justification = "Error is domain vocabulary consistent with the rest of this result-DTO family (IGenerationOutcome, IRouterCaseResult); renaming for cross-language interop that doesn't apply here would ripple across many call sites for no functional benefit.")]
public interface IChatCompletionResult
{
    string Content { get; }
    string? FinishReason { get; }
    int? PromptTokens { get; }
    int? CompletionTokens { get; }
    double LatencyMs { get; }
    string? Error { get; }
    bool Succeeded { get; }
}
