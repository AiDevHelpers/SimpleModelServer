namespace SimpleModelServer.Evals.Http;

using System.Text.Json.Nodes;

public interface ILlamaChatClient : IDisposable
{
    Task<IChatCompletionResult> CompleteAsync(
        string baseUrl,
        IReadOnlyList<IChatMessage> messages,
        double temperature,
        int maxTokens,
        JsonNode? jsonSchema = null,
        CancellationToken cancellationToken = default);

    Task<string?> TryGetModelInfoAsync(string baseUrl, CancellationToken cancellationToken = default);
}
