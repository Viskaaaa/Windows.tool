using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FiveMTweaks.Services;

/// <summary>
/// Notification-area icon built on Shell_NotifyIcon directly.
///
/// Deliberately not System.Windows.Forms.NotifyIcon: this app has already been bitten twice by
/// framework assemblies failing to resolve at runtime in the published bundle, and pulling in the
/// whole of WinForms for one icon is a third chance to hit that. This needs shell32 and user32,
/// both of which are always present.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const int NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_INFO = 0x10;
    private const int WM_APP = 0x8000, CallbackMessage = WM_APP + 1;
    private const int WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(int message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, int count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private readonly HwndSource _source;
    private NotifyIconData _data;
    private IntPtr _icon;
    private bool _added;

    /// <summary>Left click or double click on the icon.</summary>
    public event Action? Activated;

    /// <summary>Right click - the caller decides what menu to show.</summary>
    public event Action? ContextRequested;

    public TrayIcon(string tooltip)
    {
        // A message-only window: never shown, exists purely to receive the icon's callbacks.
        _source = new HwndSource(new HwndSourceParameters("FiveMTweaksTray")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3)   // HWND_MESSAGE
        });
        _source.AddHook(WndProc);

        _icon = LoadOwnIcon();

        _data = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _source.Handle,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            szTip = Truncate(tooltip, 127),
            szInfo = "",
            szInfoTitle = ""
        };

        _added = Shell_NotifyIconW(NIM_ADD, ref _data);
    }

    /// <summary>Uses the exe's own icon, so the tray matches the taskbar and the window.</summary>
    private static IntPtr LoadOwnIcon()
    {
        try
        {
            var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exe)) return IntPtr.Zero;

            ExtractIconExW(exe, 0, out var large, out var small, 1);
            if (small != IntPtr.Zero)
            {
                if (large != IntPtr.Zero) DestroyIcon(large);
                return small;
            }
            return large;
        }
        catch { return IntPtr.Zero; }
    }

    public void UpdateTooltip(string tooltip)
    {
        if (!_added) return;
        _data.szTip = Truncate(tooltip, 127);
        _data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        Shell_NotifyIconW(NIM_MODIFY, ref _data);
    }

    /// <summary>Balloon notification. Windows may suppress it if the user has them switched off.</summary>
    public void Notify(string title, string message)
    {
        if (!_added) return;
        _data.uFlags = NIF_INFO;
        _data.szInfoTitle = Truncate(title, 63);
        _data.szInfo = Truncate(message, 255);
        _data.dwInfoFlags = 0;
        Shell_NotifyIconW(NIM_MODIFY, ref _data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != CallbackMessage) return IntPtr.Zero;

        switch ((int)lParam)
        {
            case WM_LBUTTONUP:
            case WM_LBUTTONDBLCLK:
                Activated?.Invoke();
                handled = true;
                break;
            case WM_RBUTTONUP:
                ContextRequested?.Invoke();
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public void Dispose()
    {
        if (_added)
        {
            Shell_NotifyIconW(NIM_DELETE, ref _data);
            _added = false;
        }
        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
        _source.Dispose();
    }
}
