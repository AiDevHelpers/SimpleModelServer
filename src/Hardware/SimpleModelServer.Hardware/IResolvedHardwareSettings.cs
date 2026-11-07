namespace SimpleModelServer.Hardware;

public interface IResolvedHardwareSettings
{
	int? GpuLayers { get; }
	int? CpuThreads { get; }
	IHardwareGpuInfo Gpu { get; }
	int MaxConcurrentSummaryRequests { get; }
}
