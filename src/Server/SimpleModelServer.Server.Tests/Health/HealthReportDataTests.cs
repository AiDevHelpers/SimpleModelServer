using Moq;
using SimpleModelServer.Contracts.Dtos;
using SimpleModelServer.Server;
using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IHealthReportData"/> interface.
/// Validates health status reporting, component information, and timestamps.
/// </summary>
public class HealthReportDataTests
{
	/// <summary>
	/// Contract verification: IHealthReportData has all required members.
	/// </summary>
	[Fact]
	public void IHealthReportData_Interface_HasRequiredMembers()
	{
		// Arrange
		IHealthReportData healthReport = CreateTestHealthReport();

		// Act & Assert - All properties and methods must be accessible
		_ = healthReport.Status;
		_ = healthReport.Message;
		_ = healthReport.ConfigurationId;
		_ = healthReport.Models;
		_ = healthReport.Gpu;
		_ = healthReport.RecentFault;
		_ = healthReport.Timestamp;
		_ = healthReport.GetHealthSummary();
	}

	[Theory]
	[InlineData("healthy")]
	[InlineData("degraded")]
	[InlineData("unhealthy")]
	public void HealthReportData_Status_CanHaveDifferentValues(string status)
	{
		// Arrange & Act
		IHealthReportData report = CreateTestHealthReport(status: status);

		// Assert
		Assert.Equal(status, report.Status);
	}

	[Fact]
	public void HealthReportData_Message_CanBeNull()
	{
		// Arrange & Act
		IHealthReportData report = CreateTestHealthReport(message: null);

		// Assert
		Assert.Null(report.Message);
	}

	[Theory]
	[InlineData("System is operating normally")]
	[InlineData("GPU memory is running low")]
	[InlineData("Ollama service is unresponsive")]
	public void HealthReportData_Message_CanHaveDescriptiveText(string message)
	{
		// Arrange & Act
		IHealthReportData report = CreateTestHealthReport(message: message);

		// Assert
		Assert.Equal(message, report.Message);
	}

	[Fact]
	public void HealthReportData_ConfigurationId_CanBeNull()
	{
		// Arrange & Act
		IHealthReportData report = CreateTestHealthReport(configurationId: null);

		// Assert
		Assert.Null(report.ConfigurationId);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(42)]
	[InlineData(int.MaxValue)]
	public void HealthReportData_ConfigurationId_CanHaveValues(int configId)
	{
		// Arrange & Act
		IHealthReportData report = CreateTestHealthReport(configurationId: configId);

		// Assert
		Assert.Equal(configId, report.ConfigurationId);
	}

	[Fact]
	public void HealthReportData_Models_CanBeNull()
	{
		// Arrange & Act
		IHealthReportData report = CreateTestHealthReport(models: null);

		// Assert
		Assert.Null(report.Models);
	}

	[Fact]
	public void HealthReportData_Models_CanContainModelInfo()
	{
		// Arrange
		var modelsMock = new Mock<ILoadedModelsInfo>();
		modelsMock.Setup(m => m.MainModelName).Returns("model-1");
		modelsMock.Setup(m => m.DraftModelName).Returns("model-2");

		// Act
		IHealthReportData report = CreateTestHealthReport(models: modelsMock.Object);

		// Assert
		Assert.NotNull(report.Models);
		Assert.Equal("model-1", report.Models.MainModelName);
		Assert.Equal("model-2", report.Models.DraftModelName);
	}

	[Fact]
	public void HealthReportData_Gpu_CanBeNull()
	{
		// Arrange & Act
		IHealthReportData report = CreateTestHealthReport(gpu: null);

		// Assert
		Assert.Null(report.Gpu);
	}

	[Fact]
	public void HealthReportData_Gpu_CanContainGpuInfo()
	{
		// Arrange
		var gpuMock = new Mock<IGpuInfoReport>();

		// Act
		IHealthReportData report = CreateTestHealthReport(gpu: gpuMock.Object);

		// Assert
		Assert.NotNull(report.Gpu);
	}

	[Fact]
	public void HealthReportData_RecentFault_CanBeNull()
	{
		// Arrange & Act
		IHealthReportData report = CreateTestHealthReport(recentFault: null);

		// Assert
		Assert.Null(report.RecentFault);
	}

	[Theory]
	[InlineData("Ollama failed to load model")]
	[InlineData("GPU out of memory")]
	[InlineData("Connection timeout")]
	public void HealthReportData_RecentFault_CanHaveErrorMessage(string faultMessage)
	{
		// Arrange & Act
		IHealthReportData report = CreateTestHealthReport(recentFault: faultMessage);

		// Assert
		Assert.Equal(faultMessage, report.RecentFault);
	}

