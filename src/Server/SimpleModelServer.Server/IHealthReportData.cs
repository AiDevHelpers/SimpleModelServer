namespace SimpleModelServer.Server;

using SimpleModelServer.Contracts.Dtos;

/// <summary>
///     Complete health status including Ollama health, model responsiveness, and loaded model information.
///     Returned by the /health endpoint for downstream client decision-making.
/// </summary>
public interface IHealthReportData
{
    /// <summary>Overall health status: "healthy", "degraded", or "unhealthy"</summary>
    string Status { get; }

    /// <summary>Human-readable status message</summary>
    string? Message { get; }

    /// <summary>Currently active configuration ID</summary>
    int? ConfigurationId { get; }

    /// <summary>Information about loaded models (names, placement, memory usage)</summary>
    ILoadedModelsInfo? Models { get; }

    /// <summary>GPU information and capabilities</summary>
    IGpuInfoReport? Gpu { get; }

    /// <summary>Recent fault message if any</summary>
    string? RecentFault { get; }

    /// <summary>When this report was generated</summary>
    DateTime Timestamp { get; }

    /// <summary>
    ///     Gets the overall health status text based on component health.
    /// </summary>
    string GetHealthSummary();
}
