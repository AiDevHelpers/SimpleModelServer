using SimpleModelServer.Contracts.Providers;

namespace SimpleModelServer.Contracts.Dtos.Providers;

/// <summary>
///     Configuration for the model inference provider (Ollama, LM Studio, etc.)
/// </summary>
public class ProviderConfiguration : IProviderConfiguration
{
    /// <summary>Which provider to use for model inference</summary>
    public Provider Type { get; set; } = Provider.Ollama;

    /// <summary>Server host/address (e.g., "127.0.0.1", "localhost")</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Server port (e.g., 11434 for Ollama, 1234 for LM Studio)</summary>
    public int Port { get; set; } = 11434;

    /// <summary>Optional API key for authentication with the inference server</summary>
    public string? ApiKey { get; set; }

    /// <summary>Optional request timeout in seconds</summary>
    public int? TimeoutSeconds { get; set; }
}
