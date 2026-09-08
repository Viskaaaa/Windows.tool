using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace FiveMTweaks.Services;

/// <summary>Whether the app keeps running in the tray, and whether Windows starts it.</summary>
public static class BackgroundPrefs
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "FiveMTweaks";

    /// <summary>Passed by the Windows startup entry so the app opens straight to the tray.</summary>
    public const string TrayArgument = "--tray";

    private static readonly string Path_ = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FiveMTweaks", "background.json");

    private sealed record Prefs(bool KeepRunning, bool NotifyOnLaunch);

    /// <summary>Closing the window hides it to the tray instead of exiting.</summary>
    public static bool KeepRunning { get; private set; }

    /// <summary>Show a balloon when FiveM starts or stops.</summary>
    public static bool NotifyOnLaunch { get; private set; } = true;

    public static void Load()
    {
        try
        {
            if (!File.Exists(Path_)) return;
            var p = JsonSerializer.Deserialize<Prefs>(File.ReadAllText(Path_));
            if (p is null) return;
            KeepRunning = p.KeepRunning;
            NotifyOnLaunch = p.NotifyOnLaunch;
        }
        catch { /* defaults are fine */ }
    }

    public static void Set(bool keepRunning, bool notifyOnLaunch)
    {
        KeepRunning = keepRunning;
        NotifyOnLaunch = notifyOnLaunch;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
            File.WriteAllText(Path_, JsonSerializer.Serialize(new Prefs(KeepRunning, NotifyOnLaunch)));
        }
        catch { }
    }

    // --- Start with Windows. Read from and written to the per-user Run key, no admin needed. ----

    public static bool StartsWithWindows
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(RunValue) is string s && s.Length > 0;
            }
            catch { return false; }
        }
    }

    public static bool SetStartWithWindows(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return false;

            if (!enabled)
            {
                key.DeleteValue(RunValue, throwOnMissingValue: false);
                JournalService.Record("Windows", "Start with Windows off", $@"HKCU\{RunKey}\{RunValue}",
                    "Removed the startup entry.");
                return true;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            // Quoted, because the path routinely contains spaces.
            key.SetValue(RunValue, $"\"{exe}\" {TrayArgument}");
            JournalService.Record("Windows", "Start with Windows on", $@"HKCU\{RunKey}\{RunValue}",
                "Added a startup entry that opens straight to the tray.");
            return true;
        }
        catch { return false; }
    }
}
