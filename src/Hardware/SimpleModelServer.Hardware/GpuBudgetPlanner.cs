using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Hardware.Interfaces;

using Microsoft.Extensions.Logging;

using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Hardware;

/// <summary>
///     Decides how many layers of each model to place on the GPU (Ollama's <c>num_gpu</c>
///     option) when more than one model shares the same GPU. <see cref="HardwareDetection" />
///     only ever resolved a single global layer count applied uniformly to every model, which is
///     fine for one model but causes VRAM thrashing when a draft model, a main model, and an
///     embedding model all need to be kept warm at once (see ModelHealthMonitor's warmup loop) —
///     each warmup evicts whichever model doesn't currently fit, so none of them ever stay
///     resident.
///
///     This planner estimates each model's VRAM footprint — weights *and* its KV cache at the
///     model's configured context size, since KV cache scales with num_ctx and can rival or
///     exceed weight size for small models run at a large context — and allocates GPU layers
///     per model against the GPU's *free* (not total) VRAM, so it accounts for whatever the
///     desktop/other apps are already using.
/// </summary>
public sealed partial class GpuBudgetPlanner : IGpuBudgetPlanner
{
	[LoggerMessage(Level = LogLevel.Information, Message = "GPU planning: '{Model}' missing architecture metadata from Ollama, using heuristic estimate ({BlockCount} layers, ~{SizeMb} MB).")]
	private static partial void LogGpuPlanningMissingMetadata(ILogger logger, string model, int blockCount, long sizeMb);

	[LoggerMessage(Level = LogLevel.Information, Message = "GPU planning: couldn't read '{Model}' architecture from Ollama, using heuristic estimate.")]
	private static partial void LogGpuPlanningCouldntRead(ILogger logger, Exception ex, string model);
	/// <summary>
	///     Fraction of free VRAM we're willing to plan against. Ollama/CUDA context overhead,
	///     fragmentation, and the fact that "free" can drift between detection and actual load
	///     all mean planning against 100% of free VRAM reliably ends in an OOM/evicted model, so
	///     this leaves headroom.
	/// </summary>
	private const double SafetyFactor = 0.85;

	/// <summary>Flat reserve (MB) for CUDA context/allocator overhead, on top of <see cref="SafetyFactor" />.</summary>
	private const int ReserveMb = 512;

	private const int BytesPerMb = 1024 * 1024;

	/// <summary>K and V caches, each stored at this many bytes/element (fp16 KV cache — Ollama's default).</summary>
	private const int KvBytesPerElement = 2;

	/// <summary>One request describing a model that needs GPU layers planned for it.</summary>
	/// <param name="Name">Ollama model name (as passed to /api/generate).</param>
	/// <param name="NumCtx">The context size this model will actually be run at — KV cache scales with this.</param>
	/// <param name="Pinned">
	///     Explicit user override (<see cref="ModelSourceConfig.NumGpu" />). When set, this model is
	///     excluded from auto-allocation, but its estimated footprint (at the pinned layer count) is
	///     still subtracted from the shared budget so other models don't over-commit around it.
	/// </param>
	/// <param name="Priority">
	///     Lower allocates first (and preferentially gets its full layer count). Ties broken by
	///     declaration order.
	/// </param>
	public readonly record struct ModelGpuRequest(string Name, int NumCtx, int? Pinned, int Priority);

