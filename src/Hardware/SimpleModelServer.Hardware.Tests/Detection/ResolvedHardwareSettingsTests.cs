using Moq;
using SimpleModelServer.Hardware;
using Xunit;

namespace SimpleModelServer.Hardware.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IResolvedHardwareSettings"/> interface.
/// Validates hardware configuration resolution including GPU and CPU settings.
/// </summary>
public class ResolvedHardwareSettingsTests
{
    /// <summary>
    /// Contract verification: IResolvedHardwareSettings has all required members.
    /// </summary>
    [Fact]
    public void IResolvedHardwareSettings_Interface_HasRequiredMembers()
    {
        // Arrange
        IResolvedHardwareSettings settings = CreateTestHardwareSettings();

        // Act & Assert - All properties must be readable without throwing
        _ = settings.GpuLayers;
        _ = settings.CpuThreads;
        _ = settings.Gpu;
        _ = settings.MaxConcurrentSummaryRequests;
    }

    [Fact]
    public void ResolvedHardwareSettings_CanHaveGpuLayersSet()
    {
        // Arrange & Act
        IResolvedHardwareSettings settings = CreateTestHardwareSettings(gpuLayers: 48);

        // Assert
        Assert.Equal(48, settings.GpuLayers);
    }

    [Fact]
    public void ResolvedHardwareSettings_GpuLayers_CanBeNull()
    {
        // Arrange & Act
        IResolvedHardwareSettings settings = CreateTestHardwareSettings(gpuLayers: null);

        // Assert
        Assert.Null(settings.GpuLayers);
    }

    [Fact]
    public void ResolvedHardwareSettings_CanHaveCpuThreadsSet()
    {
        // Arrange & Act
        IResolvedHardwareSettings settings = CreateTestHardwareSettings(cpuThreads: 8);

        // Assert
        Assert.Equal(8, settings.CpuThreads);
    }

    [Fact]
    public void ResolvedHardwareSettings_CpuThreads_CanBeNull()
    {
        // Arrange & Act
        IResolvedHardwareSettings settings = CreateTestHardwareSettings(cpuThreads: null);

        // Assert
        Assert.Null(settings.CpuThreads);
    }

    [Fact]
    public void ResolvedHardwareSettings_Gpu_IsMandatory()
    {
        // Arrange
        var gpuInfoMock = new Mock<IHardwareGpuInfo>();
        gpuInfoMock.Setup(g => g.Detected).Returns(true);
        gpuInfoMock.Setup(g => g.Name).Returns("Test GPU");

        // Act
        IResolvedHardwareSettings settings = CreateTestHardwareSettings(gpu: gpuInfoMock.Object);

        // Assert
        Assert.NotNull(settings.Gpu);
        Assert.True(settings.Gpu.Detected);
        Assert.Equal("Test GPU", settings.Gpu.Name);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void ResolvedHardwareSettings_MaxConcurrentSummaryRequests_CanBeSet(int maxRequests)
    {
        // Arrange & Act
        IResolvedHardwareSettings settings = CreateTestHardwareSettings(maxConcurrentRequests: maxRequests);

        // Assert
        Assert.Equal(maxRequests, settings.MaxConcurrentSummaryRequests);
    }

    [Fact]
    public void ResolvedHardwareSettings_AllPropertiesCanBeConfigured()
    {
        // Arrange
        var gpuInfoMock = new Mock<IHardwareGpuInfo>();
        gpuInfoMock.Setup(g => g.Detected).Returns(true);
        gpuInfoMock.Setup(g => g.Name).Returns("NVIDIA RTX 3080");
        gpuInfoMock.Setup(g => g.MemoryMb).Returns(10240);
        gpuInfoMock.Setup(g => g.FreeMemoryMb).Returns(8192);

        // Act
        IResolvedHardwareSettings settings = CreateTestHardwareSettings(
            gpuLayers: 32,
            cpuThreads: 16,
            gpu: gpuInfoMock.Object,
            maxConcurrentRequests: 8
        );

        // Assert
        Assert.Equal(32, settings.GpuLayers);
        Assert.Equal(16, settings.CpuThreads);
        Assert.NotNull(settings.Gpu);
        Assert.True(settings.Gpu.Detected);
        Assert.Equal("NVIDIA RTX 3080", settings.Gpu.Name);
        Assert.Equal(8, settings.MaxConcurrentSummaryRequests);
    }

    [Fact]
    public void ResolvedHardwareSettings_WithUndetectedGpu()
    {
        // Arrange
        var gpuInfoMock = new Mock<IHardwareGpuInfo>();
        gpuInfoMock.Setup(g => g.Detected).Returns(false);
        gpuInfoMock.Setup(g => g.Name).Returns("");
        gpuInfoMock.Setup(g => g.MemoryMb).Returns((int?)null);
        gpuInfoMock.Setup(g => g.FreeMemoryMb).Returns((int?)null);

        // Act
        IResolvedHardwareSettings settings = CreateTestHardwareSettings(
            gpuLayers: null,
            gpu: gpuInfoMock.Object
        );

        // Assert
        Assert.Null(settings.GpuLayers);
        Assert.NotNull(settings.Gpu);
        Assert.False(settings.Gpu.Detected);
    }

    [Fact]
    public void ResolvedHardwareSettings_ZeroCpuThreads_IsValid()
    {
        // Arrange & Act
        IResolvedHardwareSettings settings = CreateTestHardwareSettings(cpuThreads: 0);

        // Assert
        Assert.Equal(0, settings.CpuThreads);
    }

    [Fact]
    public void ResolvedHardwareSettings_HighGpuLayerCount_IsValid()
    {
        // Arrange & Act
        IResolvedHardwareSettings settings = CreateTestHardwareSettings(gpuLayers: 200);

        // Assert
        Assert.Equal(200, settings.GpuLayers);
    }

    // Helper to create test implementations
    private static IResolvedHardwareSettings CreateTestHardwareSettings(
        int? gpuLayers = 32,
        int? cpuThreads = 8,
        IHardwareGpuInfo? gpu = null,
        int maxConcurrentRequests = 4)
    {
        gpu ??= CreateDefaultGpuInfo();

        return new TestResolvedHardwareSettings
        {
            GpuLayers = gpuLayers,
            CpuThreads = cpuThreads,
            Gpu = gpu,
            MaxConcurrentSummaryRequests = maxConcurrentRequests
        };
    }

    private static IHardwareGpuInfo CreateDefaultGpuInfo()
    {
        var mock = new Mock<IHardwareGpuInfo>();
        mock.Setup(g => g.Detected).Returns(true);
        mock.Setup(g => g.Name).Returns("Test GPU");
        mock.Setup(g => g.MemoryMb).Returns(8192);
        mock.Setup(g => g.FreeMemoryMb).Returns(4096);
        return mock.Object;
    }

    private class TestResolvedHardwareSettings : IResolvedHardwareSettings
    {
        public required int? GpuLayers { get; init; }
        public required int? CpuThreads { get; init; }
        public required IHardwareGpuInfo Gpu { get; init; }
        public required int MaxConcurrentSummaryRequests { get; init; }
    }
}
