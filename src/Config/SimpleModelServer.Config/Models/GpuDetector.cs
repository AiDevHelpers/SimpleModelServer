using SimpleModelServer.Contracts.Models;

namespace SimpleModelServer.Config.Models;

using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
///     Detects GPU capabilities and memory available on the system.
/// </summary>
public static class GpuDetector
{
    /// <summary>
    ///     Detects GPU information for the current system.
    ///     Checks for NVIDIA CUDA, Apple Metal, or falls back to CPU-only detection.
    /// </summary>
    public static GpuInfo DetectGpu()
    {
        // Try NVIDIA CUDA first (most common for Linux/Windows)
        var cudaGpu = DetectNvidiaGpu();
        if (cudaGpu.IsAvailable)
        {
            return cudaGpu;
        }

        // Try Apple Metal (macOS with Apple Silicon)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var metalGpu = DetectAppleMetal();
            if (metalGpu.IsAvailable)
            {
                return metalGpu;
            }
        }

        // Fallback: CPU-only
        return DetectCpuOnly();
    }

    private static GpuInfo DetectNvidiaGpu()
    {
        try
        {
            // Try to run nvidia-smi to detect NVIDIA GPU
            var processInfo = new ProcessStartInfo
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "nvidia-smi.exe" : "nvidia-smi",
                Arguments = "--query-gpu=memory.total,name --format=csv,noheader",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                return new GpuInfo { IsAvailable = false };
            }

            using var reader = process.StandardOutput;
            var output = reader.ReadToEnd();
            process.WaitForExit();

            if (string.IsNullOrWhiteSpace(output))
            {
                return new GpuInfo { IsAvailable = false };
            }

            // Parse output: "memory_total, gpu_name"
            var parts = output.Trim().Split(',');
            if (parts.Length >= 2)
            {
                var memoryStr = parts[0].Trim();
                var gpuName = parts[1].Trim();

                // Parse memory (usually in MB)
                if (double.TryParse(memoryStr.Split()[0], out var memoryMb))
                {
                    var memoryGb = memoryMb / 1024.0;
                    var maxModelSize = EstimateMaxModelSize(memoryGb);

                    return new GpuInfo
                    {
                        IsAvailable = true,
                        Model = gpuName,
                        MemoryGb = memoryGb,
                        MaxModelSizeB = maxModelSize,
                    };
                }
            }
        }
        catch
        {
            // nvidia-smi not found or execution failed
        }

        return new GpuInfo { IsAvailable = false };
    }

    private static GpuInfo DetectAppleMetal()
    {
        try
        {
            // Use system_profiler to get Apple Metal GPU info
            var processInfo = new ProcessStartInfo
            {
                FileName = "/usr/sbin/system_profiler",
                Arguments = "SPDisplaysDataType",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                return new GpuInfo { IsAvailable = false };
            }

            using var reader = process.StandardOutput;
            var output = reader.ReadToEnd();
            process.WaitForExit();

            if (output.Contains("Apple", StringComparison.OrdinalIgnoreCase))
            {
                // Try to get memory from vm_stat (unified memory on Apple Silicon)
                var memoryGb = GetAppleSiliconMemory();
                var maxModelSize = EstimateMaxModelSize(memoryGb);

                return new GpuInfo
                {
                    IsAvailable = true,
                    Model = "Apple Metal",
                    MemoryGb = memoryGb,
                    MaxModelSizeB = maxModelSize,
                };
            }
        }
        catch
        {
            // system_profiler not available
        }

        return new GpuInfo { IsAvailable = false };
    }

    private static double GetAppleSiliconMemory()
    {
        try
        {
            // Get unified memory size on Apple Silicon
            var processInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/vm_stat",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                return 8.0; // Default fallback
            }

            using var reader = process.StandardOutput;
            var output = reader.ReadToEnd();
            process.WaitForExit();

            // Try to parse physical memory (pages are typically 4KB)
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("Pages free:", StringComparison.Ordinal) && long.TryParse(line.Split(':')[1].Trim().Split()[0], out var freePages))
                {
                    // Total memory is harder to get, estimate based on common configs
                    // Most Apple Silicon has 8GB, 16GB, or 24GB unified memory
                    return 16.0; // Conservative estimate
                }
            }
        }
        catch
        {
            // Fallback
        }

        return 16.0; // Default for Apple Silicon
    }

    private static GpuInfo DetectCpuOnly()
    {
        // Estimate CPU memory available for models
        var totalMemory = GC.GetTotalMemory(false);
        var availableGb = (totalMemory / (1024.0 * 1024.0 * 1024.0)) * 0.7; // Use ~70% of available

        var maxModelSize = EstimateMaxModelSize(availableGb);

        return new GpuInfo
        {
            IsAvailable = false,
            Model = "CPU Only",
            MemoryGb = availableGb,
            MaxModelSizeB = maxModelSize,
        };
    }

    /// <summary>
    ///     Estimates the maximum model size (in billions of parameters) that can fit in available memory.
    ///     Uses rough estimation: roughly 2 bytes per parameter for inference (FP16) + overhead.
    /// </summary>
    private static double EstimateMaxModelSize(double memoryGb)
    {
        if (memoryGb < 1)
        {
            return 0.5; // Minimum viable
        }

        // Rough estimation: 2 bytes per parameter in FP16, plus 30% overhead for activations
        // 1B params ≈ 2GB for inference
        var estimatedModelSize = (memoryGb / 2.6) * 0.9; // Conservative estimate

        // Round to common model sizes
        if (estimatedModelSize < 0.5)
            return 0.5;
        if (estimatedModelSize < 1.5)
            return 1.5;
        if (estimatedModelSize < 3)
            return 3;
        if (estimatedModelSize < 7)
            return 7;
        if (estimatedModelSize < 13)
            return 13;

        return Math.Floor(estimatedModelSize);
    }
}


