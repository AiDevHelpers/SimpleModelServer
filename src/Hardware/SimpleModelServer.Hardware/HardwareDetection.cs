using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Config.Models;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Hardware.Interfaces;

namespace SimpleModelServer.Hardware;

public sealed class HardwareDetection : IHardwareDetection
{
    public ResolvedHardwareSettings Resolve(IGlobalConfig config)
    {
        GpuInfo gpu = config.GpuLayers.Auto ? DetectGpu() : GpuInfo.None;
        return new ResolvedHardwareSettings
        {
            Gpu = gpu,
            GpuLayers = ResolveGpuLayers(config.GpuLayers, gpu),
            CpuThreads = ResolveCpuThreads(config.CpuThreads),
            MaxConcurrentSummaryRequests = ResolveMaxConcurrentSummaryRequests(gpu)
        };
    }

    /// <summary>
    ///     Background summary calls (indexing) must not starve interactive draft-pair
    ///     generation on the same Ollama instance, so this stays small regardless of
    ///     hardware: 1 without a capable GPU, 2 with >=12GB VRAM, 3 with >=24GB VRAM.
    /// </summary>
    private static int ResolveMaxConcurrentSummaryRequests(GpuInfo gpu)
    {
        return !gpu.Detected || gpu.MemoryMb is null
            ? 1
            : gpu.MemoryMb switch
            {
                >= 24576 => 3,
                >= 12288 => 2,
                _ => 1
            };
    }

    private static int? ResolveGpuLayers(HardwareSetting setting, GpuInfo gpu)
    {
        return !setting.IsSpecified
            ? null
            : !setting.Auto
            ? setting.Value
            : !gpu.Detected || gpu.MemoryMb is null || gpu.MemoryMb < 4096
            ? 0
            : gpu.MemoryMb switch
            {
                >= 12288 => 999,
                >= 8192 => 35,
                >= 6144 => 28,
                _ => 20
            };
    }

    private static int? ResolveCpuThreads(HardwareSetting setting)
    {
        return !setting.IsSpecified ? null : !setting.Auto ? setting.Value : Math.Max(1, Environment.ProcessorCount - 2);
    }

    // The actual nvidia-smi process launch + CSV parsing lives in GpuDetector.TryDetectNvidiaGpuRaw
    // (SimpleModelServer.Config) — shared with GpuDetector.DetectGpu, which needs the same raw
    // reading for its own (GB-based) GpuInfo shape.
    private static GpuInfo DetectGpu()
    {
        (bool success, string name, int memoryTotalMb, int? memoryFreeMb) = GpuDetector.TryDetectNvidiaGpuRaw();
        return success
            ? new GpuInfo { Detected = true, Name = name, MemoryMb = memoryTotalMb, FreeMemoryMb = memoryFreeMb }
            : GpuInfo.None;
    }
}
