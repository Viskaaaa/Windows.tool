using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;

namespace ViskaTweak.Services;

/// <summary>
/// Watches FiveM in the background and keeps a history of play sessions.
///
/// What it cannot do, stated plainly: this reads process counters from outside the game. It is not
/// an overlay and does not hook the renderer, so it reports memory, CPU and uptime - never in-game
/// FPS. Anything claiming a real FPS figure from out here would be making it up.
/// </summary>
public sealed class FiveMWatcher : IDisposable
{
    private static readonly string[] ProcessNames =
        { "FiveM", "FiveM_GTAProcess", "FiveM_b2699_GTAProcess", "FiveM_b3095_GTAProcess", "FiveM_b2802_GTAProcess" };

    private static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ViskaTweak", "sessions.json");

    public sealed record Session(DateTime Started, DateTime Ended, long PeakMemoryBytes)
    {
        public TimeSpan Duration => Ended - Started;
    }

    public sealed record Status(
        bool Running,
        TimeSpan Uptime,
        long MemoryBytes,
        long PeakMemoryBytes,
        double CpuPercent,
        int ProcessCount);

    private readonly DispatcherTimer _timer;
    private readonly int _cpuCount = Math.Max(1, Environment.ProcessorCount);

    private DateTime? _sessionStart;
    private long _peakMemory;
    private TimeSpan _lastCpu;
    private DateTime _lastSampleAt;

    public Status Current { get; private set; } = new(false, TimeSpan.Zero, 0, 0, 0, 0);

    /// <summary>Fires on every sample, and once more when a session ends.</summary>
    public event Action<Status>? Updated;

    public FiveMWatcher(TimeSpan interval)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = interval };
        _timer.Tick += (_, _) => Sample();
    }

    public void Start()
    {
        Sample();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void Sample()
    {
        var procs = ProcessNames
            .SelectMany(n => { try { return Process.GetProcessesByName(n); } catch { return Array.Empty<Process>(); } })
            .ToList();

        try
        {
            if (procs.Count == 0)
            {
                if (_sessionStart is { } started) EndSession(started);
                Current = new Status(false, TimeSpan.Zero, 0, 0, 0, 0);
                Updated?.Invoke(Current);
                return;
            }

            long memory = 0;
            var cpu = TimeSpan.Zero;
            var start = DateTime.Now;

            foreach (var p in procs)
            {
                try
                {
                    memory += p.WorkingSet64;
                    cpu += p.TotalProcessorTime;
                    if (p.StartTime < start) start = p.StartTime;
                }
                catch { /* exited between the listing and the read */ }
            }

            _sessionStart ??= start;
            _peakMemory = Math.Max(_peakMemory, memory);

            // CPU time delta over wall-clock delta, divided by core count.
            var now = DateTime.UtcNow;
            double cpuPercent = 0;
            if (_lastSampleAt != default)
            {
                var wall = (now - _lastSampleAt).TotalSeconds;
                if (wall > 0)
                    cpuPercent = Math.Clamp((cpu - _lastCpu).TotalSeconds / wall / _cpuCount * 100, 0, 100);
            }
            _lastCpu = cpu;
            _lastSampleAt = now;

            Current = new Status(true, DateTime.Now - _sessionStart.Value, memory, _peakMemory,
                                 cpuPercent, procs.Count);
            Updated?.Invoke(Current);
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
        }
    }

    private void EndSession(DateTime started)
    {
        var session = new Session(started, DateTime.Now, _peakMemory);
        _sessionStart = null;
        _peakMemory = 0;
        _lastCpu = TimeSpan.Zero;
        _lastSampleAt = default;

        // Sessions under a minute are almost always a failed launch, not play time.
        if (session.Duration < TimeSpan.FromMinutes(1)) return;

        var all = LoadSessions();
        all.Insert(0, session);
        if (all.Count > 100) all.RemoveRange(100, all.Count - 100);
        SaveSessions(all);
    }

    public static List<Session> LoadSessions()
    {
        try
        {
            return File.Exists(SessionPath)
                ? JsonSerializer.Deserialize<List<Session>>(File.ReadAllText(SessionPath)) ?? new()
                : new();
        }
        catch { return new(); }
    }

    private static void SaveSessions(List<Session> sessions)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
            File.WriteAllText(SessionPath, JsonSerializer.Serialize(sessions,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* history is a convenience */ }
    }

    public void Dispose() => _timer.Stop();
}
