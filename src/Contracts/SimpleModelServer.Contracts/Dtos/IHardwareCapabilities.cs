namespace SimpleModelServer.Contracts.Dtos;

/// <summary>
///     Hardware capabilities and resource information.
/// </summary>
public interface IHardwareCapabilities
{
    /// <summary>Whether a GPU is detected and available.</summary>
    bool GpuAvailable { get; }

    /// <summary>GPU name/model if available (e.g., "NVIDIA GeForce RTX 4090").</summary>
    string? GpuName { get; }

    /// <summary>Total GPU memory in MB, if available.</summary>
    int? GpuMemoryMb { get; }

    /// <summary>Free GPU memory in MB at detection time, if available.</summary>
    int? GpuFreeMemoryMb { get; }

    /// <summary>GPU layers allocated for inference.</summary>
    int? GpuLayers { get; }

    /// <summary>CPU thread count available.</summary>
    int? CpuThreads { get; }

    /// <summary>Maximum concurrent background inference requests this machine can handle.</summary>
    int MaxConcurrentSummaryRequests { get; }
}
