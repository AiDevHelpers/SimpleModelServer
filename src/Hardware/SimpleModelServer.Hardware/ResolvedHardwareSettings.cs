namespace SimpleModelServer.Hardware;

public sealed class ResolvedHardwareSettings : IResolvedHardwareSettings
{
	public int? GpuLayers { get; init; }
	public int? CpuThreads { get; init; }
	public IHardwareGpuInfo Gpu { get; init; } = GpuInfo.None;

	/// <summary>
	///     Conservative cap on concurrent background inference calls (e.g. AI file/folder
	///     summaries during indexing) that this machine can absorb without starving the
	///     interactive draft-pair generation path. Both models share one Ollama instance,
	///     so this is deliberately small even on capable hardware.
	/// </summary>
	public int MaxConcurrentSummaryRequests { get; init; } = 1;
}
