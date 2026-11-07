namespace SimpleModelServer.Hardware;

public interface IGpuAllocationPlan
{
	IReadOnlyDictionary<string, int> NumGpuLayers { get; }
	string Summary { get; }
	int? TryGetLayers(string model);
}
