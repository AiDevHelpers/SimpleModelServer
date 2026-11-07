namespace SimpleModelServer.Server;

using SimpleModelServer.Config;
using SimpleModelServer.Contracts.Dtos;
using System.Text.Json.Serialization;

/// <summary>
///     Complete health status including Ollama health, model responsiveness, and loaded model information.
///     Returned by the /health endpoint for downstream client decision-making.
/// </summary>
public class HealthReportData : IHealthReportData
{
	/// <summary>Overall health status: "healthy", "degraded", or "unhealthy"</summary>
	[JsonPropertyName("status")]
	public string Status { get; set; } = "unknown";

	/// <summary>Human-readable status message</summary>
	[JsonPropertyName("message")]
	public string? Message { get; set; }

	/// <summary>Currently active configuration ID</summary>
	[JsonPropertyName("configurationId")]
	public int? ConfigurationId { get; set; }

	/// <summary>Information about loaded models (names, placement, memory usage)</summary>
	[JsonPropertyName("models")]
	public LoadedModelsInfo? Models { get; set; }

	ILoadedModelsInfo? IHealthReportData.Models => Models;

	/// <summary>GPU information and capabilities</summary>
	[JsonPropertyName("gpu")]
	public GpuInfoReport? Gpu { get; set; }

	IGpuInfoReport? IHealthReportData.Gpu => Gpu;

	/// <summary>Recent fault message if any</summary>
	[JsonPropertyName("recentFault")]
	public string? RecentFault { get; set; }

	/// <summary>When this report was generated</summary>
	[JsonPropertyName("timestamp")]
	public DateTime Timestamp { get; set; } = DateTime.UtcNow;

	/// <summary>Serializable GPU info for JSON responses</summary>
	public class GpuInfoReport : IGpuInfoReport
	{
		[JsonPropertyName("available")]
		public bool Available { get; set; }

		[JsonPropertyName("model")]
		public string? Model { get; set; }

		[JsonPropertyName("memoryGb")]
		public double MemoryGb { get; set; }

		[JsonPropertyName("maxModelSizeB")]
		public double? MaxModelSizeB { get; set; }
	}

	/// <summary>
	///     Creates a GPU info report from detection results.
	/// </summary>
	public static GpuInfoReport? FromGpuInfo(GpuInfo? gpu)
	{
		if (gpu is null)
		{
			return null;
		}

		return new GpuInfoReport
		{
			Available = gpu.IsAvailable,
			Model = gpu.Model,
			MemoryGb = gpu.MemoryGb,
			MaxModelSizeB = gpu.MaxModelSizeB,
		};
	}

	/// <summary>
	///     Gets the overall health status text based on component health.
	/// </summary>
	public string GetHealthSummary()
	{
		if (RecentFault is not null)
		{
			return $"degraded: {RecentFault}";
		}

		if (Models?.IsComplete != true)
		{
			return "degraded: incomplete model configuration";
		}

		return "healthy";
	}
}
