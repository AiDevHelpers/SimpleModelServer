namespace SimpleModelServer.Evals.Http;

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
