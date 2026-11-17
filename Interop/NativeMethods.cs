using System;
using System.Runtime.InteropServices;

namespace AuraWall.Interop;

internal static class NativeMethods
{
    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;

    public const long WS_POPUP = 0x80000000L, WS_CHILD = 0x40000000L, WS_VISIBLE = 0x10000000L,
        WS_CAPTION = 0x00C00000L, WS_THICKFRAME = 0x00040000L, WS_SYSMENU = 0x00080000L;
    public const long WS_EX_TRANSPARENT = 0x00000020L, WS_EX_TOOLWINDOW = 0x00000080L, WS_EX_NOACTIVATE = 0x08000000L;

    public const uint SMTO_NORMAL = 0x0;
    public const uint SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020, SWP_SHOWWINDOW = 0x0040;
    public const int SW_HIDE = 0;
    public const uint SPI_SETDESKWALLPAPER = 0x0014, SPIF_UPDATEINIFILE = 0x1, SPIF_SENDCHANGE = 0x2;

    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public static readonly IntPtr HWND_BOTTOM = new(1);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? cls, string? title);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam,
        uint flags, uint timeoutMs, out UIntPtr result);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int cmd);

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT pt);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode)]
    public static extern bool SystemParametersInfo(uint action, uint param, string? pvParam, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int state);

    [DllImport("wtsapi32.dll")]
    public static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int flags);

    [DllImport("wtsapi32.dll")]
    public static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    public static long GetStyle(IntPtr h, int idx) => GetWindowLongPtr(h, idx).ToInt64();
    public static void SetStyle(IntPtr h, int idx, long v) => SetWindowLongPtr(h, idx, new IntPtr(v));
}
