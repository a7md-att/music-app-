using System;
using System.Globalization;
using SkiaSharp;
using AuraWall.Audio;
using AuraWall.Media;

namespace AuraWall.Rendering;

/// <summary>Draws all presets, the camera pulse, smoothing/idle animation and the clock overlay.</summary>
internal sealed class Visualizer : IDisposable
{
    private const int Bins = AudioFrame.Bins;
    private const int WP = AudioFrame.WavePoints;

    private readonly AudioFrame _f = new();
    private readonly float[] _spec = new float[Bins];
    private readonly float[] _wl = new float[WP], _wr = new float[WP];
    private float _bass, _mid, _high, _prevBassTarget, _t;
    private bool _hit;
    private readonly Random _rng = new(7);

    private struct Particle { public float X, Y, VX, VY, Life, Size, Hue; public bool Alive; }
    private readonly Particle[] _p = new Particle[500];
    private float _emitAcc;

    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _blur = new() { IsAntialias = true, Style = SKPaintStyle.Fill, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 24f) };
    private SKTypeface? _tf;
    private int _tfWeight = -1;

    private static SKColor Hsv(float h, float s, float v, byte a = 255)
    {
        h %= 360f; if (h < 0) h += 360f;
        return SKColor.FromHsv(h, s, v, a);
    }

    private static float Smooth(float cur, float target, float dt, float attack, float release)
    {
        float k = target > cur ? attack : release;                 // fast attack, slow release
        return cur + (target - cur) * (1f - MathF.Exp(-k * dt));   // frame-rate independent EMA
    }

    public void Render(SKCanvas c, int w, int h, float dt, LoopbackAnalyzer audio, AppSettings s, MediaSnapshot? media)
    {
        audio.Snapshot(_f);
        Update(dt);

        c.Clear(new SKColor(6, 7, 12));
        float hue = (_t * 7f + _bass * 30f) % 360f;
        DrawBackground(c, w, h, hue);

        // "Camera": bass-driven scale pulse and shake
        c.Save();
        float shake = MathF.Max(0f, _bass - 0.45f) * 14f;
        float sx = ((float)_rng.NextDouble() - 0.5f) * shake, sy = ((float)_rng.NextDouble() - 0.5f) * shake;
        c.Translate(w / 2f + sx, h / 2f + sy);
        c.Scale(1f + _bass * 0.035f);
        c.Translate(-w / 2f, -h / 2f);
        switch (s.Preset)
        {
            case VisualizerPreset.RadialSpectrum: DrawRadial(c, w, h, hue); break;
            case VisualizerPreset.FluidWaveforms: DrawWaves(c, w, h, hue); break;
            default: DrawParticles(c, w, h, hue, dt); break;
        }
        c.Restore();

        if (s.ShowClock) DrawClock(c, w, h, s, media);
    }

    private void Update(float dt)
    {
        _t += dt;
        bool on = _f.Active;
        float breath = 0.5f + 0.5f * MathF.Sin(_t * 1.1f);

        float bT = on ? _f.Bass : 0.14f + 0.10f * breath;           // idle breathing when silent
        float mT = on ? _f.Mid : 0.10f + 0.05f * MathF.Sin(_t * 0.7f + 1f);
        float hT = on ? _f.High : 0.05f + 0.03f * breath;
        _hit = on && bT - _prevBassTarget > 0.18f;
        _prevBassTarget = bT;

        _bass = Smooth(_bass, bT, dt, 30f, 6f);
        _mid = Smooth(_mid, mT, dt, 25f, 7f);
        _high = Smooth(_high, hT, dt, 30f, 9f);

        for (int i = 0; i < Bins; i++)
        {
            float t = on ? _f.Spectrum[i] : 0.07f + 0.06f * MathF.Sin(_t * 1.3f + i * 0.18f);
            _spec[i] = Smooth(_spec[i], t, dt, 35f, 7f);
        }
        for (int i = 0; i < WP; i++)
        {
            _wl[i] = Smooth(_wl[i], on ? _f.Left[i] : 0f, dt, 30f, 15f);
            _wr[i] = Smooth(_wr[i], on ? _f.Right[i] : 0f, dt, 30f, 15f);
        }
    }

    private void DrawBackground(SKCanvas c, int w, int h, float hue)
    {
        using var sh = SKShader.CreateRadialGradient(
            new SKPoint(w / 2f, h / 2f), MathF.Max(w, h) * (0.45f + _bass * 0.25f),
            new[] { Hsv(hue, 70, 100, (byte)(30 + _bass * 110)), SKColors.Transparent },
            null, SKShaderTileMode.Clamp);
        _fill.Shader = sh;
        c.DrawRect(0, 0, w, h, _fill);
        _fill.Shader = null;
    }

    // ---------- Preset 1: Radial spectrum ----------
    private void DrawRadial(SKCanvas c, int w, int h, float hue)
    {
        float cx = w / 2f, cy = h / 2f, m = MathF.Min(w, h);
        float r0 = m * 0.17f * (1f + _bass * 0.30f);

        _blur.Color = Hsv(hue, 80, 100, (byte)(70 + _bass * 150));
        c.DrawCircle(cx, cy, r0 * (0.9f + _bass * 0.25f), _blur);

        using (var core = SKShader.CreateRadialGradient(new SKPoint(cx, cy), r0,
                   new[] { Hsv(hue + 20, 40, 100), Hsv(hue, 90, 70) }, null, SKShaderTileMode.Clamp))
        {
            _fill.Shader = core;
            c.DrawCircle(cx, cy, r0 * 0.92f, _fill);
            _fill.Shader = null;
        }

        const int n = 96;
        float sw = MathF.Max(3f, m * 0.0055f);
        for (int i = 0; i < n; i++)
        {
            int bi = i < 48 ? i : 95 - i;                           // mirror: lows at top
            float v = _spec[bi * 2];
            float len = v * m * 0.24f + m * 0.004f;
            float ang = i / (float)n * MathF.Tau - MathF.PI / 2f + _t * 0.04f;
            float cs = MathF.Cos(ang), sn = MathF.Sin(ang);
            var col = Hsv(hue + bi * 3.2f, 75, 100);
            float x1 = cx + cs * r0, y1 = cy + sn * r0, x2 = cx + cs * (r0 + len), y2 = cy + sn * (r0 + len);

            _stroke.Color = col.WithAlpha(55); _stroke.StrokeWidth = sw * 3f;
            c.DrawLine(x1, y1, x2, y2, _stroke);
            _stroke.Color = col; _stroke.StrokeWidth = sw;
            c.DrawLine(x1, y1, x2, y2, _stroke);
        }
        _stroke.Color = SKColors.White.WithAlpha(120); _stroke.StrokeWidth = 2f;
        c.DrawCircle(cx, cy, r0 * 0.97f, _stroke);
    }

    // ---------- Preset 2: Fluid waveforms ----------
    private void DrawWaves(SKCanvas c, int w, int h, float hue)
    {
        const int layers = 5;
        for (int j = layers - 1; j >= 0; j--)
        {
            float[] src = (j & 1) == 0 ? _wl : _wr;                 // stereo: even layers L, odd layers R
            float amp = h * (0.10f + _mid * 0.22f) * (1f - j * 0.13f);
            float phase = _t * (0.6f + j * 0.25f) + j * 1.7f;
            using var path = new SKPath();
            float px = 0, py = 0;
            for (int i = 0; i < WP; i++)
            {
                float u = i / (float)(WP - 1);
                float x = u * w;
                float env = MathF.Sin(u * MathF.PI);
                float y = h * 0.5f + (src[i] * amp * 1.6f +
                          MathF.Sin(u * MathF.Tau * (1.5f + j * 0.4f) + phase) * amp * 0.35f * (0.4f + _bass)) * env;
                if (i == 0) path.MoveTo(x, y);
                else path.QuadTo(px, py, (px + x) / 2f, (py + y) / 2f);
                px = x; py = y;
            }
            path.LineTo(px, py);

            var col = Hsv(hue + j * 28f, 70, 100);
            _stroke.Color = col.WithAlpha(40); _stroke.StrokeWidth = 14f;
            c.DrawPath(path, _stroke);
            _stroke.Color = col.WithAlpha((byte)(255 - j * 30)); _stroke.StrokeWidth = 3.5f;
            c.DrawPath(path, _stroke);
        }
    }

    // ---------- Preset 3: Ambient particle field ----------
    private void DrawParticles(SKCanvas c, int w, int h, float hue, float dt)
    {
        float m = MathF.Min(w, h);
        _emitAcc += dt * (25f + _high * 420f);                     // highs drive emission
        int emit = (int)_emitAcc; _emitAcc -= emit;
        if (_hit) emit += 45;                                       // bass hit = burst
        float speed = 0.35f + _bass * 3.2f;                         // bass drives velocity
        float jitter = _high * 60f;                                 // highs drive fine jitter

        _fill.BlendMode = SKBlendMode.Plus;
        for (int i = 0; i < _p.Length; i++)
        {
            ref var p = ref _p[i];
            if (!p.Alive)
            {
                if (emit <= 0) continue;
                emit--;
                float ang = (float)_rng.NextDouble() * MathF.Tau;
                float spd = m * (0.02f + (float)_rng.NextDouble() * 0.05f);
                p.X = (float)_rng.NextDouble() * w; p.Y = (float)_rng.NextDouble() * h;
                p.VX = MathF.Cos(ang) * spd; p.VY = MathF.Sin(ang) * spd - m * 0.015f;
                p.Life = 0.5f + (float)_rng.NextDouble() * 0.9f;
                p.Size = m * (0.0015f + (float)_rng.NextDouble() * 0.004f);
                p.Hue = (float)_rng.NextDouble() * 80f - 40f;
                p.Alive = true;
            }
            p.Life -= dt * 0.18f;
            if (p.Life <= 0) { p.Alive = false; continue; }
            p.X += (p.VX * speed + ((float)_rng.NextDouble() - 0.5f) * jitter) * dt;
            p.Y += (p.VY * speed + ((float)_rng.NextDouble() - 0.5f) * jitter) * dt;
            if (p.X < -20 || p.X > w + 20 || p.Y < -20 || p.Y > h + 20) { p.Alive = false; continue; }

            float a = Math.Clamp(MathF.Min(1f, p.Life * 2f) * (0.35f + _bass * 0.65f), 0f, 1f);
            float r = p.Size * (1f + _bass * 0.8f);
            _fill.Color = Hsv(hue + p.Hue, 60, 100, (byte)(a * 50));
            c.DrawCircle(p.X, p.Y, r * 3.5f, _fill);               // soft glow
            _fill.Color = Hsv(hue + p.Hue, 40, 100, (byte)(a * 230));
            c.DrawCircle(p.X, p.Y, r, _fill);
        }
        _fill.BlendMode = SKBlendMode.SrcOver;
    }

    // ---------- Clock + media overlay ----------
    private void EnsureTypeface(int weight)
    {
        if (_tf != null && _tfWeight == weight) return;
        _tf?.Dispose();
        _tf = SKTypeface.FromFamilyName("Segoe UI", new SKFontStyle(weight, (int)SKFontStyleWidth.Normal, SKFontStyleSlant.Upright));
        _tfWeight = weight;
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private void DrawClock(SKCanvas c, int w, int h, AppSettings s, MediaSnapshot? media)
    {
        EnsureTypeface(s.ClockWeight);
        var now = DateTime.Now;
        string time = s.Use24Hour ? now.ToString("HH:mm", CultureInfo.InvariantCulture)
                                  : now.ToString("h:mm tt", CultureInfo.InvariantCulture);
        string date = now.ToString("dddd, MMMM d");

        float big = h * 0.11f, mid = h * 0.024f, small = h * 0.018f, art = h * 0.06f;
        bool hasMedia = s.ShowMedia && media != null && (media.Title.Length > 0);
        float blockH = big * 0.8f + mid * 1.6f + (hasMedia ? (media!.Art != null ? art + small * 0.8f : 0) + small * 2.6f + mid * 0.6f : 0);

        int pos = (int)s.ClockPosition, col = pos % 3, row = pos / 3;
        float margin = h * 0.06f;
        float top = row == 0 ? margin : row == 1 ? (h - blockH) / 2f : h - margin - blockH;
        float ax = col == 0 ? margin : col == 1 ? w / 2f : w - margin;
        var align = col == 0 ? SKTextAlign.Left : col == 1 ? SKTextAlign.Center : SKTextAlign.Right;
        byte A(float k) => (byte)(255 * Math.Clamp(s.ClockOpacity * k, 0f, 1f));

        using var fBig = new SKFont(_tf, big);
        using var fMid = new SKFont(_tf, mid);
        using var fSmall = new SKFont(_tf, small);

        float y = top + big * 0.8f;
        _fill.Color = SKColors.White.WithAlpha(A(1f));
        c.DrawText(time, ax, y, align, fBig, _fill);
        y += mid * 1.5f;
        _fill.Color = SKColors.White.WithAlpha(A(0.75f));
        c.DrawText(date, ax, y, align, fMid, _fill);

        if (hasMedia)
        {
            y += mid * 0.6f;
            if (media!.Art != null)
            {
                float axl = col == 0 ? ax : col == 1 ? ax - art / 2f : ax - art;
                var dest = new SKRect(axl, y, axl + art, y + art);
                using var rr = new SKRoundRect(dest, art * 0.12f);
                c.Save();
                c.ClipRoundRect(rr, SKClipOperation.Intersect, true);
                _fill.Color = SKColors.White.WithAlpha(A(0.9f));
                c.DrawImage(media.Art, dest, _fill);
                c.Restore();
                y += art + small * 0.8f;
            }
            y += small * 1.1f;
            _fill.Color = SKColors.White.WithAlpha(A(0.8f));
            c.DrawText(Trim(media.Title, 48), ax, y, align, fSmall, _fill);
            y += small * 1.3f;
            string sub = media.Album.Length > 0 ? $"{media.Artist} — {media.Album}" : media.Artist;
            _fill.Color = SKColors.White.WithAlpha(A(0.55f));
            c.DrawText(Trim(sub, 56), ax, y, align, fSmall, _fill);
        }
    }

    public void Dispose()
    {
        _tf?.Dispose(); _fill.Dispose(); _stroke.Dispose(); _blur.Dispose();
    }
}