	public async Task<GpuAllocationPlan> PlanAsync(
		HttpClient http,
		int ollamaPort,
		IHardwareGpuInfo gpu,
		IReadOnlyList<ModelGpuRequest> models,
		ILogger logger,
		CancellationToken ct = default)
	{
		Dictionary<string, int> result = new(StringComparer.OrdinalIgnoreCase);
		List<string> summaryLines = [];

		if (!gpu.Detected || gpu.FreeMemoryMb is null)
		{
			// No usable GPU telemetry — honor explicit pins (a user who hand-set num_gpu clearly
			// has an opinion), leave everything else alone so the existing global
			// HardwareDetection.GpuLayers fallback (or Ollama's own default) applies instead of us
			// guessing blind.
			foreach (ModelGpuRequest m in models)
			{
				if (m.Pinned is not null)
				{
					result[m.Name] = m.Pinned.Value;
				}
			}

			return new GpuAllocationPlan
			{
				NumGpuLayers = result,
				Summary = gpu.Detected
					? "GPU detected but free VRAM unknown (nvidia-smi didn't report memory.free) — per-model auto-planning skipped."
					: "No GPU detected — per-model auto-planning skipped."
			};
		}

		long budgetBytes = (long)(gpu.FreeMemoryMb.Value * SafetyFactor * BytesPerMb) - (long)ReserveMb * BytesPerMb;
		budgetBytes = Math.Max(0, budgetBytes);
		long initialBudgetBytes = budgetBytes;

		Dictionary<string, long> sizeByModel = await TryFetchModelSizesAsync(http, ollamaPort, ct);

		// Fetch architecture info for every model up front (pinned models too — we still need
		// their footprint to subtract from the shared budget).
		Dictionary<string, ModelArchInfo> archByModel = new(StringComparer.OrdinalIgnoreCase);
		foreach (ModelGpuRequest m in models)
		{
			_ = sizeByModel.TryGetValue(m.Name, out long sizeBytes);
			archByModel[m.Name] = await FetchArchInfoAsync(http, ollamaPort, m.Name, sizeBytes, logger, ct);
		}

		// Pinned models: honor the override, but still deduct their real footprint from the
		// shared budget before auto-allocating the rest.
		foreach (ModelGpuRequest m in models.Where(m => m.Pinned is not null))
		{
			result[m.Name] = m.Pinned!.Value;
			ModelArchInfo arch = archByModel[m.Name];
			long used = FootprintBytes(arch, Math.Clamp(m.Pinned.Value, 0, arch.BlockCount), m.NumCtx);
			budgetBytes = Math.Max(0, budgetBytes - used);
			summaryLines.Add(FormatLine(m.Name, m.Pinned.Value, arch.BlockCount, used, pinned: true));
		}

		foreach (ModelGpuRequest m in models.Where(m => m.Pinned is null).OrderBy(m => m.Priority))
		{
			ModelArchInfo arch = archByModel[m.Name];
			long fullFootprint = FootprintBytes(arch, arch.BlockCount, m.NumCtx);

			int layers;
			long used;
			if (fullFootprint <= budgetBytes || arch.BlockCount <= 0)
			{
				layers = arch.BlockCount;
				used = fullFootprint;
			}
			else
			{
				long perLayer = FootprintBytes(arch, 1, m.NumCtx);
				layers = perLayer <= 0 ? 0 : (int)Math.Clamp(budgetBytes / perLayer, 0, arch.BlockCount);
				used = FootprintBytes(arch, layers, m.NumCtx);
			}

			result[m.Name] = layers;
			budgetBytes = Math.Max(0, budgetBytes - used);
			summaryLines.Add(FormatLine(m.Name, layers, arch.BlockCount, used, pinned: false));
		}

		string summary = $"GPU budget: {gpu.FreeMemoryMb} MB free, {initialBudgetBytes / BytesPerMb} MB planned against " +
						  $"(after {(int)((1 - SafetyFactor) * 100)}% safety margin + {ReserveMb} MB reserve):\n"
						  + string.Join('\n', summaryLines);
		return new GpuAllocationPlan { NumGpuLayers = result, Summary = summary };
	}

	private static string FormatLine(string model, int layers, int totalLayers, long usedBytes, bool pinned)
	{
		string tag = pinned ? "pinned" : layers >= totalLayers && totalLayers > 0 ? "full" : "partial";
		return $"  {model}: {layers}/{Math.Max(totalLayers, layers)} layers on GPU (~{usedBytes / BytesPerMb} MB) [{tag}]";
	}

	/// <summary>Weight + KV-cache VRAM cost of putting <paramref name="layers" /> of this model's layers on the GPU.</summary>
	private static long FootprintBytes(ModelArchInfo arch, int layers, int numCtx)
	{
		if (layers <= 0 || arch.BlockCount <= 0)
		{
			return 0;
		}

		long weightBytesPerLayer = arch.SizeBytes / Math.Max(1, arch.BlockCount);
		long kvBytesPerLayer = 2L /* K + V */ * arch.HeadCountKv * arch.HeadDim * numCtx * KvBytesPerElement;
		return layers * (weightBytesPerLayer + kvBytesPerLayer);
	}

	private sealed class ModelArchInfo
	{
		public long SizeBytes { get; init; }
		public int BlockCount { get; init; }
		public int HeadCountKv { get; init; }
		public int HeadDim { get; init; }
	}

	/// <summary>/api/tags reports on-disk size in bytes for every local model — used as a stand-in for VRAM weight cost when fully offloaded.</summary>
	private static async Task<Dictionary<string, long>> TryFetchModelSizesAsync(HttpClient http, int port, CancellationToken ct)
	{
		Dictionary<string, long> sizes = new(StringComparer.OrdinalIgnoreCase);
		try
		{
			using HttpResponseMessage response = await http.GetAsync(new Uri($"http://localhost:{port}/api/tags"), ct);
			if (!response.IsSuccessStatusCode)
			{
				return sizes;
			}

			JsonNode? json = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
			if (json?["models"] is not JsonArray arr)
			{
				return sizes;
			}

			foreach (JsonNode? entry in arr)
			{
				string? name = entry?["name"]?.GetValue<string>();
				if (!string.IsNullOrWhiteSpace(name) && entry?["size"] is JsonNode sizeNode &&
					sizeNode.AsValue().TryGetValue(out long size))
				{
					sizes[name] = size;
				}
			}
		}
		catch
		{
			// Best-effort — callers fall back to a parameter-count heuristic per model below.
		}

		return sizes;
	}

