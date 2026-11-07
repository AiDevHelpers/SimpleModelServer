namespace SimpleModelServer.Contracts.Dtos;

using SimpleModelServer.Contracts.Models;

/// <summary>
///     Configuration for embedding models. Embedding models are specialized for
///     generating vector embeddings and have different parameters than generation models.
/// </summary>
public class EmbedConfig
{
	/// <summary>Embedding model name (e.g., "nomic-embed-text")</summary>
	public string Name { get; set; } = "";

	/// <summary>Context window size in tokens</summary>
	public int? NumCtx { get; set; }

	/// <summary>
	///     Keep-alive duration in minutes. 0 = disabled (always hot, never unload).
	///     Positive values = minutes to keep alive.
	/// </summary>
	public int? KeepAliveMinutes { get; set; }

	/// <summary>Stop tokens for embedding generation</summary>
	public string[] Stop { get; set; } = [];
}

