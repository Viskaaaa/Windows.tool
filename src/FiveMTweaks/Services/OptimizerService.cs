using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using FiveMTweaks.Models;

namespace FiveMTweaks.Services;

/// <summary>
/// Applies low-end presets to game settings files. Every write is preceded by a one-time backup
/// (<c>&lt;file&gt;.fivemtweaks.bak</c>) so <see cref="Restore"/> can always put the original back.
/// </summary>
public static class OptimizerService
{
    private const string BackupSuffix = ".fivemtweaks.bak";

    private static string Docs => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static List<GameTarget> DetectGames()
    {
        var list = new List<GameTarget>();

        string[] fivemSettings =
        {
            Path.Combine(Docs, "Rockstar Games", "FiveM Application Data", "settings.xml"),
            Path.Combine(Docs, "Rockstar Games", "GTA V", "settings.xml"),
        };
        var fivem = fivemSettings.FirstOrDefault(File.Exists);
        if (fivem is not null)
            list.Add(new GameTarget { Id = "fivem", DisplayName = "FiveM (GTA V graphics)", SettingsPath = fivem });

        var gta = Path.Combine(Docs, "Rockstar Games", "GTA V", "settings.xml");
        if (File.Exists(gta) && !string.Equals(gta, fivem, StringComparison.OrdinalIgnoreCase))
            list.Add(new GameTarget { Id = "gtav", DisplayName = "GTA V (story mode)", SettingsPath = gta });

        var cfg = Path.Combine(Roaming, "CitizenFX", "fivem.cfg");
        if (File.Exists(cfg))
            list.Add(new GameTarget { Id = "fivemcfg", DisplayName = "FiveM client config", SettingsPath = cfg });

        return list;
    }

