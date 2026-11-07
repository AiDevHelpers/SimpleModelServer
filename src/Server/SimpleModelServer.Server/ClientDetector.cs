using Microsoft.AspNetCore.Http;

namespace SimpleModelServer.Server;

/// <summary>
///     Detects the type of client making a request based on User-Agent, headers, and other signals.
///     Allows the router to respond with format-appropriate responses (Ollama-native vs OpenAI-compatible).
/// </summary>
internal static class ClientDetector
{
	public enum ClientType
	{
		/// <summary>Ollama-compatible client (ollama CLI, localpilot, etc.)</summary>
		Ollama,
		/// <summary>OpenAI-compatible client or unknown client</summary>
		OpenAi,
	}

	/// <summary>
	///     Detects the client type from an HTTP request. Checks User-Agent, custom headers, and
	///     other signals to determine whether the client expects Ollama-native or OpenAI-compatible
	///     responses.
	/// </summary>
	public static ClientType Detect(HttpRequest request)
	{
		// Custom header takes priority (for testing and explicit routing)
		string? formatHeader = request.Headers["X-Client-Format"];
		if (string.Equals(formatHeader, "ollama", StringComparison.OrdinalIgnoreCase))
		{
			return ClientType.Ollama;
		}

		if (string.Equals(formatHeader, "openai", StringComparison.OrdinalIgnoreCase))
		{
			return ClientType.OpenAi;
		}

		string? userAgent = request.Headers["User-Agent"];

		// Ollama clients and clones
		if (userAgent?.Contains("ollama", StringComparison.OrdinalIgnoreCase) ?? false)
		{
			return ClientType.Ollama;
		}

		if (userAgent?.Contains("localpilot", StringComparison.OrdinalIgnoreCase) ?? false)
		{
			return ClientType.Ollama;
		}

		if (userAgent?.Contains("tokensaver", StringComparison.OrdinalIgnoreCase) ?? false)
		{
			return ClientType.Ollama;
		}

		// Clients with no User-Agent (LocalPilot, TokenSaver, etc.) default to Ollama format
		if (string.IsNullOrEmpty(userAgent))
		{
			return ClientType.Ollama;
		}

		// Default to OpenAI format for recognizable/known clients
		return ClientType.OpenAi;
	}
}
