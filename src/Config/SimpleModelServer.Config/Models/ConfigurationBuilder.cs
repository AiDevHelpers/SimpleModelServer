using SimpleModelServer.Contracts.Models;

namespace SimpleModelServer.Config.Models;

/// <summary>
///     Builds optimized model configurations based on GPU capabilities and preference mode.
/// </summary>
public static class ConfigurationBuilder
{
    /// <summary>
    ///     Detects GPU and builds an optimized model configuration.
    /// </summary>
    public static ServerConfig BuildOptimizedConfig(ServerConfig baseConfig)
    {
        // Detect GPU capabilities
        var gpuInfo = GpuDetector.DetectGpu();
        baseConfig.Global.GpuInfo = gpuInfo;

        // Build draft/main models for each family pair based on GPU and preference mode
        foreach (var config in baseConfig.Models.Configurations)
        {
            foreach (var familyPair in config.Families)
            {
                BuildOptimalModelsForPair(familyPair, gpuInfo, baseConfig.Global.PreferenceMode);
            }

            // Ensure embed model exists
            if (config.Embed == null)
            {
                config.Embed = CreateDefaultEmbedModel();
            }
        }

        return baseConfig;
    }

    private static void BuildOptimalModelsForPair(
        FamilyPair pair,
        GpuInfo gpuInfo,
        PreferenceMode preference)
    {
        // If both models are already specified, don't override
        if (pair.Draft != null && pair.Main != null)
        {
            return; // Use explicit configuration
        }

        var strategy = DetermineAllocationStrategy(gpuInfo, preference);

        if (pair.Draft == null && pair.Main == null)
        {
            // No models specified - build from family/variant
            (pair.Draft, pair.Main) = CreateModelsForStrategy(pair, strategy, gpuInfo);
        }
        // else: exactly one of Draft/Main is already set - keep as-is (draft-only or main-only)
    }

    private static AllocationStrategy DetermineAllocationStrategy(GpuInfo gpuInfo, PreferenceMode preference)
    {
        // If GPU not available, use CPU strategy
        if (!gpuInfo.IsAvailable || gpuInfo.MaxModelSizeB == null || gpuInfo.MaxModelSizeB < 0.5)
        {
            return AllocationStrategy.CpuOnly;
        }

        var maxSize = gpuInfo.MaxModelSizeB.Value;

        // Determine strategy based on preference mode and GPU capability
        return preference switch
        {
            PreferenceMode.GPU when maxSize >= 3 => AllocationStrategy.MainGpuDraftCpu,
            PreferenceMode.GPU when maxSize >= 1.5 => AllocationStrategy.MainGpuEmbedCpu,
            PreferenceMode.GPU => AllocationStrategy.DraftGpuEmbedCpu,

            PreferenceMode.Size when maxSize >= 6.7 => AllocationStrategy.MainGpuDraftCpu,
            PreferenceMode.Size when maxSize >= 3 => AllocationStrategy.MainGpuDraftCpu,
            PreferenceMode.Size => AllocationStrategy.MainCpuDraftCpu,

            PreferenceMode.Auto => // Fall back to Size logic
                maxSize >= 3 ? AllocationStrategy.MainCpuDraftCpu : AllocationStrategy.DraftGpuEmbedCpu,

            _ => AllocationStrategy.MainCpuDraftCpu,
        };
    }

    private static (ModelConfig? draft, ModelConfig? main) CreateModelsForStrategy(
        FamilyPair pair,
        AllocationStrategy strategy,
        GpuInfo gpuInfo)
    {
        // This is simplified - in production, you'd map family/variant to actual model sizes
        return strategy switch
        {
            AllocationStrategy.CpuOnly => (
                CreateModelConfig(pair, "0.5b", 2048, onGpu: false),
                CreateModelConfig(pair, "3b", 4096, onGpu: false)
            ),
            AllocationStrategy.DraftGpuEmbedCpu => (
                CreateModelConfig(pair, "0.5b", 2048, onGpu: true),
                null
            ),
            AllocationStrategy.MainGpuEmbedCpu => (
                null,
                CreateModelConfig(pair, "3b", 4096, onGpu: true)
            ),
            AllocationStrategy.MainGpuDraftCpu => (
                CreateModelConfig(pair, "0.5b", 2048, onGpu: false),
                CreateModelConfig(pair, "3b", 4096, onGpu: true)
            ),
            _ => (
                CreateModelConfig(pair, "0.5b", 2048, onGpu: false),
                CreateModelConfig(pair, "3b", 4096, onGpu: false)
            ),
        };
    }

    private static ModelConfig CreateModelConfig(FamilyPair pair, string size, int numCtx, bool onGpu)
    {
        var config = new ModelConfig
        {
            Family = pair.Family,
            Variant = pair.Variant,
            Size = size,
            Temperature = 0.1,
            TopP = 0.9,
            NumCtx = AutoCalculateContext(numCtx),
            Stop = GetDefaultStopTokens(size),
            // GPU/placement info would be tracked elsewhere in runtime
        };

        return config;
    }

    private static EmbedConfig CreateDefaultEmbedModel()
    {
        return new EmbedConfig
        {
            Name = "nomic-embed-text",
            NumCtx = 8192,
            KeepAliveMinutes = 30,
            Stop = [],
        };
    }

    private static int AutoCalculateContext(int baseCtx)
    {
        // In production, scale based on GPU memory
        // For now, return base
        return baseCtx;
    }

    private static string[] GetDefaultStopTokens(string size)
    {
        // Common stop tokens for code/instruction models
        return
        [
            "```json",
            "{\"name\":",
            "\"arguments\":",
            "{\"function\":",
            "\"tool_call",
        ];
    }

    private enum AllocationStrategy
    {
        /// <summary>CPU only - no GPU available</summary>
        CpuOnly,

        /// <summary>Draft on GPU, embed on CPU</summary>
        DraftGpuEmbedCpu,

        /// <summary>Main on GPU, embed on CPU</summary>
        MainGpuEmbedCpu,

        /// <summary>Main on GPU, draft on CPU</summary>
        MainGpuDraftCpu,

        /// <summary>All on CPU</summary>
        MainCpuDraftCpu,
    }
}


