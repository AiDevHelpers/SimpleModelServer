using SimpleModelServer.Contracts.Models;

using System.Text.Json.Nodes;

namespace SimpleModelServer.Providers.Common.Generation;

/// <summary>
///     Backend-agnostic contract for turning a (model, prompt) pair into generated text —
///     streaming or single-shot — so <see cref="SimpleModelServer.Server.SimpleModelPairGenerationService" />
///     doesn't have to know whether it's talking to Ollama's native API or LM Studio's
///     OpenAI-compatible one. Each implementation owns translating <see cref="GenerationOptions" />
///     into its own wire format and ignoring the options it has no equivalent for (e.g. LM Studio
///     has no per-request GPU-layer/keep-alive control).
/// </summary>
public interface IGenerationBackend
{
    /// <summary>Starts a streaming generation; caller reads tokens via the returned handle.</summary>
    Task<IGenerationHandle> StartStreamAsync(string model, string prompt, GenerationOptions options, CancellationToken ct = default);

    /// <summary>Single-shot, non-streaming generation — returns the full response text (or "" on failure).</summary>
    Task<string> GenerateAsync(string model, string prompt, GenerationOptions options, CancellationToken ct = default);

    /// <summary>
    ///     Generates embeddings for <paramref name="input" /> (a JSON string or array of strings,
    ///     matching Ollama's/OpenAI's shared "input" shape). Returns one float-array embedding
    ///     vector per input item, in order — or <see langword="null" /> on failure. Each backend
    ///     translates to its own native embeddings endpoint (Ollama's <c>/api/embed</c>, LM
    ///     Studio's OpenAI-compatible <c>/v1/embeddings</c>) and normalizes the response back to
    ///     this shared shape.
    /// </summary>
    Task<JsonArray?> GenerateEmbeddingsAsync(string model, JsonNode? input, CancellationToken ct = default);
}

/// <summary>
///     A live streaming generation in progress. Disposing releases whatever transport resources
///     (HTTP response, stream reader) the backend opened for it.
/// </summary>
public interface IGenerationHandle : IAsyncDisposable
{
    Task<TokenResult> ReadTokenAsync(TimeSpan timeout);
}
