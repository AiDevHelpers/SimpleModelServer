using System.Text.Json.Nodes;

namespace SimpleModelServer.Contracts.Providers;

/// <summary>
///     LM Studio provider interface for OpenAI-compatible API communication.
///     LM Studio is a local inference engine with a user-friendly interface for model management.
///     Default port: 1234
/// </summary>
public interface ILMStudioProvider
{
    /// <summary>
    ///     Get the base URL for the LM Studio server.
    /// </summary>
    string BaseUrl { get; }

    /// <summary>
    ///     Test connection to the LM Studio server.
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);

    /// <summary>
    ///     Get available models from LM Studio.
    /// </summary>
    Task<JsonArray?> GetModelsAsync(CancellationToken ct = default);

    /// <summary>
    ///     Send a chat completion request to LM Studio. Pass
    ///     <see cref="HttpCompletionOption.ResponseHeadersRead" /> for a streaming request
    ///     (<c>"stream": true</c> in <paramref name="request" />) — the default,
    ///     <see cref="HttpCompletionOption.ResponseContentRead" />, buffers the entire response
    ///     body before returning, which would silently turn a streamed request into a blocking one.
    /// </summary>
    Task<HttpResponseMessage> PostChatCompletionAsync(
        JsonObject request,
        CancellationToken ct = default,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead);

    /// <summary>
    ///     Send a text completion request to LM Studio.
    /// </summary>
    Task<HttpResponseMessage> PostCompletionAsync(JsonObject request, CancellationToken ct = default);

    /// <summary>
    ///     Send an embeddings request to LM Studio.
    /// </summary>
    Task<HttpResponseMessage> PostEmbeddingsAsync(JsonObject request, CancellationToken ct = default);
}