	[Fact]
	public void HealthReportData_Timestamp_IsSet()
	{
		// Arrange
		var expectedTime = DateTime.UtcNow;

		// Act
		IHealthReportData report = CreateTestHealthReport(timestamp: expectedTime);

		// Assert
		Assert.Equal(expectedTime, report.Timestamp);
	}

	[Fact]
	public void HealthReportData_GetHealthSummary_ReturnsString()
	{
		// Arrange & Act
		IHealthReportData report = CreateTestHealthReport();
		var summary = report.GetHealthSummary();

		// Assert
		Assert.NotNull(summary);
		Assert.NotEmpty(summary);
	}

	[Fact]
	public void HealthReportData_HealthyStatus_ProducesHealthySummary()
	{
		// Arrange & Act
		IHealthReportData report = CreateTestHealthReport(status: "healthy");
		var summary = report.GetHealthSummary();

		// Assert
		Assert.Contains("healthy", summary.ToLowerInvariant(), StringComparison.Ordinal);
	}

	[Fact]
	public void HealthReportData_DegradedStatus_ProducesDegradedSummary()
	{
		// Arrange & Act
		IHealthReportData report = CreateTestHealthReport(status: "degraded");
		var summary = report.GetHealthSummary();

		// Assert
		Assert.Contains("degraded", summary.ToLowerInvariant(), StringComparison.Ordinal);
	}

	[Fact]
	public void HealthReportData_UnhealthyStatus_ProducesUnhealthySummary()
	{
		// Arrange & Act
		IHealthReportData report = CreateTestHealthReport(status: "unhealthy");
		var summary = report.GetHealthSummary();

		// Assert
		Assert.Contains("unhealthy", summary.ToLowerInvariant(), StringComparison.Ordinal);
	}

	[Fact]
	public void HealthReportData_AllPropertiesCanBeConfigured()
	{
		// Arrange
		var modelsMock = new Mock<ILoadedModelsInfo>();
		var gpuMock = new Mock<IGpuInfoReport>();
		var timestamp = DateTime.UtcNow;

		// Act
		IHealthReportData report = CreateTestHealthReport(
			status: "degraded",
			message: "Running with reduced capacity",
			configurationId: 5,
			models: modelsMock.Object,
			gpu: gpuMock.Object,
			recentFault: "Warning: GPU throttling detected",
			timestamp: timestamp
		);

		// Assert
		Assert.Equal("degraded", report.Status);
		Assert.Equal("Running with reduced capacity", report.Message);
		Assert.Equal(5, report.ConfigurationId);
		Assert.NotNull(report.Models);
		Assert.NotNull(report.Gpu);
		Assert.Equal("Warning: GPU throttling detected", report.RecentFault);
		Assert.Equal(timestamp, report.Timestamp);
	}

	[Fact]
	public void HealthReportData_WithMinimalConfiguration()
	{
		// Arrange & Act
		IHealthReportData report = CreateTestHealthReport(
			status: "unhealthy",
			message: null,
			configurationId: null,
			models: null,
			gpu: null,
			recentFault: null
		);

		// Assert
		Assert.Equal("unhealthy", report.Status);
		Assert.Null(report.Message);
		Assert.Null(report.ConfigurationId);
		Assert.Null(report.Models);
		Assert.Null(report.Gpu);
		Assert.Null(report.RecentFault);
	}

	// Helper to create test implementations
	private static IHealthReportData CreateTestHealthReport(
		string status = "healthy",
		string? message = "System operating normally",
		int? configurationId = 1,
		ILoadedModelsInfo? models = null,
		IGpuInfoReport? gpu = null,
		string? recentFault = null,
		DateTime? timestamp = null)
	{
		return new TestHealthReportData
		{
			Status = status,
			Message = message,
			ConfigurationId = configurationId,
			Models = models,
			Gpu = gpu,
			RecentFault = recentFault,
			Timestamp = timestamp ?? DateTime.UtcNow
		};
	}

	private class TestHealthReportData : IHealthReportData
	{
		public required string Status { get; init; }
		public string? Message { get; init; }
		public int? ConfigurationId { get; init; }
		public ILoadedModelsInfo? Models { get; init; }
		public IGpuInfoReport? Gpu { get; init; }
		public string? RecentFault { get; init; }
		public required DateTime Timestamp { get; init; }

		public string GetHealthSummary()
		{
			return Status switch
			{
				"healthy" => "System is healthy",
				"degraded" => "System is degraded",
				"unhealthy" => "System is unhealthy",
				_ => "Unknown status"
			};
		}
	}
}
