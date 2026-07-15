using SimpleModelServer.Evals.Http;
using SimpleModelServer.Evals.Interfaces;

namespace SimpleModelServer.Evals.Codegen;

/// <summary>The v1 pipeline: one call to the generation endpoint, no repair loop.</summary>
public sealed class DirectGenerationPipeline(LlamaChatClient client, string genUrl, double temperature, int maxTokens)
    : IDirectGenerationPipeline
{
    public async Task<GenerationOutcome> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        List<ChatMessage> messages = [new ChatMessage("user", prompt)];
        IChatCompletionResult result = await client.CompleteAsync(
            genUrl, messages, temperature, maxTokens, null, cancellationToken);

        return result.Succeeded
            ? new GenerationOutcome(result.Content, result.FinishReason, result.CompletionTokens, result.LatencyMs)
            : new GenerationOutcome("", result.FinishReason, result.CompletionTokens, result.LatencyMs, result.Error);
    }
}
