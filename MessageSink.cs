using System;
using System.Windows.Forms;
using AuraWall.Interop;

namespace AuraWall;

/// <summary>
/// Hidden top-level (not message-only, so it receives broadcasts) window that handles the global hotkey,
/// session lock/unlock (WTS), display changes and Explorer restarts.
/// </summary>
internal sealed class MessageSink : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312, WM_WTSSESSION_CHANGE = 0x02B1, WM_DISPLAYCHANGE = 0x007E;
    private const int WTS_SESSION_LOCK = 7, WTS_SESSION_UNLOCK = 8;
    private const uint MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

    private readonly int _taskbarCreated;
    public event Action? HotkeyPressed, SessionLocked, SessionUnlocked, DisplayChanged, ExplorerRestarted;

    public MessageSink()
    {
        CreateHandle(new CreateParams { Caption = "AuraWallSink" });
        _taskbarCreated = (int)NativeMethods.RegisterWindowMessage("TaskbarCreated");
        NativeMethods.RegisterHotKey(Handle, 1, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, 0x56); // Ctrl+Shift+V
        NativeMethods.WTSRegisterSessionNotification(Handle, 0);                                // this session only
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY) HotkeyPressed?.Invoke();
        else if (m.Msg == WM_WTSSESSION_CHANGE)
        {
            int code = m.WParam.ToInt32();
            if (code == WTS_SESSION_LOCK) SessionLocked?.Invoke();
            else if (code == WTS_SESSION_UNLOCK) SessionUnlocked?.Invoke();
        }
        else if (m.Msg == WM_DISPLAYCHANGE) DisplayChanged?.Invoke();
        else if (m.Msg == _taskbarCreated) ExplorerRestarted?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        NativeMethods.UnregisterHotKey(Handle, 1);
        NativeMethods.WTSUnRegisterSessionNotification(Handle);
        DestroyHandle();
    }
}
