using System;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using ViskaTweak.Models;

namespace ViskaTweak.Services;

public static class HardwareService
{
    public static Task<HardwareInfo> DetectAsync() => Task.Run(Detect);

    public static HardwareInfo Detect()
    {
        string gpu = "Unknown GPU"; long vram = 0;
        string cpu = "Unknown CPU"; int cores = 0, threads = 0;
        long ram = 0;

        try
        {
            using var q = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM FROM Win32_VideoController");
            // Pick the adapter with the most VRAM: laptops report the iGPU first.
            var best = q.Get().Cast<ManagementObject>()
                .Select(o => (Name: o["Name"]?.ToString() ?? "", Ram: ToLong(o["AdapterRAM"])))
                .OrderByDescending(x => x.Ram)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(best.Name)) { gpu = best.Name.Trim(); vram = best.Ram; }
        }
        catch { /* leave defaults; the UI shows "Unknown" rather than failing the launch */ }

        try
        {
            using var q = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (ManagementObject o in q.Get())
            {
                cpu = (o["Name"]?.ToString() ?? cpu).Trim();
                cores += (int)ToLong(o["NumberOfCores"]);
                threads += (int)ToLong(o["NumberOfLogicalProcessors"]);
            }
        }
        catch { }

        try
        {
            using var q = new ManagementObjectSearcher(
                "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            ram = q.Get().Cast<ManagementObject>()
                   .Select(o => ToLong(o["TotalPhysicalMemory"])).FirstOrDefault();
        }
        catch { }

        if (threads == 0) threads = Environment.ProcessorCount;
        if (cores == 0) cores = Math.Max(1, threads / 2);

        return new HardwareInfo
        {
            GpuName = gpu, GpuVramBytes = vram,
            CpuName = cpu, CpuCores = cores, CpuThreads = threads,
            RamBytes = ram,
            Tier = Classify(gpu, vram, ram, threads)
        };
    }

    /// <summary>
    /// Deliberately conservative: when in doubt we land on Low, because a Low profile on a strong
    /// PC costs some visual quality, while a High profile on a GT 1030 just stutters.
    /// </summary>
    internal static PcTier Classify(string gpu, long vram, long ram, int threads)
    {
        var g = gpu.ToUpperInvariant();
        double ramGb = ram / 1024d / 1024 / 1024;
        double vramGb = vram / 1024d / 1024 / 1024;

        // Known weak/integrated parts: straight to Low regardless of the rest of the box.
        string[] weak = { "GT 1030", "GT1030", "GT 710", "GT 730", "GT 1010", "MX150", "MX250",
                          "UHD GRAPHICS", "HD GRAPHICS", "VEGA 3", "VEGA 8", "RADEON R5", "RADEON R7 GRAPHICS" };
        if (weak.Any(w => g.Contains(w))) return PcTier.Low;

        int score = 0;
        if (ramGb >= 16) score++;
        if (ramGb >= 32) score++;
        if (threads >= 8) score++;
        if (threads >= 16) score++;
        if (vramGb >= 6) score++;
        if (vramGb >= 10) score++;

        string[] strong = { "RTX 30", "RTX 40", "RTX 50", "RX 6", "RX 7", "RX 9" };
        if (strong.Any(s => g.Contains(s))) score += 2;

        if (ramGb > 0 && ramGb <= 8) return PcTier.Low;   // 8 GB or less is the binding constraint
        return score >= 5 ? PcTier.High : score >= 3 ? PcTier.Medium : PcTier.Low;
    }

    private static long ToLong(object? o)
    {
        try { return o is null ? 0 : Convert.ToInt64(o); } catch { return 0; }
    }
}
