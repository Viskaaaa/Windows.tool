using System;
using FiveMTweaks.Models;

namespace FiveMTweaks.Services;

/// <summary>
/// v1 = Option B from the spec: a modelled estimate, never a measurement. The numbers come from the
/// hardware tier and which changes were actually applied; the UI labels them "estimated" everywhere.
/// Swapping in real data later means replacing this class with a PresentMon capture + CSV parse and
/// setting <see cref="FpsEstimate.IsEstimate"/> to false.
/// </summary>
public static class FpsEstimator
{
    public static FpsEstimate Estimate(HardwareInfo hw, bool presetApplied, bool cachesCleaned, long freedBytes = 0)
    {
        int baseline = hw.Tier switch
        {
            PcTier.Low => 27,
            PcTier.Medium => 55,
            _ => 95
        };

        // A weak CPU caps FiveM harder than the GPU does — busy servers are CPU-bound.
        if (hw.CpuThreads <= 4) baseline -= 4;
        if (hw.RamBytes > 0 && hw.RamBytes < 8L * 1024 * 1024 * 1024) baseline -= 5;
        baseline = Math.Max(12, baseline);

        double factor = 1.0;
        if (presetApplied)
            factor += hw.Tier switch { PcTier.Low => 0.45, PcTier.Medium => 0.22, _ => 0.08 };

        // Clearing caches mostly buys smoothness and load times, not average FPS. Kept small on purpose.
        if (cachesCleaned)
            factor += Math.Min(0.06, freedBytes / (40d * 1024 * 1024 * 1024));

        return new FpsEstimate
        {
            Before = baseline,
            After = (int)Math.Round(baseline * factor),
            IsEstimate = true
        };
    }
}
