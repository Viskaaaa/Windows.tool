using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FiveMTweaks.Models;

namespace FiveMTweaks.Services;

/// <summary>
/// Manages an existing ReShade install. It does not download or install ReShade - this only tunes
/// or switches off what is already there.
///
/// Worth being blunt about the direction: ReShade costs frames, it does not add them. Everything
/// here makes it cheaper or turns it off.
/// </summary>
public static class ReShadeService
{
    /// <summary>Shaders that cost the most on weak hardware, matched against technique names.</summary>
    private static readonly string[] Expensive =
    {
        "MXAO", "RTGI", "RayTracing", "qUINT_rt", "DOF", "DepthOfField", "ADOF", "CinematicDOF",
        "Bloom", "MagicBloom", "LightDoF", "SSR", "Reflect", "GodRays", "Sunbeams", "Volumetric",
        "SMAA", "TAA", "AmbientLight", "MotionBlur", "NeoBloom", "PPFX"
    };

    private const string BackupSuffix = ".fivemtweaks.bak";

    public sealed record Install(string Folder, string DllPath, string IniPath, bool Enabled)
    {
        public string DisplayFolder => Folder;
        public string PresetPath { get; init; } = "";
    }

    /// <summary>Loader names, plus anything called *reshade*.dll, in any of the folders below.</summary>
    private static readonly string[] LoaderNames =
        { "dxgi.dll", "d3d11.dll", "d3d12.dll", "opengl32.dll", "ReShade64.dll", "ReShade32.dll", "ReShade.dll" };

    /// <summary>
    /// FiveM loads ReShade out of FiveM.app\plugins, not from beside the executable the way a
    /// normal game does, so the plugins folders are searched first.
    /// </summary>
    public static Install? Detect()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new[]
        {
            Path.Combine(local, "FiveM", "FiveM.app", "plugins"),
            Path.Combine(local, "FiveM", "plugins"),
            Path.Combine(local, "FiveM", "FiveM.app"),
            Path.Combine(local, "FiveM"),
        };

