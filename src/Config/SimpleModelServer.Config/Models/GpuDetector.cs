using SimpleModelServer.Contracts.Models;

namespace SimpleModelServer.Config.Models;

using System.Diagnostics;
using System.Globalization;
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

    /// <summary>
    ///     Raw NVIDIA GPU probe via <c>nvidia-smi</c> — the single place that actually shells out
    ///     and parses its CSV output. Shared by <see cref="DetectNvidiaGpu" /> (which needs total
    ///     VRAM in GB for model-size estimation) and
    ///     <see cref="SimpleModelServer.Hardware.HardwareDetection" /> (which needs total/free VRAM
    ///     in MB for GPU-layer/concurrency decisions) so the process-launch and parsing logic isn't
    ///     maintained twice with the two copies free to drift out of sync.
    /// </summary>
    public static bool TryDetectNvidiaGpuRaw(out string name, out int memoryTotalMb, out int? memoryFreeMb)
    {
        name = "";
        memoryTotalMb = 0;
        memoryFreeMb = null;

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "nvidia-smi.exe" : "nvidia-smi",
                Arguments = "--query-gpu=name,memory.total,memory.free --format=csv,noheader,nounits",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(1500))
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                }

                return false;
            }

            string output = process.StandardOutput.ReadToEnd();
            if (process.ExitCode != 0)
            {
                return false;
            }

            string? line = output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string[] parts = line.Split(',', 3, StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int totalMb))
            {
                return false;
            }

            // memory.free is a newer column; tolerate drivers/nvidia-smi builds that only return
            // name+memory.total by leaving memoryFreeMb null rather than failing detection outright.
            int? freeMb = parts.Length >= 3 &&
                          int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedFree)
                ? parsedFree
                : null;

            name = parts[0];
            memoryTotalMb = totalMb;
            memoryFreeMb = freeMb;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static GpuInfo DetectNvidiaGpu()
    {
        if (!TryDetectNvidiaGpuRaw(out string name, out int memoryTotalMb, out _))
        {
            return new GpuInfo { IsAvailable = false };
        }

        double memoryGb = memoryTotalMb / 1024.0;
        return new GpuInfo
        {
            IsAvailable = true,
            Model = name,
            MemoryGb = memoryGb,
            MaxModelSizeB = EstimateMaxModelSize(memoryGb),
        };
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

    /// <summary>
    ///     Total unified memory on Apple Silicon, via <c>sysctl hw.memsize</c> (returns total
    ///     physical RAM in bytes). Falls back to a conservative 16GB estimate if sysctl is
    ///     unavailable or its output can't be parsed.
    /// </summary>
    private static double GetAppleSiliconMemory()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "/usr/sbin/sysctl",
                Arguments = "-n hw.memsize",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                return 16.0; // Default fallback
            }

            using var reader = process.StandardOutput;
            var output = reader.ReadToEnd();
            process.WaitForExit();

            if (long.TryParse(output.Trim(), out var totalBytes) && totalBytes > 0)
            {
                return totalBytes / (1024.0 * 1024.0 * 1024.0);
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


