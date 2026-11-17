using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using AuraWall.Audio;
using AuraWall.Interop;
using AuraWall.Media;
using AuraWall.Rendering;

namespace AuraWall;

/// <summary>Borderless render window. Lives behind the icons in Wallpaper Mode, becomes a normal window in Full-Screen Mode.</summary>
internal sealed class MainWindow : Window
{
    private readonly AppSettings _s;
    private readonly LoopbackAnalyzer _audio;
    private readonly MediaWatcher _media;
    private readonly Visualizer _viz = new();
    private readonly SKElement _canvas = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _last;

    public IntPtr Hwnd { get; private set; }
    public bool IsFullscreen { get; private set; }
    public bool Paused { get; set; }
    public event Action? ExitFullscreenRequested;

    public MainWindow(AppSettings s, LoopbackAnalyzer audio, MediaWatcher media)
    {
        _s = s; _audio = audio; _media = media;
        Title = "AuraWall";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        Width = 320; Height = 180;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000; Top = -32000;
        Content = _canvas;

        _canvas.PaintSurface += OnPaint;
        CompositionTarget.Rendering += OnTick;      // fires at the display refresh rate (vsync)
        PreviewKeyDown += OnKeyDown;
    }

    public void Start()
    {
        Show();
        Hwnd = new WindowInteropHelper(this).Handle;
        EnterWallpaperMode();
    }

    private System.Drawing.Rectangle Bounds => Monitors.GetBounds(_s.MonitorIndex);

    public void EnterWallpaperMode()
    {
        IsFullscreen = false;
        Topmost = false;
        Cursor = null;
        if (!WallpaperHost.Attach(Hwnd, Bounds))
            WallpaperHost.PlaceTopLevel(Hwnd, Bounds);   // Explorer not ready: fall back to a plain window
    }

    public void EnterFullscreenMode()
    {
        WallpaperHost.Detach(Hwnd);
        IsFullscreen = true;
        WallpaperHost.PlaceTopLevel(Hwnd, Bounds);
        Activate();
        Focus();
        NativeMethods.SetForegroundWindow(Hwnd);
    }

    /// <summary>Re-apply placement after monitor changes, monitor selection changes or an Explorer restart.</summary>
    public void Reapply()
    {
        if (IsFullscreen) WallpaperHost.PlaceTopLevel(Hwnd, Bounds);
        else { WallpaperHost.Detach(Hwnd); EnterWallpaperMode(); }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (Paused || !IsVisible) return;
        _canvas.InvalidateVisual();
    }

    private void OnPaint(object? sender, SKPaintSurfaceEventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Min(now - _last, 0.1);
        _last = now;
        _viz.Render(e.Surface.Canvas, e.Info.Width, e.Info.Height, dt, _audio, _s, _media.Current);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsFullscreen) return;
        switch (e.Key)
        {
            case Key.Escape: ExitFullscreenRequested?.Invoke(); return;
            case Key.D1: _s.Preset = VisualizerPreset.RadialSpectrum; break;
            case Key.D2: _s.Preset = VisualizerPreset.FluidWaveforms; break;
            case Key.D3: _s.Preset = VisualizerPreset.ParticleField; break;
            case Key.Tab:
            case Key.Right: _s.Preset = (VisualizerPreset)(((int)_s.Preset + 1) % 3); break;
            case Key.Left: _s.Preset = (VisualizerPreset)(((int)_s.Preset + 2) % 3); break;
            case Key.Up: _s.Sensitivity = Math.Min(3f, _s.Sensitivity + 0.1f); break;
            case Key.Down: _s.Sensitivity = Math.Max(0.1f, _s.Sensitivity - 0.1f); break;
            case Key.H: _s.ShowClock = !_s.ShowClock; break;
            case Key.M: _s.ShowMedia = !_s.ShowMedia; break;
            default: return;
        }
        e.Handled = true;
        _s.Save();
    }

    public void Teardown()
    {
        CompositionTarget.Rendering -= OnTick;
        WallpaperHost.Detach(Hwnd);
        Close();
        _viz.Dispose();
    }
}
