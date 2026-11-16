namespace SimpleModelServer.Contracts.Dtos;

using SimpleModelServer.Contracts.Models;

/// <summary>
///     Information about currently loaded models for health/status reports.
/// </summary>
public class LoadedModelsInfo : ILoadedModelsInfo
{
	/// <summary>Draft model name if loaded</summary>
	public string? DraftModelName { get; set; }

	/// <summary>Main/primary model name if loaded</summary>
	public string? MainModelName { get; set; }

	/// <summary>Embedding model name if loaded</summary>
	public string? EmbedModelName { get; set; }

	/// <summary>Whether draft model is in GPU memory</summary>
	public bool DraftOnGpu { get; set; }

	/// <summary>Whether main model is in GPU memory</summary>
	public bool MainOnGpu { get; set; }

	/// <summary>Whether embedding model is in GPU memory</summary>
	public bool EmbedOnGpu { get; set; }

	/// <summary>Estimated memory used by loaded models in GB</summary>
	public double EstimatedMemoryGb { get; set; }

	/// <summary>Current configuration ID being used</summary>
	public int ConfigurationId { get; set; }

	/// <summary>Timestamp when models were loaded/last updated</summary>
	public DateTime LoadedAt { get; set; }

	/// <summary>
	///     Check if configuration is complete (has draft or main + embed).
	/// </summary>
	public bool IsComplete =>
		(!string.IsNullOrEmpty(DraftModelName) || !string.IsNullOrEmpty(MainModelName))
		&& !string.IsNullOrEmpty(EmbedModelName);

	/// <summary>
	///     Get human-readable status of loaded models.
	/// </summary>
	public string GetStatus()
	{
		if (!IsComplete)
		{
			return "Incomplete: missing models";
		}

		var locations = new List<string>();
		if (!string.IsNullOrEmpty(DraftModelName))
		{
			locations.Add($"Draft({(DraftOnGpu ? "GPU" : "CPU")})");
		}

		if (!string.IsNullOrEmpty(MainModelName))
		{
			locations.Add($"Main({(MainOnGpu ? "GPU" : "CPU")})");
		}

		if (!string.IsNullOrEmpty(EmbedModelName))
		{
			locations.Add($"Embed({(EmbedOnGpu ? "GPU" : "CPU")})");
		}

		return $"Ready: {string.Join(" + ", locations)}, Mem: {EstimatedMemoryGb:F1}GB";
	}

	/// <summary>Creates a shallow copy for thread-safe retrieval.</summary>
	public LoadedModelsInfo CreateSnapshot() =>
		new()
		{
			DraftModelName = DraftModelName,
			MainModelName = MainModelName,
			EmbedModelName = EmbedModelName,
			DraftOnGpu = DraftOnGpu,
			MainOnGpu = MainOnGpu,
			EmbedOnGpu = EmbedOnGpu,
			EstimatedMemoryGb = EstimatedMemoryGb,
			ConfigurationId = ConfigurationId,
			LoadedAt = LoadedAt,
		};
}


