using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ViskaTweak.Services;

/// <summary>
/// A durable record of everything this tool changed on the PC: what, where, when, and whether it
/// can still be undone. Without it the app forgets its own history the moment it closes, and the
/// user has no way to answer "what did this thing actually do to my machine?".
/// </summary>
public static class JournalService
{
    public sealed record Entry(
        DateTime When,
        string Category,      // "Settings", "Cache", "Windows", "ReShade"
        string Action,
        string Target,        // file or registry path, so it can be checked by hand
        string Detail,
        bool Reversible);

    private static readonly string Path_ = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ViskaTweak", "journal.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>Cap so a long-lived install cannot grow the file without bound.</summary>
    private const int MaxEntries = 500;

    private static List<Entry>? _cache;

    /// <summary>Raised after any change, so open pages can refresh.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<Entry> All()
    {
        if (_cache is not null) return _cache;

        try
        {
            _cache = File.Exists(Path_)
                ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(Path_)) ?? new()
                : new();
        }
        catch
        {
            // A corrupt journal must never stop the app starting.
            _cache = new();
        }

        return _cache;
    }

    public static void Record(string category, string action, string target, string detail, bool reversible = true)
    {
        var list = (List<Entry>)All();
        list.Insert(0, new Entry(DateTime.Now, category, action, target, detail, reversible));

        if (list.Count > MaxEntries) list.RemoveRange(MaxEntries, list.Count - MaxEntries);

        Save();
        Changed?.Invoke();
    }

    public static void Clear()
    {
        _cache = new();
        Save();
        Changed?.Invoke();
    }

    /// <summary>Changes still on the machine that the user could still undo.</summary>
    public static int ReversibleCount() => All().Count(e => e.Reversible);

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
            File.WriteAllText(Path_, JsonSerializer.Serialize(_cache, Json));
        }
        catch { /* the journal is a record, never a blocker */ }
    }
}
