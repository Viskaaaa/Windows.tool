using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace RigBooster.Services;

public enum AppTheme { Red, HighContrast }

/// <summary>Swaps the palette dictionary and rescales every font size, at runtime.</summary>
public static class ThemeService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RigBooster", "prefs.json");

    private static readonly Dictionary<string, double> BaseSizes = new()
    {
        ["Font.Small"] = 12, ["Font.Base"] = 14, ["Font.Nav"] = 15,
        ["Font.H2"] = 17, ["Font.H1"] = 22, ["Font.Huge"] = 40,
    };

    public static AppTheme Theme { get; private set; } = AppTheme.Red;
    public static double FontScale { get; private set; } = 1.0;

    private sealed record Prefs(string Theme, double FontScale);

    public static void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var p = JsonSerializer.Deserialize<Prefs>(File.ReadAllText(ConfigPath));
            if (p is null) return;
            Theme = Enum.TryParse<AppTheme>(p.Theme, out var t) ? t : AppTheme.Red;
            FontScale = Math.Clamp(p.FontScale, 0.85, 1.75);
        }
        catch { /* fall back to defaults */ }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new Prefs(Theme.ToString(), FontScale)));
        }
        catch { }
    }

    public static void Apply(AppTheme theme, double fontScale)
    {
        Theme = theme;
        FontScale = Math.Clamp(fontScale, 0.85, 1.75);

        var dicts = Application.Current.Resources.MergedDictionaries;
        var uri = new Uri(theme == AppTheme.HighContrast
            ? "pack://application:,,,/RigBooster;component/Themes/Palette.HighContrast.xaml"
            : "pack://application:,,,/RigBooster;component/Themes/Palette.Red.xaml", UriKind.Absolute);

        // Palette is always dictionary 0 (Controls.xaml stays after it and resolves by DynamicResource).
        var palette = new ResourceDictionary { Source = uri };
        if (dicts.Count == 0) dicts.Add(palette); else dicts[0] = palette;

        foreach (var (key, size) in BaseSizes)
            Application.Current.Resources[key] = Math.Round(size * FontScale);

        Save();
    }
}
