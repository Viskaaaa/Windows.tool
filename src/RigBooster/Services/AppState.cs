using System;
using System.Collections.Generic;
using RigBooster.Models;

namespace RigBooster.Services;

/// <summary>Small shared state so the Dashboard reflects what the other tabs have done.</summary>
public static class AppState
{
    public static HardwareInfo? Hardware { get; set; }
    public static bool PresetApplied { get; set; }
    public static bool CachesCleaned { get; set; }
    public static long FreedBytes { get; set; }

    /// <summary>Most recent scan, so switching tabs does not force a rescan.</summary>
    public static List<JunkItem>? LastScan { get; set; }

    /// <summary>Raised when a tab changes something the Dashboard shows.</summary>
    public static event Action? Changed;
    public static void NotifyChanged() => Changed?.Invoke();
}
