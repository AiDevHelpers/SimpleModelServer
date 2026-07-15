namespace SimpleModelServer.Contracts.Dtos;

using SimpleModelServer.Contracts.Models;

/// <summary>
///     GPU capabilities and memory information for model placement decisions.
/// </summary>
public class GpuInfo : IGpuInfo
{
    /// <summary>GPU memory available in GB</summary>
    public double MemoryGb { get; set; }

    /// <summary>Whether GPU is available and usable</summary>
    public bool IsAvailable { get; set; }

    /// <summary>GPU model/name if available</summary>
    public string? Model { get; set; }

    /// <summary>Estimated maximum model size that can run on GPU (in billions of parameters)</summary>
    public double? MaxModelSizeB { get; set; }
}


