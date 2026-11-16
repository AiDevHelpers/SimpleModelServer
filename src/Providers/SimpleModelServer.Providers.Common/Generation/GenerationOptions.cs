using System.Text.Json.Nodes;

namespace SimpleModelServer.Providers.Common.Generation;

/// <summary>
///     Backend-agnostic generation options. Ollama's backend consumes every field (mirrors the
///     "options" object it has always built); LM Studio's backend ignores <see cref="NumGpu" />,
///     <see cref="NumThread" />, and <see cref="KeepAlive" /> — LM Studio's OpenAI-compatible API
///     has no per-request equivalent for GPU-layer count, CPU-thread count, or keep-alive.
/// </summary>
public sealed record GenerationOptions
{
	public static readonly GenerationOptions None = new();

	public int? ContextSize { get; init; }
	public double? Temperature { get; init; }
	public int? NumGpu { get; init; }
	public int? NumThread { get; init; }
	public IReadOnlyList<string>? Stop { get; init; }
	public string? SystemPrompt { get; init; }
	public string? KeepAlive { get; init; }

	/// <summary>Arbitrary extra sampling key/value pairs passed through verbatim (e.g. custom top_p/top_k).</summary>
	public JsonObject? ExtraSampling { get; init; }
}
