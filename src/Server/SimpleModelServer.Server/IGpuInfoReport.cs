namespace SimpleModelServer.Server;

public interface IGpuInfoReport
{
	bool Available { get; }
	string? Model { get; }
	double MemoryGb { get; }
	double? MaxModelSizeB { get; }
}
