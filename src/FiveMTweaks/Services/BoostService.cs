using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using FiveMTweaks.Models;

namespace FiveMTweaks.Services;

/// <summary>
/// FiveM-specific speed and frame-stability tweaks. Everything here is per-user (HKCU) and
/// reversible - nothing needs administrator rights and nothing touches system-wide settings.
/// </summary>
public static class BoostService
{
    private const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string GpuPrefKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

    /// <summary>Path to FiveM.exe, or null if it is not installed where we expect.</summary>
    public static string? FindFiveMExe()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(local, "FiveM", "FiveM.exe"),
            Path.Combine(local, "FiveM", "FiveM.app", "FiveM.exe"),
            Path.Combine(local, "FiveM", "FiveM.app", "FiveM_b2699_GTAProcess.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Fullscreen optimisations put a borderless-window compositor in front of an exclusive
    /// fullscreen game. On weak hardware that shows up as uneven frame times more than as lost
    /// average FPS, which is exactly the "my FPS is fine but it stutters" complaint.
    /// </summary>
    public static TweakResult DisableFullscreenOptimisations()
    {
        var exe = FindFiveMExe();
        if (exe is null) return new(false, "Could not find FiveM.exe. Install or launch FiveM once first.");

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(LayersKey);
            var existing = key?.GetValue(exe) as string ?? "";
            if (existing.Contains("DISABLEDXMAXIMIZEDWINDOWEDMODE"))
                return new(true, "Fullscreen optimisations were already off for FiveM.");

            key?.SetValue(exe, ("~ " + existing.Replace("~", "").Trim() + " DISABLEDXMAXIMIZEDWINDOWEDMODE").Replace("  ", " "));
            return new(true, "Fullscreen optimisations turned off for FiveM. Steadier frame times in fullscreen.", 1);
        }
        catch (Exception ex) { return new(false, $"Could not set the compatibility flag: {ex.Message}"); }
    }

    /// <summary>Pins FiveM to the discrete GPU on machines that have two.</summary>
    public static TweakResult PreferHighPerformanceGpu()
    {
        var exe = FindFiveMExe();
        if (exe is null) return new(false, "Could not find FiveM.exe. Install or launch FiveM once first.");

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(GpuPrefKey);
            key?.SetValue(exe, "GpuPreference=2;");   // 2 = high performance
            return new(true, "FiveM set to use the high-performance GPU.", 1);
        }
        catch (Exception ex) { return new(false, $"Could not set the GPU preference: {ex.Message}"); }
    }

    /// <summary>
    /// Raises the priority of a running FiveM. Above normal rather than High on purpose: High can
    /// starve the audio and input threads and make the game feel worse, not better.
    /// </summary>
    public static TweakResult BoostRunningProcess()
    {
        var names = new[] { "FiveM", "FiveM_b2699_GTAProcess", "FiveM_GTAProcess", "FiveM_b3095_GTAProcess" };
        var found = names.SelectMany(n =>
        {
            try { return Process.GetProcessesByName(n); }
            catch { return Array.Empty<Process>(); }
        }).ToList();

        if (found.Count == 0)
            return new(false, "FiveM is not running. Start the game, then press this.");

        var boosted = 0;
        foreach (var p in found)
        {
            try
            {
                p.PriorityClass = ProcessPriorityClass.AboveNormal;
                p.PriorityBoostEnabled = true;
                boosted++;
            }
            catch { /* protected process or already exited */ }
            finally { p.Dispose(); }
        }

        return boosted == 0
            ? new(false, "Windows refused the priority change. Try running this tool as administrator.")
            : new(true, $"Raised the priority of {boosted} FiveM process(es). Resets when the game closes.", boosted);
    }

    /// <summary>Undoes both registry tweaks. The priority change undoes itself on game exit.</summary>
    public static TweakResult RestoreAll()
    {
        var exe = FindFiveMExe();
        if (exe is null) return new(false, "Could not find FiveM.exe, so there is nothing to undo.");

        var undone = 0;
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(LayersKey, writable: true))
                if (key?.GetValue(exe) is not null) { key.DeleteValue(exe, false); undone++; }

            using (var key = Registry.CurrentUser.OpenSubKey(GpuPrefKey, writable: true))
                if (key?.GetValue(exe) is not null) { key.DeleteValue(exe, false); undone++; }

            return undone == 0
                ? new(true, "Nothing to undo — neither tweak was applied.")
                : new(true, $"Undid {undone} FiveM tweak(s).", undone);
        }
        catch (Exception ex) { return new(false, $"Could not undo: {ex.Message}"); }
    }
}
