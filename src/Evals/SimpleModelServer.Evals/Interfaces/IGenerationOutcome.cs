namespace SimpleModelServer.Evals.Interfaces;

public interface IGenerationOutcome
{
    string RawResponse { get; }
    string? FinishReason { get; }
    int? CompletionTokens { get; }
    double LatencyMs { get; }
    string? Error { get; }
    bool Succeeded { get; }
}
