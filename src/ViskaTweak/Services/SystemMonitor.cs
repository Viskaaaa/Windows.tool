using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ViskaTweak.Services;

/// <summary>
/// Live CPU and memory load, sampled on a timer. Uses the kernel counters directly rather than
/// PerformanceCounter, which needs an extra package and is slow to initialise on some machines.
/// </summary>
public sealed class SystemMonitor : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys, AvailPhys;
        public ulong TotalPageFile, AvailPageFile;
        public ulong TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    private readonly DispatcherTimer _timer;
    private long _lastIdle, _lastKernel, _lastUser;
    private bool _primed;

    /// <summary>CPU load 0-100, memory load 0-100, memory used in bytes.</summary>
    public event Action<double, double, long>? Sampled;

    public SystemMonitor(TimeSpan interval)
    {
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => Sample();
    }

    public void Start()
    {
        Sample();       // primes the CPU delta; the first reading is discarded
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void Sample()
    {
        double cpu = 0;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var dIdle = idle - _lastIdle;
            var dKernel = kernel - _lastKernel;
            var dUser = user - _lastUser;
            _lastIdle = idle; _lastKernel = kernel; _lastUser = user;

            // Kernel time already includes idle, so total busy = kernel + user - idle.
            var total = dKernel + dUser;
            if (_primed && total > 0)
                cpu = Math.Clamp((total - dIdle) * 100.0 / total, 0, 100);
            _primed = true;
        }

        double ramPercent = 0; long ramUsed = 0;
        var mem = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            ramPercent = mem.MemoryLoad;
            ramUsed = (long)(mem.TotalPhys - mem.AvailPhys);
        }

        Sampled?.Invoke(cpu, ramPercent, ramUsed);
    }

    public void Dispose() => _timer.Stop();
}
