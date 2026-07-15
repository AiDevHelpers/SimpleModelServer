namespace SimpleModelServer.Contracts.Providers;

/// <summary>
///     Supported model inference providers/backends.
/// </summary>
public enum Provider
{
    /// <summary>Ollama - local inference engine with model management</summary>
    Ollama = 0,

    /// <summary>LM Studio - local inference engine with easy model management UI</summary>
    LMStudio = 1,
}
