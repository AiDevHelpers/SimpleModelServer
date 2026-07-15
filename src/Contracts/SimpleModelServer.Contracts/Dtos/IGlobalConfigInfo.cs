namespace SimpleModelServer.Contracts.Dtos;

/// <summary>
///     Global configuration settings.
/// </summary>
public interface IGlobalConfigInfo
{
    /// <summary>Router host address (typically 127.0.0.1 or 0.0.0.0).</summary>
    string? RouterHost { get; }

    /// <summary>Router HTTP port.</summary>
    int RouterPort { get; }

    /// <summary>Virtual model name advertised by the router.</summary>
    string? RouterModelName { get; }

    /// <summary>Ollama server port.</summary>
    int OllamaPort { get; }

    /// <summary>Draft model name.</summary>
    string? DraftModel { get; }

    /// <summary>Main (refine) model name.</summary>
    string? MainModel { get; }

    /// <summary>Embedding model name.</summary>
    string? EmbedModel { get; }

    /// <summary>Default context size (num_ctx) if configured.</summary>
    int? ContextSize { get; }

    /// <summary>Default agent type (code, debug, planning, research).</summary>
    string? DefaultAgentType { get; }

    /// <summary>Forced agent type, if configured.</summary>
    string? ForcedAgentType { get; }

    /// <summary>Whether mDNS service discovery is enabled.</summary>
    bool MdnsEnabled { get; }

    /// <summary>mDNS service name override, if configured.</summary>
    string? MdnsServiceName { get; }
}
