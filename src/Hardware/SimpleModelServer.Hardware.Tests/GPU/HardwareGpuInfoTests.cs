using SimpleModelServer.Hardware;
using Xunit;

namespace SimpleModelServer.Hardware.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IHardwareGpuInfo"/> interface.
/// Validates GPU detection and memory reporting.
/// </summary>
public class HardwareGpuInfoTests
{
    /// <summary>
    /// Contract verification: IHardwareGpuInfo has all required members.
    /// </summary>
    [Fact]
    public void IHardwareGpuInfo_Interface_HasRequiredMembers()
    {
        // Arrange
        IHardwareGpuInfo gpu = CreateTestGpuInfo();

        // Act & Assert - All properties must be readable without throwing
        _ = gpu.Detected;
        _ = gpu.Name;
        _ = gpu.MemoryMb;
        _ = gpu.FreeMemoryMb;
    }

    [Fact]
    public void HardwareGpuInfo_CanRepresentDetectedGpu()
    {
        // Arrange & Act
        IHardwareGpuInfo gpu = CreateTestGpuInfo(
            detected: true,
            name: "NVIDIA RTX 3080",
            memoryMb: 10240,
            freeMemoryMb: 8192
        );

        // Assert
        Assert.True(gpu.Detected);
        Assert.Equal("NVIDIA RTX 3080", gpu.Name);
        Assert.Equal(10240, gpu.MemoryMb);
        Assert.Equal(8192, gpu.FreeMemoryMb);
    }

    [Fact]
    public void HardwareGpuInfo_CanRepresentUndetectedGpu()
    {
        // Arrange & Act
        IHardwareGpuInfo gpu = CreateTestGpuInfo(detected: false);

        // Assert
        Assert.False(gpu.Detected);
    }

    [Fact]
    public void HardwareGpuInfo_MemoryMb_CanBeNull()
    {
        // Arrange & Act
        IHardwareGpuInfo gpu = CreateTestGpuInfo(memoryMb: null);

        // Assert
        Assert.Null(gpu.MemoryMb);
    }

    [Fact]
    public void HardwareGpuInfo_FreeMemoryMb_CanBeNull()
    {
        // Arrange & Act
        IHardwareGpuInfo gpu = CreateTestGpuInfo(freeMemoryMb: null);

        // Assert
        Assert.Null(gpu.FreeMemoryMb);
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(16384)]
    [InlineData(49152)]
    public void HardwareGpuInfo_MemoryMb_AcceptsVariousSizes(int memoryMb)
    {
        // Arrange & Act
        IHardwareGpuInfo gpu = CreateTestGpuInfo(memoryMb: memoryMb);

        // Assert
        Assert.Equal(memoryMb, gpu.MemoryMb);
    }

    [Theory]
    [InlineData("NVIDIA RTX 3080")]
    [InlineData("NVIDIA RTX 4090")]
    [InlineData("AMD Radeon RX 6900 XT")]
    [InlineData("Intel Arc A770")]
    public void HardwareGpuInfo_Name_AcceptsGpuNames(string gpuName)
    {
        // Arrange & Act
        IHardwareGpuInfo gpu = CreateTestGpuInfo(name: gpuName);

        // Assert
        Assert.Equal(gpuName, gpu.Name);
    }

    [Fact]
    public void HardwareGpuInfo_FreeMemory_LessThanOrEqualToTotal()
    {
        // Arrange & Act
        IHardwareGpuInfo gpu = CreateTestGpuInfo(
            memoryMb: 10240,
            freeMemoryMb: 8192
        );

        // Assert
        Assert.True(gpu.FreeMemoryMb <= gpu.MemoryMb);
    }

    [Fact]
    public void HardwareGpuInfo_CanHaveBothMemoryValuesNull()
    {
        // Arrange & Act
        IHardwareGpuInfo gpu = CreateTestGpuInfo(
            memoryMb: null,
            freeMemoryMb: null
        );

        // Assert
        Assert.Null(gpu.MemoryMb);
        Assert.Null(gpu.FreeMemoryMb);
    }

    // Helper to create test implementations
    private static IHardwareGpuInfo CreateTestGpuInfo(
        bool detected = true,
        string name = "Test GPU",
        int? memoryMb = 8192,
        int? freeMemoryMb = 4096)
    {
        return new TestHardwareGpuInfo
        {
            Detected = detected,
            Name = name,
            MemoryMb = memoryMb,
            FreeMemoryMb = freeMemoryMb
        };
    }

    private class TestHardwareGpuInfo : IHardwareGpuInfo
    {
        public bool Detected { get; init; }
        public string Name { get; init; } = "";
        public int? MemoryMb { get; init; }
        public int? FreeMemoryMb { get; init; }
    }
}
