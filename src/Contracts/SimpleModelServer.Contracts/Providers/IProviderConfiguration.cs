namespace SimpleModelServer.Contracts.Providers;

/// <summary>
///     Configuration for the model inference provider (Ollama, LM Studio, etc.)
/// </summary>
public interface IProviderConfiguration
{
	/// <summary>Which provider to use for model inference</summary>
	Provider Type { get; set; }

	/// <summary>Server host/address (e.g., "127.0.0.1", "localhost")</summary>
	string Host { get; set; }

	/// <summary>Server port (e.g., 11434 for Ollama, 1234 for LM Studio)</summary>
	int Port { get; set; }

	/// <summary>Optional request timeout in seconds</summary>
	int? TimeoutSeconds { get; set; }
}