	/// <summary>
	///     /api/show's <c>model_info</c> exposes llama.cpp-style architecture fields
	///     (<c>&lt;arch&gt;.block_count</c>, <c>.attention.head_count[_kv]</c>, <c>.embedding_length</c>).
	///     Field names vary by Ollama version, so this scans loosely for any key ending in the
	///     expected suffix rather than requiring a specific architecture prefix. When model_info is
	///     missing entirely (older Ollama, or the model isn't pulled yet), falls back to a rough
	///     heuristic from <c>details.parameter_size</c> — good enough to avoid wildly over- or
	///     under-committing VRAM, not a substitute for the real metadata.
	/// </summary>
	private static async Task<ModelArchInfo> FetchArchInfoAsync(
		HttpClient http, int port, string model, long knownSizeBytes, ILogger logger, CancellationToken ct)
	{
		try
		{
			using JsonContent request = JsonContent.Create(new JsonObject { ["model"] = model });
			using HttpResponseMessage response =
				await http.PostAsync(new Uri($"http://localhost:{port}/api/show"), request, ct);
			if (!response.IsSuccessStatusCode)
			{
				return FallbackArchInfo(model, knownSizeBytes);
			}

			JsonNode? json = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
			JsonObject? modelInfo = json?["model_info"] as JsonObject;
			long sizeBytes = knownSizeBytes > 0 ? knownSizeBytes : 0;

			int? blockCount = FindSuffixedInt(modelInfo, "block_count");
			int? headCount = FindSuffixedInt(modelInfo, "attention.head_count");
			int? headCountKv = FindSuffixedInt(modelInfo, "attention.head_count_kv") ?? headCount;
			int? embeddingLength = FindSuffixedInt(modelInfo, "embedding_length");

			if (blockCount is null or <= 0 || headCountKv is null or <= 0 || embeddingLength is null or <= 0 || sizeBytes <= 0)
			{
				ModelArchInfo fallback = FallbackArchInfo(model, knownSizeBytes);
				LogGpuPlanningMissingMetadata(logger, model, fallback.BlockCount, fallback.SizeBytes / BytesPerMb);
				return fallback;
			}

			int headDim = headCount is > 0 ? embeddingLength.Value / headCount.Value : 128;
			return new ModelArchInfo
			{
				SizeBytes = sizeBytes,
				BlockCount = blockCount.Value,
				HeadCountKv = headCountKv.Value,
				HeadDim = Math.Max(1, headDim)
			};
		}
		catch (Exception ex)
		{
			LogGpuPlanningCouldntRead(logger, ex, model);
			return FallbackArchInfo(model, knownSizeBytes);
		}
	}

	private static int? FindSuffixedInt(JsonObject? obj, string suffix)
	{
		if (obj is null)
		{
			return null;
		}

		foreach (KeyValuePair<string, JsonNode?> pair in obj)
		{
			if (pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
				pair.Value is JsonValue value && value.TryGetValue(out int intValue))
			{
				return intValue;
			}
		}

		return null;
	}

	/// <summary>
	///     Coarse fallback when Ollama doesn't give us real architecture metadata: assume ~0.7
	///     bytes/param (typical of Q4-ish quantization) if we don't even have a size, and bucket
	///     layer/head counts by rough model scale. This exists so planning degrades gracefully
	///     instead of throwing — accuracy here matters much less than in the metadata-driven path,
	///     since it only kicks in when Ollama itself won't tell us more.
	/// </summary>
	private static ModelArchInfo FallbackArchInfo(string model, long knownSizeBytes)
	{
		long sizeBytes = knownSizeBytes > 0 ? knownSizeBytes : EstimateSizeFromName(model);
		double approxParamsB = sizeBytes / (0.7 * 1_000_000_000);
		(int blocks, int headsKv, int headDim) = approxParamsB switch
		{
			<= 1.5 => (24, 4, 64),
			<= 4 => (32, 8, 96),
			<= 9 => (32, 8, 128),
			<= 15 => (40, 8, 128),
			_ => (48, 8, 128)
		};

		return new ModelArchInfo { SizeBytes = sizeBytes, BlockCount = blocks, HeadCountKv = headsKv, HeadDim = headDim };
	}

	private static long EstimateSizeFromName(string model)
	{
		// Last-resort guess when we have neither /api/tags size nor /api/show metadata (e.g. the
		// model hasn't been pulled/created yet). Matches "0.5b", "3b", "7B", etc. in the name.
		System.Text.RegularExpressions.Match match =
			System.Text.RegularExpressions.Regex.Match(model, @"(\d+(?:\.\d+)?)\s*b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		double paramsB = match.Success && double.TryParse(match.Groups[1].Value, out double parsed) ? parsed : 3;
		return (long)(paramsB * 1_000_000_000 * 0.7);
	}
}

/// <summary>Result of <see cref="GpuBudgetPlanner.PlanAsync" />: per-model GPU layer counts plus a human-readable breakdown for logging.</summary>
public sealed class GpuAllocationPlan : IGpuAllocationPlan
{
	public static GpuAllocationPlan Empty { get; } = new();
	public IReadOnlyDictionary<string, int> NumGpuLayers { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
	public string Summary { get; init; } = "";

	public int? TryGetLayers(string model)
	{
		return NumGpuLayers.TryGetValue(model, out int layers) ? layers : null;
	}
}