    /// <summary>Values written into GTA V's settings.xml for each profile. value="…" on each node.</summary>
    private static Dictionary<string, string> Preset(PresetProfile profile) => profile switch
    {
        // Frame-time first. Everything that causes a spike is pinned to its cheapest value, even
        // where that costs more average FPS than the Low profile would: texture pressure on a small
        // VRAM pool, HD streaming, world variety and population are what produce the stutter people
        // actually feel. The result is a lower peak than Low, held far more consistently.
        PresetProfile.Stability => new()
        {
            ["TextureQuality"] = "0", ["ShaderQuality"] = "0", ["ShadowQuality"] = "0",
            ["ReflectionQuality"] = "0", ["ReflectionMSAA"] = "0", ["WaterQuality"] = "0",
            ["ParticleQuality"] = "0", ["GrassQuality"] = "0", ["PostFX"] = "0",
            ["MSAA"] = "0", ["SamplingMode"] = "0", ["FXAA_Enabled"] = "true",
            ["AnisotropicFiltering"] = "0", ["SSAO"] = "0", ["Tessellation"] = "0",
            ["DoF"] = "false", ["MotionBlurStrength"] = "0.000000",
            ["Shadow_SoftShadows"] = "0", ["UltraShadows_Enabled"] = "false",
            ["Shadow_ParticleShadows"] = "false", ["Shadow_LongShadows"] = "false",
            ["Shadow_Distance"] = "0.000000", ["Reflection_MipBlur"] = "false",

            // The anti-stutter block proper.
            ["HdStreamingInFlight"] = "false",     // stops mid-drive texture streaming hitches
            ["MaxLodScale"] = "0.000000",
            ["LodScale"] = "0.200000",
            ["PedLodBias"] = "0.000000", ["VehicleLodBias"] = "0.000000",
            ["CityDensity"] = "0.200000",
            ["PedVarietyMultiplier"] = "0.200000", ["VehicleVarietyMultiplier"] = "0.200000",
            ["ExtendedDistanceScale"] = "0.000000", ["ExtendedShadowsDistance"] = "0.000000",
            ["VSync"] = "0", ["PauseOnFocusLoss"] = "true",
        },

        PresetProfile.Low => new()
        {
            ["Tessellation"] = "0", ["LodScale"] = "0.300000", ["PedLodBias"] = "0.000000",
            ["VehicleLodBias"] = "0.000000", ["ShadowQuality"] = "0", ["ReflectionQuality"] = "0",
            ["ReflectionMSAA"] = "0", ["SSAO"] = "0", ["AnisotropicFiltering"] = "2",
            ["MSAA"] = "0", ["SamplingMode"] = "0", ["TextureQuality"] = "0",
            ["ParticleQuality"] = "0", ["WaterQuality"] = "0", ["GrassQuality"] = "0",
            ["ShaderQuality"] = "0", ["Shadow_SoftShadows"] = "0", ["UltraShadows_Enabled"] = "false",
            ["Shadow_ParticleShadows"] = "false", ["Shadow_LongShadows"] = "false",
            ["Reflection_MipBlur"] = "false", ["FXAA_Enabled"] = "true", ["MotionBlurStrength"] = "0.000000",
            ["PostFX"] = "0", ["DoF"] = "false", ["HdStreamingInFlight"] = "false",
            ["MaxLodScale"] = "0.000000", ["CityDensity"] = "0.400000",
            // Stutter, rather than average FPS: variety and streaming are what spike frame times
            // on a busy server, so the low profile cuts them hard.
            ["PedVarietyMultiplier"] = "0.300000", ["VehicleVarietyMultiplier"] = "0.300000",
            ["ExtendedDistanceScale"] = "0.000000", ["ExtendedShadowsDistance"] = "0.000000",
            ["VSync"] = "0", ["PauseOnFocusLoss"] = "true",
        },
        PresetProfile.Medium => new()
        {
            ["Tessellation"] = "1", ["LodScale"] = "0.700000", ["ShadowQuality"] = "1",
            ["ReflectionQuality"] = "1", ["SSAO"] = "1", ["AnisotropicFiltering"] = "4",
            ["MSAA"] = "0", ["TextureQuality"] = "1", ["ParticleQuality"] = "1",
            ["WaterQuality"] = "1", ["GrassQuality"] = "1", ["ShaderQuality"] = "1",
            ["PostFX"] = "1", ["MotionBlurStrength"] = "0.000000", ["CityDensity"] = "0.700000",
        },
        _ => new()
        {
            ["Tessellation"] = "2", ["LodScale"] = "1.000000", ["ShadowQuality"] = "2",
            ["ReflectionQuality"] = "2", ["SSAO"] = "2", ["AnisotropicFiltering"] = "8",
            ["TextureQuality"] = "2", ["ParticleQuality"] = "2", ["WaterQuality"] = "2",
            ["GrassQuality"] = "2", ["ShaderQuality"] = "2", ["PostFX"] = "2", ["CityDensity"] = "1.000000",
        }
    };

    public sealed record ApplyResult(bool Ok, string Message, int Changed);

