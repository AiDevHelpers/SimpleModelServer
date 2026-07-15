using SimpleModelServer.Server;
using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IGpuInfoReport"/> interface.
/// Validates GPU information properties and their values.
/// </summary>
public class GpuInfoReportTests
{
    private class MockGpuInfoReport : IGpuInfoReport
    {
        public bool Available { get; init; }
        public string? Model { get; init; }
        public double MemoryGb { get; init; }
        public double? MaxModelSizeB { get; init; }
    }

    /// <summary>
    /// Contract verification: IGpuInfoReport has all required members.
    /// </summary>
    [Fact]
    public void IGpuInfoReport_Interface_HasAllRequiredMembers()
    {
        // Arrange
        IGpuInfoReport report = new MockGpuInfoReport();

        // Act & Assert - All properties must be accessible
        _ = report.Available;
        _ = report.Model;
        _ = report.MemoryGb;
        _ = report.MaxModelSizeB;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Available_CanBeSet(bool value)
    {
        // Arrange & Act
        var report = new MockGpuInfoReport { Available = value };

        // Assert
        Assert.Equal(value, report.Available);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("NVIDIA RTX 4090")]
    [InlineData("NVIDIA GeForce RTX 3080")]
    [InlineData("Intel Arc A770")]
    [InlineData("AMD Radeon RX 7900")]
    public void Model_CanBeSet(string? value)
    {
        // Arrange & Act
        var report = new MockGpuInfoReport { Model = value };

        // Assert
        Assert.Equal(value, report.Model);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(4.0)]
    [InlineData(8.0)]
    [InlineData(24.0)]
    [InlineData(48.0)]
    public void MemoryGb_CanBeSet(double value)
    {
        // Arrange & Act
        var report = new MockGpuInfoReport { MemoryGb = value };

        // Assert
        Assert.Equal(value, report.MemoryGb);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(5.0)]
    [InlineData(10.0)]
    public void MaxModelSizeB_CanBeSet(double? value)
    {
        // Arrange & Act
        var report = new MockGpuInfoReport { MaxModelSizeB = value };

        // Assert
        Assert.Equal(value, report.MaxModelSizeB);
    }

    [Fact]
    public void GpuInfoReport_AvailableGpu_HasValidProperties()
    {
        // Arrange & Act
        var report = new MockGpuInfoReport
        {
            Available = true,
            Model = "NVIDIA RTX 4090",
            MemoryGb = 24.0,
            MaxModelSizeB = 20.0
        };

        // Assert
        Assert.True(report.Available);
        Assert.Equal("NVIDIA RTX 4090", report.Model);
        Assert.Equal(24.0, report.MemoryGb);
        Assert.Equal(20.0, report.MaxModelSizeB);
    }

    [Fact]
    public void GpuInfoReport_UnavailableGpu_HasNullModel()
    {
        // Arrange & Act
        var report = new MockGpuInfoReport
        {
            Available = false,
            Model = null,
            MemoryGb = 0.0,
            MaxModelSizeB = null
        };

        // Assert
        Assert.False(report.Available);
        Assert.Null(report.Model);
        Assert.Equal(0.0, report.MemoryGb);
        Assert.Null(report.MaxModelSizeB);
    }

    [Fact]
    public void GpuInfoReport_PartialInfo_IsValid()
    {
        // Arrange & Act - Some properties might be null even if GPU is available
        var report = new MockGpuInfoReport
        {
            Available = true,
            Model = null,
            MemoryGb = 8.0,
            MaxModelSizeB = null
        };

        // Assert
        Assert.True(report.Available);
        Assert.Null(report.Model);
        Assert.Equal(8.0, report.MemoryGb);
        Assert.Null(report.MaxModelSizeB);
    }

    [Fact]
    public void GpuInfoReport_InterfaceContract()
    {
        // Arrange
        IGpuInfoReport report = new MockGpuInfoReport
        {
            Available = true,
            Model = "Test GPU",
            MemoryGb = 16.0,
            MaxModelSizeB = 14.0
        };

        // Act & Assert
        Assert.True(report.Available);
        Assert.Equal("Test GPU", report.Model);
        Assert.Equal(16.0, report.MemoryGb);
        Assert.Equal(14.0, report.MaxModelSizeB);
    }

    [Theory]
    [InlineData(true, "NVIDIA RTX 4090", 24.0, 20.0)]
    [InlineData(false, null, 0.0, null)]
    [InlineData(true, "AMD Radeon", 8.0, 6.0)]
    [InlineData(false, "Intel Arc", 4.0, null)]
    public void GpuInfoReport_VariousCombinations(
        bool available,
        string? model,
        double memoryGb,
        double? maxModelSize)
    {
        // Arrange & Act
        var report = new MockGpuInfoReport
        {
            Available = available,
            Model = model,
            MemoryGb = memoryGb,
            MaxModelSizeB = maxModelSize
        };

        // Assert
        Assert.Equal(available, report.Available);
        Assert.Equal(model, report.Model);
        Assert.Equal(memoryGb, report.MemoryGb);
        Assert.Equal(maxModelSize, report.MaxModelSizeB);
    }

    [Fact]
    public void GpuInfoReport_MemoryGb_SupportsFloatingPoint()
    {
        // Arrange & Act
        var report = new MockGpuInfoReport
        {
            MemoryGb = 23.5,
            MaxModelSizeB = 19.75
        };

        // Assert
        Assert.Equal(23.5, report.MemoryGb);
        Assert.Equal(19.75, report.MaxModelSizeB);
    }
}
