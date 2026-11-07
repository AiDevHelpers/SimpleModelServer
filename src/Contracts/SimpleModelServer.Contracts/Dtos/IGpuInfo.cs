namespace SimpleModelServer.Contracts.Dtos;

/// <summary>
///     GPU capabilities and memory information for model placement decisions.
/// </summary>
public interface IGpuInfo
{
	/// <summary>GPU memory available in GB</summary>
	double MemoryGb { get; }

	/// <summary>Whether GPU is available and usable</summary>
	bool IsAvailable { get; }

	/// <summary>GPU model/name if available</summary>
	string? Model { get; }

	/// <summary>Estimated maximum model size that can run on GPU (in billions of parameters)</summary>
	double? MaxModelSizeB { get; }
}
