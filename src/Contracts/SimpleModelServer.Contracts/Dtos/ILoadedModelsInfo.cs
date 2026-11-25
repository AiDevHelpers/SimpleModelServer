namespace SimpleModelServer.Contracts.Dtos;

/// <summary>
///     Information about currently loaded models for health/status reports.
/// </summary>
public interface ILoadedModelsInfo
{
    /// <summary>Draft model name if loaded</summary>
    string? DraftModelName { get; }

    /// <summary>Main/primary model name if loaded</summary>
    string? MainModelName { get; }

    /// <summary>Embedding model name if loaded</summary>
    string? EmbedModelName { get; }

    /// <summary>Whether draft model is in GPU memory</summary>
    bool DraftOnGpu { get; }

    /// <summary>Whether main model is in GPU memory</summary>
    bool MainOnGpu { get; }

    /// <summary>Whether embedding model is in GPU memory</summary>
    bool EmbedOnGpu { get; }

    /// <summary>Estimated memory used by loaded models in GB</summary>
    double EstimatedMemoryGb { get; }

    /// <summary>Current configuration ID being used</summary>
    int ConfigurationId { get; }

    /// <summary>Timestamp when models were loaded/last updated</summary>
    DateTime LoadedAt { get; }

    /// <summary>
    ///     Check if configuration is complete (has draft or main + embed).
    /// </summary>
    bool IsComplete { get; }

    /// <summary>
    ///     Get human-readable status of loaded models.
    /// </summary>
    string GetStatus();

    /// <summary>Creates a shallow copy for thread-safe retrieval.</summary>
    LoadedModelsInfo CreateSnapshot();
}