        foreach (var dir in roots.Where(Directory.Exists))
        {
            var dll = FindLoader(dir);
            if (dll is null) continue;

            // The ini usually sits beside the DLL, but with a plugins install it often lives one
            // level up in FiveM.app. Take whichever exists.
            var ini = FirstExisting(
                Path.Combine(dir, "ReShade.ini"),
                Path.Combine(Directory.GetParent(dir)?.FullName ?? dir, "ReShade.ini"));

            var iniDir = ini.Length > 0 ? Path.GetDirectoryName(ini)! : dir;

            return new Install(dir, dll, ini, !dll.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                PresetPath = ReadPresetPath(ini, iniDir)
            };
        }
        return null;
    }

    private static string? FindLoader(string dir)
    {
        foreach (var name in LoaderNames)
        {
            var active = Path.Combine(dir, name);
            if (File.Exists(active)) return active;

            var parked = active + ".disabled";
            if (File.Exists(parked)) return parked;
        }

        // Catches renamed builds such as ReShade64_FiveM.dll.
        try
        {
            return Directory.EnumerateFiles(dir, "*.dll*")
                .FirstOrDefault(f => Path.GetFileName(f).Contains("reshade", StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    private static string FirstExisting(params string[] paths)
        => paths.FirstOrDefault(File.Exists) ?? "";


    private static string ReadPresetPath(string ini, string dir)
    {
        if (!File.Exists(ini)) return "";
        var value = ReadKey(ini, "GENERAL", "PresetPath");
        if (string.IsNullOrWhiteSpace(value)) return "";
        return Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(dir, value));
    }

    /// <summary>
    /// PerformanceMode skips ReShade's per-frame uniform handling and shader recompiles. It is the
    /// single cheapest win available while still keeping the effects on.
    /// </summary>
    public static TweakResult EnablePerformanceMode(Install install)
    {
        if (string.IsNullOrEmpty(install.IniPath) || !File.Exists(install.IniPath))
            return new(false, "No ReShade.ini found yet. Launch FiveM once with ReShade loaded, then try again.");

        try
        {
            Backup(install.IniPath);
            WriteKey(install.IniPath, "GENERAL", "PerformanceMode", "1");
            WriteKey(install.IniPath, "GENERAL", "NoDebugInfo", "1");
            WriteKey(install.IniPath, "SCREENSHOT", "SaveBeforeShot", "0");
            return new(true, "Performance mode on. Effects still run, with less per-frame overhead.", 3);
        }
        catch (Exception ex) { return new(false, $"Could not edit ReShade.ini: {ex.Message}"); }
    }

    /// <summary>Removes the heaviest techniques from the active preset, keeping the cheap ones.</summary>
    public static TweakResult StripHeavyEffects(Install install)
    {
        var preset = install.PresetPath;
        if (string.IsNullOrEmpty(preset) || !File.Exists(preset))
            return new(false, "Could not find the active preset file that ReShade.ini points at.");

        try
        {
            Backup(preset);

            var lines = File.ReadAllLines(preset);
            var removed = 0;

            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("Techniques=", StringComparison.OrdinalIgnoreCase)) continue;

                var value = lines[i]["Techniques=".Length..];
                var kept = value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t =>
                    {
                        var heavy = Expensive.Any(x => t.Contains(x, StringComparison.OrdinalIgnoreCase));
                        if (heavy) removed++;
                        return !heavy;
                    })
                    .ToArray();

                lines[i] = "Techniques=" + string.Join(',', kept);
            }

            if (removed == 0)
                return new(true, "No expensive effects were enabled — nothing to strip.");

            File.WriteAllLines(preset, lines);
            return new(true, $"Removed {removed} expensive effect(s) from the preset. Restore puts them back.", removed);
        }
        catch (Exception ex) { return new(false, $"Could not edit the preset: {ex.Message}"); }
    }

    /// <summary>
    /// Renames the loader DLL rather than deleting it, so this is a toggle and never destructive.
    /// Off is always the largest FPS gain ReShade can give you.
    /// </summary>
    public static TweakResult SetEnabled(Install install, bool enabled)
    {
        try
        {
            var active = install.DllPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? install.DllPath[..^".disabled".Length]
                : install.DllPath;
            var parked = active + ".disabled";

            if (enabled)
            {
                if (File.Exists(active)) return new(true, "ReShade is already on.");
                if (!File.Exists(parked)) return new(false, "Could not find the parked ReShade DLL.");
                File.Move(parked, active);
                return new(true, "ReShade switched back on.", 1);
            }

            if (!File.Exists(active)) return new(true, "ReShade is already off.");
            if (File.Exists(parked)) File.Delete(parked);
            File.Move(active, parked);
            return new(true, "ReShade switched off. This is the biggest single FPS gain it can give.", 1);
        }
        catch (IOException)
        {
            return new(false, "The file is in use. Close FiveM completely and try again.");
        }
        catch (Exception ex) { return new(false, $"Could not toggle ReShade: {ex.Message}"); }
    }

    public static TweakResult Restore(Install install)
    {
        var restored = 0;
        foreach (var file in new[] { install.IniPath, install.PresetPath })
        {
            if (string.IsNullOrEmpty(file)) continue;
            var bak = file + BackupSuffix;
            if (!File.Exists(bak)) continue;
            try { File.Copy(bak, file, overwrite: true); restored++; } catch { /* reported below */ }
        }

        return restored == 0
            ? new(false, "No ReShade backups exist yet.")
            : new(true, $"Restored {restored} ReShade file(s) from backup.", restored);
    }

    private static void Backup(string path)
    {
        var bak = path + BackupSuffix;
        if (!File.Exists(bak)) File.Copy(path, bak);
    }

    // --- Minimal INI handling. ReShade writes plain "key=value" under "[SECTION]" headers. -------

    private static string ReadKey(string path, string section, string key)
    {
        var inSection = false;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inSection = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (inSection && line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                return line[(key.Length + 1)..].Trim();
        }
        return "";
    }

    private static void WriteKey(string path, string section, string key, string value)
    {
        var lines = File.ReadAllLines(path).ToList();
        var sectionAt = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));

        if (sectionAt < 0)
        {
            lines.Add($"[{section}]");
            lines.Add($"{key}={value}");
            File.WriteAllLines(path, lines);
            return;
        }

        for (var i = sectionAt + 1; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith('[')) { lines.Insert(i, $"{key}={value}"); break; }
            if (lines[i].TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"{key}={value}";
                break;
            }
            if (i == lines.Count - 1) lines.Add($"{key}={value}");
        }

        File.WriteAllLines(path, lines);
    }
}
