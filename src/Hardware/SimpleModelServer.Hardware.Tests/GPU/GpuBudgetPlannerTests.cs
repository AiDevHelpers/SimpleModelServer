using SimpleModelServer.Hardware;

using Microsoft.Extensions.Logging.Abstractions;

using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using Xunit;
using Xunit.Sdk;

namespace SimpleModelServer.Hardware.Tests;

/// <summary>
///     Covers GpuBudgetPlanner.PlanAsync — the per-model VRAM budgeting logic that decides how
///     many GPU layers each of draft/main/embed gets when they share one GPU. This had zero test
///     coverage before: a bug here silently manifests as VRAM thrashing (each warmup evicting
///     whichever model doesn't fit) rather than a loud failure, which is exactly the kind of bug
///     that's expensive to diagnose in production and cheap to catch here.
///
///     Uses a stub HttpMessageHandler instead of a real Ollama process (mirrors the pattern
///     FakeOllamaServer establishes for the router tests, but PlanAsync only needs /api/tags and
///     /api/show — a full HTTP listener would be unnecessary overhead for what's a pure
///     computation given those two responses).
/// </summary>
public sealed class GpuBudgetPlannerTests
{
	private sealed class StubHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string body = respond(request);
			HttpResponseMessage response = new(HttpStatusCode.OK)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json")
			};
			return Task.FromResult(response);
		}
	}

	/// <summary>
	///     /api/show response with full architecture metadata for a given model — the "happy
	///     path" GpuBudgetPlanner expects when Ollama reports real block_count/head_count/etc.
	/// </summary>
	private static string ShowResponse(int blockCount, int headCountKv, int embeddingLength)
	{
		JsonObject modelInfo = new()
		{
			["some_arch.block_count"] = blockCount,
			["some_arch.attention.head_count"] = headCountKv,
			["some_arch.attention.head_count_kv"] = headCountKv,
			["some_arch.embedding_length"] = embeddingLength
		};
		JsonObject root = new() { ["model_info"] = modelInfo };
		return root.ToJsonString();
	}

	private static HttpClient CreateHttpClient(Dictionary<string, long> sizesByModel, Dictionary<string, (int blocks, int headsKv, int embedLen)> archByModel)
	{
		StubHandler handler = new(request =>
		{
			string path = request.RequestUri!.AbsolutePath;
			if (path == "/api/tags")
			{
				JsonArray models = [];
				foreach ((string name, long size) in sizesByModel)
				{
					models.Add(new JsonObject { ["name"] = name, ["size"] = size });
				}

				return new JsonObject { ["models"] = models }.ToJsonString();
			}

			if (path == "/api/show")
			{
				// Body contains {"model": "<name>"} — extract it to look up the right arch info.
				using StreamReader reader = new(request.Content!.ReadAsStream());
				string bodyText = reader.ReadToEnd();
				JsonNode? bodyNode = JsonNode.Parse(bodyText);
				string model = bodyNode?["model"]?.GetValue<string>() ?? "";
				return archByModel.TryGetValue(model, out (int blocks, int headsKv, int embedLen) arch)
					? ShowResponse(arch.blocks, arch.headsKv, arch.embedLen)
					: "{}";
			}

			return "{}";
		});

		return new HttpClient(handler);
	}

	[Fact]
	public async Task PlanAsync_NoGpuDetected_HonorsExplicitPinsOnly()
	{
		var planner = new GpuBudgetPlanner();
		using HttpClient http = CreateHttpClient([], []);
		List<GpuBudgetPlanner.ModelGpuRequest> requests =
		[
			new GpuBudgetPlanner.ModelGpuRequest("draft", 2048, Pinned: 10, Priority: 0),
			new GpuBudgetPlanner.ModelGpuRequest("main", 4096, Pinned: null, Priority: 1)
		];

		GpuAllocationPlan plan = await planner.PlanAsync(http, 11434, GpuInfo.None, requests, NullLogger.Instance, TestContext.Current.CancellationToken);

		Assert.Equal(10, plan.TryGetLayers("draft"));
		Assert.Null(plan.TryGetLayers("main"));
		Assert.Contains("No GPU detected", plan.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public async Task PlanAsync_GpuDetectedButFreeMemoryUnknown_SkipsAutoPlanning()
	{
		var planner = new GpuBudgetPlanner();
		using HttpClient http = CreateHttpClient([], []);
		GpuInfo gpu = new() { Detected = true, Name = "Fake GPU", MemoryMb = 8192, FreeMemoryMb = null };
		List<GpuBudgetPlanner.ModelGpuRequest> requests = [new GpuBudgetPlanner.ModelGpuRequest("draft", 2048, null, 0)];

		GpuAllocationPlan plan = await planner.PlanAsync(http, 11434, gpu, requests, NullLogger.Instance, TestContext.Current.CancellationToken);

		Assert.Null(plan.TryGetLayers("draft"));
		Assert.Contains("free VRAM unknown", plan.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public async Task PlanAsync_AmpleVram_AllModelsGetFullLayerCount()
	{
		var planner = new GpuBudgetPlanner();
		Dictionary<string, long> sizes = new() { ["draft"] = 500_000_000, ["main"] = 2_000_000_000 };
		Dictionary<string, (int, int, int)> arch = new()
		{
			["draft"] = (24, 4, 256),
			["main"] = (32, 8, 512)
		};
		using HttpClient http = CreateHttpClient(sizes, arch);
		GpuInfo gpu = new() { Detected = true, Name = "Fake GPU", MemoryMb = 24_000, FreeMemoryMb = 24_000 };
		List<GpuBudgetPlanner.ModelGpuRequest> requests =
		[
			new GpuBudgetPlanner.ModelGpuRequest("draft", 2048, null, Priority: 0),
			new GpuBudgetPlanner.ModelGpuRequest("main", 4096, null, Priority: 1)
		];

		GpuAllocationPlan plan = await planner.PlanAsync(http, 11434, gpu, requests, NullLogger.Instance, TestContext.Current.CancellationToken);

		Assert.Equal(24, plan.TryGetLayers("draft"));
		Assert.Equal(32, plan.TryGetLayers("main"));
	}

	[Fact]
	public async Task PlanAsync_TightVram_HigherPriorityModelGetsFullLayersFirst()
	{
		var planner = new GpuBudgetPlanner();
		// Both models have identical footprint-per-layer; budget is deliberately sized to fit
		// priority-0 (draft) in full but force priority-1 (main) into partial offload — this is
		// the actual scenario PlanGpuLayersAsync's priority ordering exists for (draft must stay
		// resident since it's latency-critical; main can degrade to partial/CPU).
		Dictionary<string, long> sizes = new() { ["draft"] = 1_000_000_000, ["main"] = 1_000_000_000 };
		Dictionary<string, (int, int, int)> arch = new()
		{
			["draft"] = (20, 4, 256),
			["main"] = (20, 4, 256)
		};
		using HttpClient http = CreateHttpClient(sizes, arch);

		// Small context so KV cache is negligible vs weights — makes the per-layer footprint
		// predictable (~50 MB/layer, ~1 GB for the full 20). 2300 MB free nets a budget
		// (after the 85% safety factor and 512 MB reserve) of ~1.5x that footprint: enough for
		// draft's full 20 layers plus a partial allocation for main, but not enough for both in
		// full — verified below.
		GpuInfo gpu = new() { Detected = true, Name = "Fake GPU", MemoryMb = 2300, FreeMemoryMb = 2300 };
		List<GpuBudgetPlanner.ModelGpuRequest> requests =
		[
			new GpuBudgetPlanner.ModelGpuRequest("draft", 128, null, Priority: 0),
			new GpuBudgetPlanner.ModelGpuRequest("main", 128, null, Priority: 1)
		];

		GpuAllocationPlan plan = await planner.PlanAsync(http, 11434, gpu, requests, NullLogger.Instance, TestContext.Current.CancellationToken);

		int? draftLayers = plan.TryGetLayers("draft");
		int? mainLayers = plan.TryGetLayers("main");
		Assert.Equal(20, draftLayers);
		Assert.NotNull(mainLayers);
		Assert.True(mainLayers < 20, $"Expected main to be partially offloaded, got {mainLayers}/20 layers.");
	}

	[Fact]
	public async Task PlanAsync_PinnedModel_FootprintIsDeductedFromSharedBudget()
	{
		var planner = new GpuBudgetPlanner();
		// A pinned model still consumes real budget — verified indirectly: give main a large
		// pinned layer count that eats almost the whole budget, and confirm draft (unpinned,
		// higher priority) still only gets what's left rather than assuming the pin is "free".
		Dictionary<string, long> sizes = new() { ["draft"] = 1_000_000_000, ["main"] = 1_000_000_000 };
		Dictionary<string, (int, int, int)> arch = new()
		{
			["draft"] = (20, 4, 256),
			["main"] = (20, 4, 256)
		};
		using HttpClient http = CreateHttpClient(sizes, arch);
		GpuInfo gpu = new() { Detected = true, Name = "Fake GPU", MemoryMb = 1500, FreeMemoryMb = 1500 };
		List<GpuBudgetPlanner.ModelGpuRequest> requests =
		[
			new GpuBudgetPlanner.ModelGpuRequest("draft", 128, null, Priority: 0),
			new GpuBudgetPlanner.ModelGpuRequest("main", 128, Pinned: 20, Priority: 1)
		];

		GpuAllocationPlan plan = await planner.PlanAsync(http, 11434, gpu, requests, NullLogger.Instance, TestContext.Current.CancellationToken);

		Assert.Equal(20, plan.TryGetLayers("main"));
		int? draftLayers = plan.TryGetLayers("draft");
		Assert.NotNull(draftLayers);
		Assert.True(draftLayers < 20, $"Expected draft to be constrained by main's pinned footprint, got {draftLayers}/20 layers.");
	}

	[Fact]
	public async Task PlanAsync_MissingArchitectureMetadata_FallsBackWithoutThrowing()
	{
		var planner = new GpuBudgetPlanner();
		// No /api/show data for either model — GpuBudgetPlanner must degrade to its heuristic
		// fallback rather than crash.
		using HttpClient http = CreateHttpClient([], []);
		GpuInfo gpu = new() { Detected = true, Name = "Fake GPU", MemoryMb = 8192, FreeMemoryMb = 8192 };
		List<GpuBudgetPlanner.ModelGpuRequest> requests = [new GpuBudgetPlanner.ModelGpuRequest("qwen2.5-coder-3b-chat", 4096, null, 0)];

		GpuAllocationPlan plan = await planner.PlanAsync(http, 11434, gpu, requests, NullLogger.Instance, TestContext.Current.CancellationToken);

		Assert.NotNull(plan.TryGetLayers("qwen2.5-coder-3b-chat"));
		Assert.True(plan.TryGetLayers("qwen2.5-coder-3b-chat") > 0);
	}

	[Fact]
	public void TryGetLayers_UnknownModel_ReturnsNull()
	{
		Assert.Null(GpuAllocationPlan.Empty.TryGetLayers("anything"));
	}
}
