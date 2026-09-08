using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FiveMTweaks.Models;

namespace FiveMTweaks.Services;

/// <summary>
/// Finds cache/junk folders. Nothing here deletes anything on its own — <see cref="Delete"/> is a
/// separate call the UI only makes after an explicit confirmation naming what will be removed.
/// </summary>
public static class CacheScanner
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>Folder names that mean "safe to clear" when found under a game install.</summary>
    private static readonly string[] CacheFolderNames =
        { "shadercache", "shader_cache", "shadercache_d3d12", "cache", "caches", "temp", "tmp", "logs", "crashes", "dumps" };

    public static async Task<List<JunkItem>> ScanAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        var items = new List<JunkItem>();

        void Add(string cat, string name, string path, string note = "", bool removeFolder = false)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            if (items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))) return;
            items.Add(new JunkItem { Category = cat, Name = name, Path = path, Note = note, DeleteFolderItself = removeFolder });
        }

        progress?.Report("Looking for FiveM…");
        foreach (var root in FiveMRoots())
        {
            Add("FiveM", "FiveM cache", Path.Combine(root, "data", "cache"),
                "Re-downloaded on the next server join. Safe, first join is slower.");
            Add("FiveM", "FiveM server assets", Path.Combine(root, "data", "server-cache"),
                "Cached server resources. Re-downloaded on join.");
            Add("FiveM", "FiveM priv server assets", Path.Combine(root, "data", "server-cache-priv"),
                "Cached server resources. Re-downloaded on join.");
            Add("FiveM", "FiveM NUI browser cache", Path.Combine(root, "data", "nui-storage"),
                "In-game browser cache. Some servers store UI preferences here.");
            Add("FiveM", "FiveM crash dumps", Path.Combine(root, "crashes"), "Crash reports. Nothing depends on them.");
            Add("FiveM", "FiveM logs", Path.Combine(root, "logs"), "Old log files.");
        }

        progress?.Report("Looking for Windows and GPU caches…");
        Add("Windows", "Windows temp files", Path.GetTempPath(),
            "Files in use are skipped automatically.");
        Add("Windows", "DirectX shader cache", Path.Combine(Local, "D3DSCache"),
            "Rebuilt by Windows. First launch of each game is slower afterwards.");
        Add("GPU", "NVIDIA shader cache", Path.Combine(Local, "NVIDIA", "DXCache"), "Rebuilt automatically.");
        Add("GPU", "NVIDIA GL cache", Path.Combine(Local, "NVIDIA", "GLCache"), "Rebuilt automatically.");
        Add("GPU", "NVIDIA program cache", Path.Combine(Roaming, "NVIDIA", "ComputeCache"), "Rebuilt automatically.");
        Add("GPU", "AMD shader cache", Path.Combine(Local, "AMD", "DxCache"), "Rebuilt automatically.");
        Add("GPU", "AMD GL cache", Path.Combine(Local, "AMD", "GLCache"), "Rebuilt automatically.");
        Add("GPU", "Intel shader cache", Path.Combine(Local, "Intel", "ShaderCache"), "Rebuilt automatically.");

        progress?.Report("Looking through game libraries…");
        foreach (var lib in SteamLibraries())
        {
            Add("Steam", "Steam shader cache", Path.Combine(lib, "shadercache"), "Rebuilt on next launch.");
            Add("Steam", "Steam download temp", Path.Combine(lib, "downloading"), "Leftovers from interrupted downloads.");

            // Pattern scan rather than a hardcoded game list, so this covers games we have never seen.
            var common = Path.Combine(lib, "common");
            if (!Directory.Exists(common)) continue;
            foreach (var game in SafeDirs(common))
            {
                ct.ThrowIfCancellationRequested();
                foreach (var sub in SafeDirs(game).Where(d => CacheFolderNames.Contains(Path.GetFileName(d).ToLowerInvariant())))
                    Add("Games", $"{Path.GetFileName(game)} — {Path.GetFileName(sub)}", sub,
                        "Matched by folder name. Check it before removing if the game is unfamiliar.");
            }
        }

        progress?.Report("Measuring sizes…");
        await Task.Run(() => Parallel.ForEach(items,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 },
            i => i.SizeBytes = DirectorySize(i.Path, ct)), ct);

        return items.Where(i => i.SizeBytes > 0)
                    .OrderByDescending(i => i.SizeBytes)
                    .ToList();
    }

    private static IEnumerable<string> FiveMRoots()
    {
        var candidates = new[]
        {
            Path.Combine(Local, "FiveM", "FiveM.app"),
            Path.Combine(Local, "FiveM"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "FiveM", "FiveM.app"),
        };
        foreach (var c in candidates.Where(Directory.Exists)) yield return c;
    }

    private static IEnumerable<string> SteamLibraries()
    {
        // Default installs plus every fixed drive's usual library path. libraryfolders.vdf parsing
        // would be tidier, but this covers the normal cases without a VDF parser.
        var roots = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps"),
        };
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            roots.Add(Path.Combine(d.RootDirectory.FullName, "SteamLibrary", "steamapps"));
            roots.Add(Path.Combine(d.RootDirectory.FullName, "Steam", "steamapps"));
            roots.Add(Path.Combine(d.RootDirectory.FullName, "Games", "SteamLibrary", "steamapps"));
        }
        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SafeDirs(string path)
    {
        try { return Directory.EnumerateDirectories(path); }
        catch { return Array.Empty<string>(); }
    }

    public static long DirectorySize(string path, CancellationToken ct = default)
    {
        long total = 0;
        var stack = new Stack<string>();
        stack.Push(path);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
                foreach (var d in Directory.EnumerateDirectories(dir)) stack.Push(d);
            }
            catch { /* access denied / vanished mid-scan — skip and keep going */ }
        }
        return total;
    }

    public sealed record DeleteReport(long FreedBytes, int FilesDeleted, List<string> Skipped);

    /// <summary>Deletes the given items. Call only after the user has confirmed this exact list.</summary>
    public static Task<DeleteReport> DeleteAsync(IEnumerable<JunkItem> items, IProgress<string>? progress, CancellationToken ct = default)
        => Task.Run(() =>
        {
            long freed = 0; int files = 0; var skipped = new List<string>();

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Cleaning {item.Name}…");
                if (!Directory.Exists(item.Path)) continue;

                foreach (var f in EnumerateFilesSafe(item.Path))
                {
                    try
                    {
                        var len = new FileInfo(f).Length;
                        File.SetAttributes(f, FileAttributes.Normal);
                        File.Delete(f);
                        freed += len; files++;
                    }
                    catch { skipped.Add(f); }   // locked by a running game/launcher — expected
                }

                foreach (var d in SafeDirs(item.Path).Reverse())
                {
                    try { Directory.Delete(d, true); } catch { skipped.Add(d); }
                }

                if (item.DeleteFolderItself)
                {
                    try { Directory.Delete(item.Path, true); } catch { skipped.Add(item.Path); }
                }

                item.SizeBytes = Directory.Exists(item.Path) ? DirectorySize(item.Path, ct) : 0;
            }

            return new DeleteReport(freed, files, skipped);
        }, ct);

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] files, dirs;
            try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); }
            catch { continue; }
            foreach (var d in dirs) stack.Push(d);
            foreach (var f in files) yield return f;
        }
    }
}
