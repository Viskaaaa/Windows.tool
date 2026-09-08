using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using ViskaTweak.Models;

namespace ViskaTweak.Services;

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
            JournalService.Record("Windows", "Fullscreen optimisations off", $@"HKCU\{LayersKey}", exe);
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
            JournalService.Record("Windows", "High-performance GPU preferred", $@"HKCU\{GpuPrefKey}", exe);
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

    // --- Weak-machine tweaks. Per-user, no admin, and each one frees real resources. -----------

    private const string DesktopKey = @"Control Panel\Desktop";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string VisualFxKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";

    /// <summary>
    /// Windows animations, shadows and transparency are composited by the same GPU the game wants.
    /// On an integrated chip or a GT 1030 that is not free, and it costs nothing to look at.
    /// </summary>
    public static TweakResult TrimWindowsEffects()
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(VisualFxKey))
                key?.SetValue("VisualFXSetting", 2, RegistryValueKind.DWord);   // 2 = best performance

            using (var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey))
                key?.SetValue("EnableTransparency", 0, RegistryValueKind.DWord);

            using (var key = Registry.CurrentUser.CreateSubKey(DesktopKey))
            {
                key?.SetValue("MenuShowDelay", "0");
                key?.SetValue("DragFullWindows", "0");
                // Bit 0 of byte 0 clears the master "animate windows" flag.
                key?.SetValue("UserPreferencesMask",
                    new byte[] { 0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00 }, RegistryValueKind.Binary);
            }

            JournalService.Record("Windows", "Desktop effects trimmed", @"HKCU\Control Panel\Desktop",
                "Animations, window shadows and transparency turned off. Sign out and back in to apply fully.");

            return new(true, "Desktop animations and transparency turned off. Sign out and back in for the last of it.", 3);
        }
        catch (Exception ex) { return new(false, $"Could not change the desktop effects: {ex.Message}"); }
    }

    public static TweakResult RestoreWindowsEffects()
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(VisualFxKey))
                key?.SetValue("VisualFXSetting", 0, RegistryValueKind.DWord);   // 0 = let Windows choose

            using (var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey))
                key?.SetValue("EnableTransparency", 1, RegistryValueKind.DWord);

            using (var key = Registry.CurrentUser.CreateSubKey(DesktopKey))
            {
                key?.SetValue("MenuShowDelay", "400");
                key?.SetValue("DragFullWindows", "1");
                key?.SetValue("UserPreferencesMask",
                    new byte[] { 0x9E, 0x1E, 0x07, 0x80, 0x12, 0x00, 0x00, 0x00 }, RegistryValueKind.Binary);
            }

            JournalService.Record("Windows", "Desktop effects restored", @"HKCU\Control Panel\Desktop",
                "Windows defaults put back.", reversible: false);

            return new(true, "Desktop effects back to the Windows defaults. Sign out and back in to apply.", 3);
        }
        catch (Exception ex) { return new(false, $"Could not restore the desktop effects: {ex.Message}"); }
    }

    // --- Startup apps. On 8 GB these are the single biggest recoverable chunk of memory. --------

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ParkedKey = @"Software\ViskaTweak\DisabledStartup";

    public sealed record StartupApp(string Name, string Command, bool Enabled);

    /// <summary>Everything in the per-user Run key, plus whatever this tool has parked.</summary>
    public static List<StartupApp> ListStartupApps()
    {
        var apps = new List<StartupApp>();
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                foreach (var name in key?.GetValueNames() ?? Array.Empty<string>())
                {
                    // Never offer to disable ourselves from here; that lives in Settings.
                    if (name == "ViskaTweak") continue;
                    apps.Add(new StartupApp(name, key!.GetValue(name)?.ToString() ?? "", true));
                }

            using (var key = Registry.CurrentUser.OpenSubKey(ParkedKey))
                foreach (var name in key?.GetValueNames() ?? Array.Empty<string>())
                    apps.Add(new StartupApp(name, key!.GetValue(name)?.ToString() ?? "", false));
        }
        catch { /* an unreadable key just means a shorter list */ }

        return apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Disabling moves the entry into our own key rather than deleting it, so enabling puts back
    /// exactly what was there. Nothing the user has is ever destroyed.
    /// </summary>
    public static TweakResult SetStartupApp(StartupApp app, bool enabled)
    {
        try
        {
            var (fromKey, toKey) = enabled ? (ParkedKey, RunKey) : (RunKey, ParkedKey);

            using var from = Registry.CurrentUser.CreateSubKey(fromKey);
            using var to = Registry.CurrentUser.CreateSubKey(toKey);
            if (from is null || to is null) return new(false, "Windows would not open the startup keys.");

            var value = from.GetValue(app.Name)?.ToString() ?? app.Command;
            if (string.IsNullOrEmpty(value)) return new(false, $"Could not read the entry for {app.Name}.");

            to.SetValue(app.Name, value);
            from.DeleteValue(app.Name, throwOnMissingValue: false);

            JournalService.Record("Windows", enabled ? $"Startup app enabled: {app.Name}" : $"Startup app disabled: {app.Name}",
                $@"HKCU\{RunKey}", value);

            return new(true, enabled
                ? $"{app.Name} will start with Windows again."
                : $"{app.Name} will no longer start with Windows. It is parked, not deleted.", 1);
        }
        catch (Exception ex) { return new(false, $"Could not change {app.Name}: {ex.Message}"); }
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

            if (undone > 0)
                JournalService.Record("Windows", "FiveM tweaks undone", "HKCU", 
                    $"{undone} registry tweak(s) removed.", reversible: false);

            return undone == 0
                ? new(true, "Nothing to undo — neither tweak was applied.")
                : new(true, $"Undid {undone} FiveM tweak(s).", undone);
        }
        catch (Exception ex) { return new(false, $"Could not undo: {ex.Message}"); }
    }
}
