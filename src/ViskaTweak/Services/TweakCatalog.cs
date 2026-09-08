using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using ViskaTweak.Models;

namespace ViskaTweak.Services;

/// <summary>
/// The one-click tweak grid.
///
/// Every entry here does something a person could verify by hand - a registry value, a powercfg
/// call, a folder emptied. Where a popular "booster" feature has no real mechanism behind it, the
/// nearest genuine thing is implemented under an accurate name instead, and the card says what it
/// actually does. A button that lies is worse than no button.
/// </summary>
public static class TweakCatalog
{
    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr process);

    public sealed record Tweak(
        string Id,
        string Icon,
        string Name,
        string Description,
        bool NeedsAdmin,
        Func<TweakResult> Apply,
        Func<TweakResult>? Undo = null);

    public static List<Tweak> All() => new()
    {
        new("latency", "⚡", "Low latency mode",
            "Turns on optimisations for windowed games and switches off mouse acceleration, so pointer movement maps 1:1 to your aim.",
            false, LowLatency, UndoLowLatency),

        new("cycle", "♻", "Full system cycle",
            "Empties the temp folder, restarts Explorer and flushes the DNS cache. Clears out whatever the machine has accumulated since the last reboot.",
            false, FullSystemCycle),

        new("cpufocus", "◉", "CPU focus mode",
            "Gives the foreground program a longer share of processor time. This one is system-wide, so it needs administrator rights.",
            true, CpuFocus, UndoCpuFocus),

        new("shaders", "▦", "Shader cache reset",
            "Deletes the DirectX and GPU vendor shader caches. A stale or corrupt cache is a real cause of repeated hitching in the same spots.",
            false, ShaderCacheReset),

        new("boost", "⚙", "Processor boost mode",
            "Sets the power scheme to boost aggressively instead of conservatively, so the CPU ramps up sooner when a frame needs it.",
            false, ProcessorBoost, UndoProcessorBoost),

        new("mouse", "⌖", "Pointer precision fix",
            "Sets pointer speed to the 1:1 notch and clears the acceleration curve. Consistent aim rather than faster aim.",
            false, PointerFix, UndoPointerFix),

        new("network", "⇄", "Network latency tuning",
            "Turns off Nagle's algorithm for your network adapters, so small packets go out immediately. Helps on a busy or distant server. Needs administrator rights.",
            true, NetworkLatency, UndoNetworkLatency),

        new("memory", "▣", "Free background memory",
            "Asks background programs to release memory they are not using. Useful when RAM is nearly full; on a machine with headroom it does nothing worth having.",
            false, MemorySweep),
    };

    // --- 1. Latency -----------------------------------------------------------------------------

    private const string GpuPrefKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string MouseKey = @"Control Panel\Mouse";

    private static TweakResult LowLatency()
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(GpuPrefKey))
                key?.SetValue("SwapEffectUpgradeEnable", "1");

            using (var key = Registry.CurrentUser.CreateSubKey(MouseKey))
            {
                key?.SetValue("MouseSpeed", "0");        // acceleration off
                key?.SetValue("MouseThreshold1", "0");
                key?.SetValue("MouseThreshold2", "0");
            }

            JournalService.Record("Tweaks", "Low latency mode", $@"HKCU\{GpuPrefKey}",
                "Windowed-game optimisations on, mouse acceleration off. Sign out and back in to apply fully.");
            return new(true, "Low latency mode on. Sign out and back in for the mouse change to take hold.", 1);
        }
        catch (Exception ex) { return new(false, $"Could not apply: {ex.Message}"); }
    }

    private static TweakResult UndoLowLatency()
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(GpuPrefKey))
                key?.SetValue("SwapEffectUpgradeEnable", "0");

            using (var key = Registry.CurrentUser.CreateSubKey(MouseKey))
            {
                key?.SetValue("MouseSpeed", "1");
                key?.SetValue("MouseThreshold1", "6");
                key?.SetValue("MouseThreshold2", "10");
            }

            JournalService.Record("Tweaks", "Low latency mode undone", $@"HKCU\{MouseKey}",
                "Windows defaults restored.", reversible: false);
            return new(true, "Windows defaults restored.", 1);
        }
        catch (Exception ex) { return new(false, $"Could not undo: {ex.Message}"); }
    }

    // --- 2. Full system cycle -------------------------------------------------------------------

    private static TweakResult FullSystemCycle()
    {
        long freed = 0;
        var steps = new List<string>();

        try
        {
            var temp = Path.GetTempPath();
            var before = CacheScanner.DirectorySize(temp);
            foreach (var file in Directory.EnumerateFiles(temp))
            {
                try { File.Delete(file); } catch { /* in use */ }
            }
            freed = Math.Max(0, before - CacheScanner.DirectorySize(temp));
            steps.Add($"temp cleared ({Format.Bytes(freed)})");
        }
        catch { steps.Add("temp could not be cleared"); }

        try
        {
            Run("ipconfig", "/flushdns");
            steps.Add("DNS cache flushed");
        }
        catch { }

        try
        {
            // Explorer relaunches itself on modern Windows; the start is a belt-and-braces fallback.
            Run("taskkill", "/f /im explorer.exe");
            System.Threading.Thread.Sleep(600);
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            steps.Add("Explorer restarted");
        }
        catch { steps.Add("Explorer could not be restarted"); }

        JournalService.Record("Tweaks", "Full system cycle", Path.GetTempPath(),
            string.Join(", ", steps), reversible: false);

        return new(true, string.Join(" · ", steps), 1);
    }

    // --- 3. CPU focus ---------------------------------------------------------------------------

    private const string PriorityKey = @"SYSTEM\CurrentControlSet\Control\PriorityControl";

    private static TweakResult CpuFocus() => SetPrioritySeparation(0x26,
        "Foreground programs now get a longer processor slice. Restart to apply.");

    private static TweakResult UndoCpuFocus() => SetPrioritySeparation(2,
        "Processor scheduling back to the Windows default. Restart to apply.");

    private static TweakResult SetPrioritySeparation(int value, string message)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PriorityKey, writable: true);
            if (key is null)
                return new(false, "Needs administrator rights. Close Viska Tweak, right-click it and choose Run as administrator.");

            key.SetValue("Win32PrioritySeparation", value, RegistryValueKind.DWord);
            JournalService.Record("Tweaks", "CPU focus mode", $@"HKLM\{PriorityKey}",
                $"Win32PrioritySeparation set to 0x{value:X}.");
            return new(true, message, 1);
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, "Windows refused the change. This one needs administrator rights — right-click the app and choose Run as administrator.");
        }
        catch (Exception ex) { return new(false, $"Could not apply: {ex.Message}"); }
    }

    // --- 4. Shader caches -----------------------------------------------------------------------

    private static TweakResult ShaderCacheReset()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var targets = new[]
        {
            Path.Combine(local, "D3DSCache"),
            Path.Combine(local, "NVIDIA", "DXCache"),
            Path.Combine(local, "NVIDIA", "GLCache"),
            Path.Combine(local, "AMD", "DxCache"),
            Path.Combine(local, "AMD", "GLCache"),
            Path.Combine(local, "Intel", "ShaderCache"),
        };

        long freed = 0;
        var cleared = 0;

        foreach (var dir in targets)
        {
            if (!Directory.Exists(dir)) continue;
            var before = CacheScanner.DirectorySize(dir);
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    try { Directory.Delete(sub, true); } catch { }
                }
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    try { File.Delete(file); } catch { }
                }
                freed += Math.Max(0, before - CacheScanner.DirectorySize(dir));
                cleared++;
            }
            catch { }
        }

        if (cleared == 0) return new(true, "No shader caches found — nothing to reset.");

        JournalService.Record("Tweaks", "Shader caches reset", "Local app data",
            $"{cleared} cache folder(s), {Format.Bytes(freed)} freed.", reversible: false);

        return new(true, $"Reset {cleared} shader cache(s), {Format.Bytes(freed)} freed. "
                         + "The first launch of each game rebuilds them and will be slower.", 1);
    }

    // --- 5. Processor boost ---------------------------------------------------------------------

    private const string BoostGuid = "be337238-0d82-4146-a960-4f3749d470c7";  // PERFBOOSTMODE
    private const string ProcessorSub = "54533251-82be-4824-96c1-47b60b740d00";

    private static TweakResult ProcessorBoost() => SetBoostMode(2,
        "Processor boost set to aggressive.");

    private static TweakResult UndoProcessorBoost() => SetBoostMode(1,
        "Processor boost back to the Windows default.");

    private static TweakResult SetBoostMode(int mode, string message)
    {
        try
        {
            var ok = Run("powercfg", $"/setacvalueindex SCHEME_CURRENT {ProcessorSub} {BoostGuid} {mode}")
                     && Run("powercfg", "/setactive SCHEME_CURRENT");

            if (!ok) return new(false, "powercfg refused the change. Some laptop vendors lock the power scheme.");

            JournalService.Record("Tweaks", "Processor boost mode", "powercfg SCHEME_CURRENT",
                $"PERFBOOSTMODE set to {mode}.");
            return new(true, message, 1);
        }
        catch (Exception ex) { return new(false, $"Could not apply: {ex.Message}"); }
    }

    // --- 6. Pointer ------------------------------------------------------------------------------

    private static TweakResult PointerFix()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(MouseKey);
            key?.SetValue("MouseSensitivity", "10");   // the 6/11 notch, 1:1 with no scaling
            key?.SetValue("MouseSpeed", "0");
            key?.SetValue("MouseThreshold1", "0");
            key?.SetValue("MouseThreshold2", "0");

            JournalService.Record("Tweaks", "Pointer precision fix", $@"HKCU\{MouseKey}",
                "Sensitivity set to the 1:1 notch, acceleration cleared.");
            return new(true, "Pointer set to 1:1 with no acceleration. Sign out and back in to apply.", 1);
        }
        catch (Exception ex) { return new(false, $"Could not apply: {ex.Message}"); }
    }

    private static TweakResult UndoPointerFix()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(MouseKey);
            key?.SetValue("MouseSensitivity", "10");
            key?.SetValue("MouseSpeed", "1");
            key?.SetValue("MouseThreshold1", "6");
            key?.SetValue("MouseThreshold2", "10");

            JournalService.Record("Tweaks", "Pointer precision undone", $@"HKCU\{MouseKey}",
                "Windows defaults restored.", reversible: false);
            return new(true, "Pointer settings back to the Windows defaults.", 1);
        }
        catch (Exception ex) { return new(false, $"Could not undo: {ex.Message}"); }
    }

    // --- 7. Network -------------------------------------------------------------------------------

    private const string InterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    private static TweakResult NetworkLatency() => SetNagle(1, "Nagle's algorithm turned off on {0} adapter(s). Restart to apply.");

    private static TweakResult UndoNetworkLatency() => SetNagle(0, "Nagle's algorithm restored on {0} adapter(s). Restart to apply.");

    private static TweakResult SetNagle(int value, string message)
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(InterfacesKey, writable: true);
            if (root is null)
                return new(false, "Needs administrator rights. Right-click Viska Tweak and choose Run as administrator.");

            var touched = 0;
            foreach (var name in root.GetSubKeyNames())
            {
                try
                {
                    using var adapter = root.OpenSubKey(name, writable: true);
                    if (adapter?.GetValue("DhcpIPAddress") is null && adapter?.GetValue("IPAddress") is null)
                        continue;   // not a real, configured adapter

                    adapter.SetValue("TcpAckFrequency", value, RegistryValueKind.DWord);
                    adapter.SetValue("TCPNoDelay", value, RegistryValueKind.DWord);
                    touched++;
                }
                catch { }
            }

            if (touched == 0) return new(false, "No configured network adapters were found to change.");

            JournalService.Record("Tweaks", "Network latency tuning", $@"HKLM\{InterfacesKey}",
                $"TcpAckFrequency and TCPNoDelay set to {value} on {touched} adapter(s).");
            return new(true, string.Format(message, touched), 1);
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, "Windows refused the change. This one needs administrator rights.");
        }
        catch (Exception ex) { return new(false, $"Could not apply: {ex.Message}"); }
    }

    // --- 8. Memory --------------------------------------------------------------------------------

    private static TweakResult MemorySweep()
    {
        var trimmed = 0;
        var self = Environment.ProcessId;

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == self || p.Id <= 4) continue;
                if (EmptyWorkingSet(p.Handle) != 0) trimmed++;
            }
            catch { /* protected process - expected for most system ones */ }
            finally { p.Dispose(); }
        }

        JournalService.Record("Tweaks", "Background memory freed", $"{trimmed} process(es)",
            "Working sets trimmed.", reversible: false);

        return new(true, trimmed == 0
            ? "Windows did not allow any process to be trimmed."
            : $"Asked {trimmed} background process(es) to release unused memory. "
              + "They take it back when they need it, so do this when RAM is tight, not habitually.", 1);
    }

    // --- helpers ------------------------------------------------------------------------------------

    private static bool Run(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            p?.WaitForExit(8000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }
}
