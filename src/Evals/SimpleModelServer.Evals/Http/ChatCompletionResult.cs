namespace SimpleModelServer.Evals.Http;

public sealed record ChatCompletionResult(
	string Content,
	string? FinishReason,
	int? PromptTokens,
	int? CompletionTokens,
	double LatencyMs,
	string? Error = null) : IChatCompletionResult
{
	public bool Succeeded => Error is null;
}