    public static ApplyResult ApplyProfile(GameTarget game, PresetProfile profile)
    {
        if (!File.Exists(game.SettingsPath))
            return new(false, "That settings file is no longer there. Launch the game once, then rescan.", 0);

        if (game.Id == "fivemcfg")
            return new(false, "The client config is listed for reference only — this tool does not rewrite it.", 0);

        try
        {
            Backup(game.SettingsPath);

            // Rewritten with a targeted regex rather than an XML parser. Two reasons: the XDocument
            // assembly fails to resolve inside the single-file bundle, and settings.xml is a flat
            // machine-generated file of <Node value="x" /> lines, so a value swap leaves every other
            // byte - ordering, whitespace, unknown nodes - exactly as the game wrote it.
            var text = File.ReadAllText(game.SettingsPath);
            var preset = Preset(profile);
            int changed = 0;

            foreach (var (node, value) in preset)
                text = SetNodeValue(text, node, value, ref changed);

            if (changed == 0)
                return new(true, "Already matching the preset — nothing to change.", 0);

            File.WriteAllText(game.SettingsPath, text);
            return new(true, $"Applied the {profile} preset. {changed} settings changed. Original saved as a .bak next to it.", changed);
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, "Windows blocked the write. Close the game and try again.", 0);
        }
        catch (Exception ex)
        {
            return new(false, $"Could not apply the preset: {ex.Message}", 0);
        }
    }

    public static ApplyResult Restore(GameTarget game)
    {
        var bak = game.SettingsPath + BackupSuffix;
        if (!File.Exists(bak)) return new(false, "No backup exists for this game yet.", 0);
        try
        {
            File.Copy(bak, game.SettingsPath, overwrite: true);
            return new(true, "Original settings restored.", 0);
        }
        catch (Exception ex) { return new(false, $"Could not restore: {ex.Message}", 0); }
    }

    /// <summary>
    /// Replaces value="..." on a single node, by hand.
    ///
    /// This is deliberately written against nothing but string operations. Both XDocument and Regex
    /// failed to load at runtime on the target machine, and each one only surfaced when a user
    /// clicked the button. String is in the core library and cannot go missing, so this method
    /// cannot fail that way.
    /// </summary>
    private static string SetNodeValue(string text, string node, string value, ref int changed)
    {
        var search = "<" + node;
        var from = 0;

        while (true)
        {
            var tag = text.IndexOf(search, from, StringComparison.OrdinalIgnoreCase);
            if (tag < 0) return text;

            // Guard against <Shadow> matching <ShadowQuality>: the next character must end the name.
            var after = tag + search.Length;
            if (after >= text.Length || (text[after] != ' ' && text[after] != '\t' && text[after] != '>'))
            {
                from = tag + 1;
                continue;
            }

            var close = text.IndexOf('>', after);
            if (close < 0) return text;

            var attr = text.IndexOf("value=\"", after, StringComparison.OrdinalIgnoreCase);
            if (attr < 0 || attr > close) { from = close; continue; }

            var start = attr + "value=\"".Length;
            var end = text.IndexOf('"', start);
            if (end < 0 || end > close) { from = close; continue; }

            if (text[start..end] == value) { from = close; continue; }

            changed++;
            return text[..start] + value + text[end..];
        }
    }

    /// <summary>Backs up once and never overwrites — the first backup is the pristine one.</summary>
    private static void Backup(string path)
    {
        var bak = path + BackupSuffix;
        if (!File.Exists(bak)) File.Copy(path, bak);
    }

    // --- Optional Windows tweaks. Each is applied only when the user ticks it. ------------------

    public static ApplyResult EnableHighPerformancePowerPlan()
    {
        try
        {
            // 8c5e7fda-… is the built-in High performance GUID, present on every Windows SKU.
            var p = Process.Start(new ProcessStartInfo("powercfg", "/setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")
            { CreateNoWindow = true, UseShellExecute = false });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0
                ? new(true, "Power plan set to High performance.", 1)
                : new(false, "powercfg refused the change. On some laptops this plan is hidden by the vendor.", 0);
        }
        catch (Exception ex) { return new(false, $"Could not change the power plan: {ex.Message}", 0); }
    }

    public static ApplyResult EnableGameMode()
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar");
            k?.SetValue("AllowAutoGameMode", 1, RegistryValueKind.DWord);
            k?.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);
            return new(true, "Windows Game Mode turned on.", 1);
        }
        catch (Exception ex) { return new(false, $"Could not set Game Mode: {ex.Message}", 0); }
    }

    public static ApplyResult DisableGameDvr()
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore");
            k?.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
            return new(true, "Background game recording (Game DVR) turned off. Sign out and back in to apply.", 1);
        }
        catch (Exception ex) { return new(false, $"Could not turn off Game DVR: {ex.Message}", 0); }
    }

    /// <summary>Reverses the two registry tweaks above. The power plan is left to the user.</summary>
    public static ApplyResult RestoreWindowsTweaks()
    {
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore"))
                k?.SetValue("GameDVR_Enabled", 1, RegistryValueKind.DWord);
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar"))
                k?.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);
            return new(true, "Windows tweaks put back to their defaults. The power plan was left as-is.", 1);
        }
        catch (Exception ex) { return new(false, $"Could not restore: {ex.Message}", 0); }
    }
}
