using System;
using System.Drawing;
using static AuraWall.Interop.NativeMethods;

namespace AuraWall.Interop;

/// <summary>
/// Attaches a window behind the desktop icons (Progman -> WorkerW) and detaches it again.
/// Handles both the classic layout (WorkerW is a top-level sibling of Progman) and the
/// Windows 11 24H2 layout (WorkerW is a child of Progman).
/// </summary>
internal static class WallpaperHost
{
    private static long _origStyle, _origExStyle;
    private static bool _attached;
    private static IntPtr _parent;

    public static bool IsAttached => _attached;

    private static IntPtr FindDesktopParent()
    {
        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;

        // Ask Progman to spawn the WorkerW behind the icons.
        SendMessageTimeout(progman, 0x052C, UIntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out _);

        // Windows 11 24H2+: SHELLDLL_DefView lives inside Progman, WorkerW is Progman's child.
        if (FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
        {
            IntPtr child = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
            return child != IntPtr.Zero ? child : progman;
        }

        // Classic: find the WorkerW that hosts SHELLDLL_DefView, take the WorkerW right after it.
        IntPtr workerW = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                workerW = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return true;
        }, IntPtr.Zero);

        return workerW != IntPtr.Zero ? workerW : progman;
    }

    public static bool Attach(IntPtr hwnd, Rectangle screenBounds)
    {
        IntPtr parent = FindDesktopParent();
        if (parent == IntPtr.Zero) return false;

        if (!_attached)
        {
            _origStyle = GetStyle(hwnd, GWL_STYLE);
            _origExStyle = GetStyle(hwnd, GWL_EXSTYLE);
        }

        long style = (_origStyle & ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU)) | WS_CHILD | WS_VISIBLE;
        long ex = _origExStyle | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
        SetStyle(hwnd, GWL_STYLE, style);
        SetStyle(hwnd, GWL_EXSTYLE, ex);

        SetParent(hwnd, parent);
        _parent = parent;
        _attached = true;
        Place(hwnd, screenBounds);
        return true;
    }

    /// <summary>Positions the attached window using screen coordinates converted to the parent's client space.</summary>
    public static void Place(IntPtr hwnd, Rectangle screenBounds)
    {
        var p = new POINT { X = screenBounds.X, Y = screenBounds.Y };
        ScreenToClient(_parent, ref p);
        SetWindowPos(hwnd, HWND_BOTTOM, p.X, p.Y, screenBounds.Width, screenBounds.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
    }

    /// <summary>Detaches from the desktop and restores a normal top-level window (full-screen mode and exit).</summary>
    public static void Detach(IntPtr hwnd)
    {
        if (!_attached) return;
        ShowWindow(hwnd, SW_HIDE);
        SetParent(hwnd, IntPtr.Zero);
        SetStyle(hwnd, GWL_STYLE, _origStyle);
        SetStyle(hwnd, GWL_EXSTYLE, _origExStyle & ~(WS_EX_NOACTIVATE | WS_EX_TRANSPARENT));
        _attached = false;
        _parent = IntPtr.Zero;
    }

    public static void PlaceTopLevel(IntPtr hwnd, Rectangle bounds)
    {
        SetWindowPos(hwnd, HWND_TOP, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            SWP_SHOWWINDOW | SWP_FRAMECHANGED);
    }

    /// <summary>Forces Explorer to repaint the user's real wallpaper after we leave.</summary>
    public static void RestoreDesktopWallpaper()
    {
        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, null, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }
}
