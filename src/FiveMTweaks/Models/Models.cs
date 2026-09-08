using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FiveMTweaks.Models;

public enum PcTier { Low, Medium, High }

public sealed class HardwareInfo
{
    public string GpuName { get; init; } = "Unknown GPU";
    public long GpuVramBytes { get; init; }
    public string CpuName { get; init; } = "Unknown CPU";
    public int CpuCores { get; init; }
    public int CpuThreads { get; init; }
    public long RamBytes { get; init; }
    public PcTier Tier { get; init; } = PcTier.Low;

    public string RamDisplay => RamBytes > 0 ? $"{Math.Round(RamBytes / 1024d / 1024 / 1024)} GB" : "Unknown";
    public string VramDisplay => GpuVramBytes > 0 ? $"{Math.Round(GpuVramBytes / 1024d / 1024 / 1024, 1)} GB" : "Unknown";
    public string CpuDisplay => CpuCores > 0 ? $"{CpuCores}-core / {CpuThreads}-thread" : "Unknown";

    public string TierDisplay => Tier switch
    {
        PcTier.Low => "Low tier",
        PcTier.Medium => "Medium tier",
        _ => "High tier"
    };
}

/// <summary>One scanned folder (or file group) the user may choose to delete.</summary>
public sealed class JunkItem : INotifyPropertyChanged
{
    private bool _selected = true;
    private long _sizeBytes;

    public string Category { get; init; } = "";      // "FiveM", "Windows", "Steam", ...
    public string Name { get; init; } = "";          // "FiveM cache"
    public string Path { get; init; } = "";
    public bool DeleteFolderItself { get; init; }    // false = empty it, keep the folder
    public string Note { get; init; } = "";          // shown to the user before deleting

    public bool Selected
    {
        get => _selected;
        set { _selected = value; OnChanged(); }
    }

    public long SizeBytes
    {
        get => _sizeBytes;
        set { _sizeBytes = value; OnChanged(); OnChanged(nameof(SizeDisplay)); }
    }

    public string SizeDisplay => Format.Bytes(SizeBytes);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class GameTarget
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    /// <summary>Settings file this profile rewrites (GTA V settings.xml for FiveM).</summary>
    public string SettingsPath { get; init; } = "";
    public bool Detected => !string.IsNullOrEmpty(SettingsPath) && System.IO.File.Exists(SettingsPath);
    public bool HasBackup => Detected && System.IO.File.Exists(SettingsPath + ".fivemtweaks.bak");
}

public sealed class FpsEstimate
{
    public int Before { get; init; }
    public int After { get; init; }
    public int PercentGain => Before > 0 ? (int)Math.Round((After - Before) * 100.0 / Before) : 0;
    /// <summary>Always true in v1 — nothing here is measured. Surfaced in the UI verbatim.</summary>
    public bool IsEstimate { get; init; } = true;
}

public static class Format
{
    public static string Bytes(long b)
    {
        if (b <= 0) return "0 B";
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i <= 1 ? $"{Math.Round(v)} {u[i]}" : $"{v:0.0} {u[i]}";
    }
}

/// <summary>Outcome of a tweak the user asked for. Message is shown verbatim in the UI.</summary>
public sealed record TweakResult(bool Ok, string Message, int Changed = 0);
