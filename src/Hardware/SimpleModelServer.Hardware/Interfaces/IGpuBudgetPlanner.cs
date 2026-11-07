using Microsoft.Extensions.Logging;

namespace SimpleModelServer.Hardware.Interfaces;

public interface IGpuBudgetPlanner
{
	Task<GpuAllocationPlan> PlanAsync(
		HttpClient http,
		int ollamaPort,
		IHardwareGpuInfo gpu,
		IReadOnlyList<GpuBudgetPlanner.ModelGpuRequest> models,
		ILogger logger,
		CancellationToken ct = default);
}
