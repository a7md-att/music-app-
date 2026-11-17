using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AuraWall;

internal sealed class TrayController : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly AppSettings _s;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly Action _monitorChanged;
    private readonly List<(ToolStripMenuItem item, Func<bool> isChecked)> _checks = new();
    private readonly ToolStripMenuItem _monitorMenu = new("Monitor");
    private readonly TrackBar _sens = new() { Minimum = 10, Maximum = 300, TickFrequency = 50, Width = 180 };

    public TrayController(AppSettings s, Action toggleFullscreen, Action monitorChanged, Action exit)
    {
        _s = s; _monitorChanged = monitorChanged;

        _menu.Items.Add(Item("Toggle Full-Screen Mode\tCtrl+Shift+V", toggleFullscreen));

        var preset = new ToolStripMenuItem("Visualizer Preset");
        AddRadio(preset, "Radial Spectrum", () => _s.Preset == VisualizerPreset.RadialSpectrum, () => _s.Preset = VisualizerPreset.RadialSpectrum);
        AddRadio(preset, "Fluid Waveforms", () => _s.Preset == VisualizerPreset.FluidWaveforms, () => _s.Preset = VisualizerPreset.FluidWaveforms);
        AddRadio(preset, "Ambient Particle Field", () => _s.Preset == VisualizerPreset.ParticleField, () => _s.Preset = VisualizerPreset.ParticleField);
        _menu.Items.Add(preset);

        _menu.Items.Add(new ToolStripLabel("Audio Sensitivity"));
        _sens.ValueChanged += (_, _) => _s.Sensitivity = _sens.Value / 100f;
        _menu.Items.Add(new ToolStripControlHost(_sens) { AutoSize = false, Width = 190, Height = 32 });

        var clock = new ToolStripMenuItem("Clock Settings");
        AddCheck(clock, "Show clock", () => _s.ShowClock, v => _s.ShowClock = v);
        AddCheck(clock, "24-hour format", () => _s.Use24Hour, v => _s.Use24Hour = v);
        AddCheck(clock, "Show now-playing info", () => _s.ShowMedia, v => _s.ShowMedia = v);
        var opacity = new ToolStripMenuItem("Opacity");
        foreach (var pct in new[] { 25, 50, 75, 100 })
            AddRadio(opacity, $"{pct}%", () => Math.Abs(_s.ClockOpacity - pct / 100f) < 0.13f, () => _s.ClockOpacity = pct / 100f);
        clock.DropDownItems.Add(opacity);
        var weight = new ToolStripMenuItem("Font weight");
        AddRadio(weight, "Light", () => _s.ClockWeight == 300, () => _s.ClockWeight = 300);
        AddRadio(weight, "Regular", () => _s.ClockWeight == 400, () => _s.ClockWeight = 400);
        AddRadio(weight, "Bold", () => _s.ClockWeight == 700, () => _s.ClockWeight = 700);
        clock.DropDownItems.Add(weight);
        var position = new ToolStripMenuItem("Position");
        foreach (ClockPosition p in Enum.GetValues(typeof(ClockPosition)))
        {
            var pos = p;
            AddRadio(position, pos.ToString(), () => _s.ClockPosition == pos, () => _s.ClockPosition = pos);
        }
        clock.DropDownItems.Add(position);
        _menu.Items.Add(clock);

        _menu.Items.Add(_monitorMenu);
        _menu.Items.Add(new ToolStripSeparator());
        AddCheck(_menu, "Start with Windows", IsAutostart, SetAutostart, saveSettings: false);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item("Exit", exit));

        _menu.Opening += (_, _) => Sync();
        _menu.Closed += (_, _) => _s.Save();

        _icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "AuraWall", Visible = true, ContextMenuStrip = _menu };
        _icon.DoubleClick += (_, _) => toggleFullscreen();
    }

    private static ToolStripMenuItem Item(string text, Action onClick)
    {
        var i = new ToolStripMenuItem(text);
        i.Click += (_, _) => onClick();
        return i;
    }

    private void AddRadio(ToolStripMenuItem parent, string text, Func<bool> isChecked, Action apply)
    {
        var i = Item(text, () => { apply(); _s.Save(); });
        parent.DropDownItems.Add(i);
        _checks.Add((i, isChecked));
    }

    private void AddCheck(ToolStripMenuItem parent, string text, Func<bool> isChecked, Action<bool> set)
    {
        var i = new ToolStripMenuItem(text);
        i.Click += (_, _) => { set(!isChecked()); _s.Save(); };
        parent.DropDownItems.Add(i);
        _checks.Add((i, isChecked));
    }

    private void AddCheck(ContextMenuStrip parent, string text, Func<bool> isChecked, Action<bool> set, bool saveSettings = true)
    {
        var i = new ToolStripMenuItem(text);
        i.Click += (_, _) => { set(!isChecked()); if (saveSettings) _s.Save(); };
        parent.Items.Add(i);
        _checks.Add((i, isChecked));
    }

    private void Sync()
    {
        foreach (var (item, isChecked) in _checks) item.Checked = isChecked();
        _sens.Value = Math.Clamp((int)(_s.Sensitivity * 100), _sens.Minimum, _sens.Maximum);

        _monitorMenu.DropDownItems.Clear();
        void Add(string text, int idx)
        {
            var i = Item(text, () => { _s.MonitorIndex = idx; _s.Save(); _monitorChanged(); });
            i.Checked = _s.MonitorIndex == idx;
            _monitorMenu.DropDownItems.Add(i);
        }
        Add("Primary monitor", -2);
        Add("All monitors (span)", -1);
        var names = Monitors.Describe();
        for (int i = 0; i < names.Length; i++) Add(names[i], i);
    }

    private static bool IsAutostart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue("AuraWall") != null;
    }

    private static void SetAutostart(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (k == null) return;
        if (on) k.SetValue("AuraWall", $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue("AuraWall", false);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
