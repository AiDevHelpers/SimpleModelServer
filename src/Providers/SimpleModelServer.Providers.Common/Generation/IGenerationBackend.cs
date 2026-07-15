using SimpleModelServer.Contracts.Models;

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
}

/// <summary>
///     A live streaming generation in progress. Disposing releases whatever transport resources
///     (HTTP response, stream reader) the backend opened for it.
/// </summary>
public interface IGenerationHandle : IAsyncDisposable
{
    Task<TokenResult> ReadTokenAsync(TimeSpan timeout);
}
