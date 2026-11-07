using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Hardware;

using Xunit;

namespace SimpleModelServer.Hardware.Tests;

/// <summary>
///     Covers HardwareDetection.Resolve — the branching logic that turns a GlobalConfig's
///     GpuLayers/CpuThreads HardwareSetting (unspecified/override/auto) into concrete resolved
///     values. Zero coverage before this.
///
///     DetectGpu() shells out to a real `nvidia-smi` process and is only invoked when
///     config.GpuLayers.Auto is true — for Unspecified/Override settings, Resolve never calls it
///     (gpu is unconditionally GpuInfo.None), which is what makes the Unspecified/Override/
///     CpuThreads-Automatic cases below fully deterministic regardless of what hardware the test
///     happens to run on. The one GpuLayers=Automatic test is the exception: it exercises the
///     real nvidia-smi probe and only asserts an invariant that holds on any machine (GPU present
///     or not), rather than a specific layer count.
/// </summary>
public sealed class HardwareDetectionTests
{
	[Fact]
	public void Resolve_AllUnspecified_ReturnsNullsAndNoGpu()
	{
		var detection = new HardwareDetection();
		GlobalConfig config = new()
		{
			GpuLayers = HardwareSetting.Unspecified,
			CpuThreads = HardwareSetting.Unspecified
		};

		ResolvedHardwareSettings hw = detection.Resolve(config);

		Assert.Null(hw.GpuLayers);
		Assert.Null(hw.CpuThreads);
		Assert.False(hw.Gpu.Detected);
		Assert.Equal(1, hw.MaxConcurrentSummaryRequests);
	}

	[Fact]
	public void Resolve_ExplicitOverrides_ArePassedThroughVerbatim_WithoutProbingGpu()
	{
		var detection = new HardwareDetection();
		GlobalConfig config = new()
		{
			GpuLayers = HardwareSetting.Override(40),
			CpuThreads = HardwareSetting.Override(8)
		};

		ResolvedHardwareSettings hw = detection.Resolve(config);

		Assert.Equal(40, hw.GpuLayers);
		Assert.Equal(8, hw.CpuThreads);
		// GpuLayers isn't Auto, so Resolve must never invoke the real GPU probe — Gpu stays
		// GpuInfo.None regardless of what's actually installed on this machine.
		Assert.False(hw.Gpu.Detected);
		Assert.Equal(1, hw.MaxConcurrentSummaryRequests);
	}

	[Fact]
	public void Resolve_CpuThreadsAutomatic_MatchesProcessorCountFormula()
	{
		var detection = new HardwareDetection();
		GlobalConfig config = new()
		{
			GpuLayers = HardwareSetting.Unspecified,
			CpuThreads = HardwareSetting.Automatic
		};

		ResolvedHardwareSettings hw = detection.Resolve(config);

		int expected = Math.Max(1, Environment.ProcessorCount - 2);
		Assert.Equal(expected, hw.CpuThreads);
	}

	[Fact]
	public void Resolve_GpuLayersOverride_IndependentOfCpuThreadsSetting()
	{
		var detection = new HardwareDetection();
		// Regression guard for the two settings being resolved independently — an override on
		// one must not leak into or get clobbered by whatever the other is set to.
		GlobalConfig config = new()
		{
			GpuLayers = HardwareSetting.Override(999),
			CpuThreads = HardwareSetting.Automatic
		};

		ResolvedHardwareSettings hw = detection.Resolve(config);

		Assert.Equal(999, hw.GpuLayers);
		Assert.Equal(Math.Max(1, Environment.ProcessorCount - 2), hw.CpuThreads);
	}

	/// <summary>Only the four literal bucket values ResolveGpuLayers can produce — copied here deliberately so a change to the thresholds/values in HardwareDetection breaks this test rather than passing silently.</summary>
	private static readonly int[] PossibleAutoGpuLayerValues = [0, 20, 28, 35, 999];

	[Fact]
	public void Resolve_GpuLayersAutomatic_ReturnsOneOfTheKnownBucketValues()
	{
		var detection = new HardwareDetection();
		// Exercises the real nvidia-smi probe (via DetectGpu) — deliberately does not assert a
		// specific value, since that depends on whatever GPU (if any) is actually installed on
		// the machine running this test. What's verified is that Resolve produces a value from
		// exactly the set HardwareDetection's bucket logic is allowed to produce, and that the
		// no-GPU-detected case always yields exactly 0 (never null — GpuLayers=Automatic means
		// "resolve to something", unlike Unspecified).
		GlobalConfig config = new()
		{
			GpuLayers = HardwareSetting.Automatic,
			CpuThreads = HardwareSetting.Unspecified
		};

		ResolvedHardwareSettings hw = detection.Resolve(config);

		Assert.NotNull(hw.GpuLayers);
		Assert.Contains(hw.GpuLayers!.Value, PossibleAutoGpuLayerValues);

		if (!hw.Gpu.Detected || hw.Gpu.MemoryMb is null)
		{
			Assert.Equal(0, hw.GpuLayers);
		}
	}
}
