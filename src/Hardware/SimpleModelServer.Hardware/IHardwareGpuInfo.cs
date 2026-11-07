namespace SimpleModelServer.Hardware;

public interface IHardwareGpuInfo
{
	bool Detected { get; }
	string Name { get; }

	/// <summary>Total VRAM reported by the driver, in MB.</summary>
	int? MemoryMb { get; }

	/// <summary>
	///     Free VRAM at detection time, in MB (nvidia-smi's <c>memory.free</c>). Null when unknown
	///     (e.g. detection failed, or a driver that doesn't report it). This reflects whatever else
	///     is already resident on the GPU (desktop compositor, other apps, a previous Ollama load)
	///     and is what <see cref="GpuBudgetPlanner" /> budgets against — using <see cref="MemoryMb" />
	///     (total) instead would over-commit on machines where the GPU isn't exclusively ours.
	/// </summary>
	int? FreeMemoryMb { get; }
}
