using System.Net;

namespace SimpleModelServer.Contracts.Providers;

/// <summary>
///     Ollama API contract. All implementations (direct Ollama, or a router proxy) must support
///     these endpoints. See docs/architecture.md for the complete list of supported paths.
/// </summary>
public interface IOllama
{
	/// <summary>
	///     GET /api/tags: List models available locally.
	/// </summary>
	Task HandleGetApiTagsAsync(HttpListenerContext context);

	/// <summary>
	///     GET /api/version: Retrieve Ollama version.
	/// </summary>
	Task HandleGetApiVersionAsync(HttpListenerContext context);

	/// <summary>
	///     GET /api/ps: List models currently loaded into memory.
	/// </summary>
	Task HandleGetApiPsAsync(HttpListenerContext context);

	/// <summary>
	///     POST /api/generate: Generate text with a model.
	/// </summary>
	Task HandlePostApiGenerateAsync(HttpListenerContext context, CancellationToken ct = default);

	/// <summary>
	///     POST /api/chat: Chat completions.
	/// </summary>
	Task HandlePostApiChatAsync(HttpListenerContext context, CancellationToken ct = default);

	/// <summary>
	///     POST /api/embed: Generate embeddings from a model.
	/// </summary>
	Task HandlePostApiEmbedAsync(HttpListenerContext context, CancellationToken ct = default);

	/// <summary>
	///     POST /api/embeddings: Legacy embeddings endpoint (should map to /api/embed).
	/// </summary>
	Task HandlePostApiEmbeddingsAsync(HttpListenerContext context, CancellationToken ct = default);

	/// <summary>
	///     POST /api/show: Display model information including details and parameters.
	/// </summary>
	Task HandlePostApiShowAsync(HttpListenerContext context, CancellationToken ct = default);

	/// <summary>
	///     POST /api/create: Create a model from existing models or files.
	/// </summary>
	Task HandlePostApiCreateAsync(HttpListenerContext context, CancellationToken ct = default);

	/// <summary>
	///     POST /api/copy: Duplicate a model with another name.
	/// </summary>
	Task HandlePostApiCopyAsync(HttpListenerContext context, CancellationToken ct = default);

	/// <summary>
	///     DELETE /api/delete: Delete a model and its data.
	/// </summary>
	Task HandleDeleteApiAsync(HttpListenerContext context, CancellationToken ct = default);

	/// <summary>
	///     POST /api/pull: Download a model from the Ollama library.
	/// </summary>
	Task HandlePostApiPullAsync(HttpListenerContext context, CancellationToken ct = default);

	/// <summary>
	///     POST /api/push: Upload a model to a library.
	/// </summary>
	Task HandlePostApiPushAsync(HttpListenerContext context, CancellationToken ct = default);

	/// <summary>
	///     HEAD /api/blobs/:digest: Check if a blob exists.
	/// </summary>
	Task HandleHeadApiBlobsAsync(HttpListenerContext context, string digest);

	/// <summary>
	///     POST /api/blobs/:digest: Push a file blob to the server.
	/// </summary>
	Task HandlePostApiBlobsAsync(HttpListenerContext context, string digest, CancellationToken ct = default);
}
