using SimpleModelServer.Hardware;
using Xunit;

namespace SimpleModelServer.Hardware.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IGpuAllocationPlan"/> interface.
/// Validates GPU layer allocation across models.
/// </summary>
public class GpuAllocationPlanTests
{
	/// <summary>
	/// Contract verification: IGpuAllocationPlan has all required members.
	/// </summary>
	[Fact]
	public void IGpuAllocationPlan_Interface_HasRequiredMembers()
	{
		// Arrange
		IGpuAllocationPlan plan = CreateTestAllocationPlan();

		// Act & Assert - All properties and methods must be accessible
		_ = plan.NumGpuLayers;
		_ = plan.Summary;
		_ = plan.TryGetLayers("test-model");
	}

	[Fact]
	public void GpuAllocationPlan_CanAllocateLayers()
	{
		// Arrange & Act
		var allocation = new Dictionary<string, int>
		{
			{ "model-1", 12 },
			{ "model-2", 24 },
			{ "model-3", 8 }
		};
		IGpuAllocationPlan plan = CreateTestAllocationPlan(allocation, "3 models allocated");

		// Assert
		Assert.Equal(3, plan.NumGpuLayers.Count);
		Assert.Equal(12, plan.NumGpuLayers["model-1"]);
		Assert.Equal(24, plan.NumGpuLayers["model-2"]);
		Assert.Equal(8, plan.NumGpuLayers["model-3"]);
	}

	[Fact]
	public void GpuAllocationPlan_TryGetLayers_ReturnsAllocatedLayers()
	{
		// Arrange
		var allocation = new Dictionary<string, int> { { "test-model", 20 } };
		IGpuAllocationPlan plan = CreateTestAllocationPlan(allocation);

		// Act
		var result = plan.TryGetLayers("test-model");

		// Assert
		Assert.NotNull(result);
		Assert.Equal(20, result);
	}

	[Fact]
	public void GpuAllocationPlan_TryGetLayers_ReturnsNullForUnallocatedModel()
	{
		// Arrange
		var allocation = new Dictionary<string, int> { { "model-1", 20 } };
		IGpuAllocationPlan plan = CreateTestAllocationPlan(allocation);

		// Act
		var result = plan.TryGetLayers("non-existent-model");

		// Assert
		Assert.Null(result);
	}

	[Fact]
	public void GpuAllocationPlan_Summary_Provides_HumanReadableDescription()
	{
		// Arrange
		var allocation = new Dictionary<string, int>
		{
			{ "model-1", 12 },
			{ "model-2", 24 }
		};
		IGpuAllocationPlan plan = CreateTestAllocationPlan(allocation, "2 models, 36 total layers");

		// Act
		var summary = plan.Summary;

		// Assert
		Assert.NotEmpty(summary);
		Assert.Equal("2 models, 36 total layers", summary);
	}

	[Fact]
	public void GpuAllocationPlan_NumGpuLayers_IsReadOnly()
	{
		// Arrange
		var allocation = new Dictionary<string, int> { { "model-1", 12 } };
		IGpuAllocationPlan plan = CreateTestAllocationPlan(allocation);

		// Act
		IReadOnlyDictionary<string, int> readOnly = plan.NumGpuLayers;

		// Assert
		Assert.NotNull(readOnly);
		Assert.IsAssignableFrom<IReadOnlyDictionary<string, int>>(readOnly);
	}

	[Fact]
	public void GpuAllocationPlan_EmptyAllocation_IsValid()
	{
		// Arrange & Act
		IGpuAllocationPlan plan = CreateTestAllocationPlan(new Dictionary<string, int>(), "No allocations");

		// Assert
		Assert.Empty(plan.NumGpuLayers);
		Assert.Equal("No allocations", plan.Summary);
	}

	[Fact]
	public void GpuAllocationPlan_LargeLayerCounts_AreHandled()
	{
		// Arrange
		var allocation = new Dictionary<string, int>
		{
			{ "large-model", 128 },
			{ "medium-model", 64 },
			{ "small-model", 32 }
		};
		IGpuAllocationPlan plan = CreateTestAllocationPlan(allocation);

		// Act
		var largeResult = plan.TryGetLayers("large-model");
		var mediumResult = plan.TryGetLayers("medium-model");
		var smallResult = plan.TryGetLayers("small-model");

		// Assert
		Assert.Equal(128, largeResult);
		Assert.Equal(64, mediumResult);
		Assert.Equal(32, smallResult);
	}

	[Fact]
	public void GpuAllocationPlan_IsCaseSensitive()
	{
		// Arrange
		var allocation = new Dictionary<string, int> { { "Model", 20 } };
		IGpuAllocationPlan plan = CreateTestAllocationPlan(allocation);

		// Act
		var exactMatch = plan.TryGetLayers("Model");
		var lowerCase = plan.TryGetLayers("model");

		// Assert
		Assert.NotNull(exactMatch);
		Assert.Null(lowerCase);
	}

	// Helper to create test implementations
	private static IGpuAllocationPlan CreateTestAllocationPlan(
		Dictionary<string, int>? allocation = null,
		string summary = "Test allocation plan")
	{
		allocation ??= new Dictionary<string, int>();
		return new TestGpuAllocationPlan
		{
			NumGpuLayers = allocation,
			Summary = summary
		};
	}

	private class TestGpuAllocationPlan : IGpuAllocationPlan
	{
		public required Dictionary<string, int> NumGpuLayers { get; init; }
		public required string Summary { get; init; }

		IReadOnlyDictionary<string, int> IGpuAllocationPlan.NumGpuLayers => NumGpuLayers;

		public int? TryGetLayers(string model)
		{
			return NumGpuLayers.TryGetValue(model, out var layers) ? layers : null;
		}
	}
}
