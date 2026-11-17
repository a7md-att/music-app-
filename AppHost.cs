using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AuraWall.Audio;
using AuraWall.Interop;
using AuraWall.Media;

namespace AuraWall;

/// <summary>Wires everything together and owns the lifecycle (including clean wallpaper restore on exit).</summary>
internal sealed class AppHost
{
    private AppSettings _s = null!;
    private LoopbackAnalyzer _audio = null!;
    private MediaWatcher _media = null!;
    private MainWindow _win = null!;
    private TrayController _tray = null!;
    private MessageSink _sink = null!;
    private DispatcherTimer _watch = null!;
    private bool _locked, _exited;

    public void Start()
    {
        _s = AppSettings.Load();
        _audio = new LoopbackAnalyzer(_s); _audio.Start();
        _media = new MediaWatcher(); _media.Start();

        _win = new MainWindow(_s, _audio, _media);
        _win.ExitFullscreenRequested += ToggleFullscreen;
        _win.Start();

        _sink = new MessageSink();
        _sink.HotkeyPressed += ToggleFullscreen;
        _sink.SessionLocked += () => { _locked = true; UpdatePause(); };
        _sink.SessionUnlocked += () => { _locked = false; UpdatePause(); };
        _sink.DisplayChanged += () => _win.Reapply();
        _sink.ExplorerRestarted += async () => { await Task.Delay(1500); _win.Reapply(); };

        _tray = new TrayController(_s, ToggleFullscreen, () => _win.Reapply(), Exit);

        // Performance safeguard: pause when a full-screen app/game is in front, or the PC is locked.
        _watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _watch.Tick += (_, _) => UpdatePause();
        _watch.Start();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => WallpaperHost.RestoreDesktopWallpaper();
    }

    private void ToggleFullscreen()
    {
        if (_win.IsFullscreen) _win.EnterWallpaperMode(); else _win.EnterFullscreenMode();
        UpdatePause();
    }

    private static bool ForegroundIsFullscreen()
    {
        // 2 = QUNS_BUSY (full-screen app), 3 = D3D full screen, 4 = presentation mode
        return NativeMethods.SHQueryUserNotificationState(out int st) == 0 && st is 2 or 3 or 4;
    }

    private void UpdatePause()
    {
        bool fs = !_win.IsFullscreen && ForegroundIsFullscreen();
        _win.Paused = _locked || fs;
        _audio.Enabled = !_win.Paused;
    }

    private void Exit()
    {
        if (_exited) return;
        _exited = true;
        _watch.Stop();
        _sink.Dispose();
        _tray.Dispose();
        _win.Teardown();                         // hides + SetParent(NULL) so WorkerW releases us
        _audio.Dispose();
        _media.Dispose();
        WallpaperHost.RestoreDesktopWallpaper(); // make Explorer repaint the real wallpaper
        System.Windows.Application.Current.Shutdown();
    }
}
