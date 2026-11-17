using System;
using System.Threading.Tasks;
using NAudio.Dsp;
using NAudio.Wave;

namespace AuraWall.Audio;

public sealed class AudioFrame
{
    public const int Bins = 96;
    public const int WavePoints = 128;
    public float Bass, Mid, High;
    public bool Active;
    public readonly float[] Spectrum = new float[Bins];
    public readonly float[] Left = new float[WavePoints];
    public readonly float[] Right = new float[WavePoints];
}

/// <summary>WASAPI loopback capture of the default output device + Hann-windowed FFT.</summary>
public sealed class LoopbackAnalyzer : IDisposable
{
    private const int FftSize = 2048, FftM = 11, Mask = FftSize - 1;
    private const float MinDb = -75f, DbRange = 60f;

    private readonly AppSettings _settings;
    private readonly object _lock = new();
    private readonly float[] _l = new float[FftSize], _r = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly float[] _mapped = new float[FftSize / 2];
    private readonly float[] _spec = new float[AudioFrame.Bins];
    private readonly int[] _lo = new int[AudioFrame.Bins], _hi = new int[AudioFrame.Bins];
    private int _w, _bassHi, _midHi, _highHi;
    private float _bass, _mid, _high;
    private long _lastLoud;
    private WasapiLoopbackCapture? _capture;
    private WaveFormat? _fmt;
    private volatile bool _disposed;

    /// <summary>Set false while paused (locked / full-screen app) to skip the FFT work.</summary>
    public volatile bool Enabled = true;

    public LoopbackAnalyzer(AppSettings settings)
    {
        _settings = settings;
        for (int i = 0; i < FftSize; i++)
            _window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftSize - 1))); // Hann
    }

    public void Start()
    {
        try
        {
            _capture?.Dispose();
            _capture = new WasapiLoopbackCapture();           // default render endpoint
            _fmt = _capture.WaveFormat;
            BuildBins(_fmt.SampleRate);
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += OnStopped;           // device changed / removed
            _capture.StartRecording();
        }
        catch { RetryLater(); }                               // no audio device yet
    }

    private void OnStopped(object? s, StoppedEventArgs e) => RetryLater();

    private void RetryLater()
    {
        if (_disposed) return;
        Task.Run(async () => { await Task.Delay(2000); if (!_disposed) Start(); });
    }

    private void BuildBins(int sampleRate)
    {
        float df = sampleRate / (float)FftSize;
        const float fMin = 30f, fMax = 16000f;
        for (int i = 0; i < AudioFrame.Bins; i++)
        {
            float f0 = fMin * MathF.Pow(fMax / fMin, i / (float)AudioFrame.Bins);
            float f1 = fMin * MathF.Pow(fMax / fMin, (i + 1) / (float)AudioFrame.Bins);
            _lo[i] = Math.Max(1, (int)(f0 / df));
            _hi[i] = Math.Min(FftSize / 2 - 1, Math.Max(_lo[i], (int)(f1 / df)));
        }
        _bassHi = Math.Max(2, (int)(250f / df));
        _midHi = Math.Max(_bassHi + 1, (int)(4000f / df));
        _highHi = Math.Min(FftSize / 2 - 1, (int)(20000f / df));
    }

    private static float Read(byte[] b, int o, int bytes) => bytes switch
    {
        4 => BitConverter.ToSingle(b, o),
        2 => BitConverter.ToInt16(b, o) / 32768f,
        3 => ((b[o] << 8) | (b[o + 1] << 16) | (b[o + 2] << 24)) / 2147483648f,
        _ => 0f
    };

    private void OnData(object? s, WaveInEventArgs e)
    {
        if (!Enabled || _fmt == null || e.BytesRecorded == 0) return;
        int ch = _fmt.Channels, bytes = _fmt.BitsPerSample / 8, frame = ch * bytes;
        int n = e.BytesRecorded / frame;
        float peak = 0;
        lock (_lock)
        {
            for (int i = 0; i < n; i++)
            {
                int o = i * frame;
                float l = Read(e.Buffer, o, bytes);
                float r = ch > 1 ? Read(e.Buffer, o + bytes, bytes) : l;
                _l[_w] = l; _r[_w] = r;
                _w = (_w + 1) & Mask;
                peak = MathF.Max(peak, MathF.Max(MathF.Abs(l), MathF.Abs(r)));
            }
            if (peak > 0.0005f) _lastLoud = Environment.TickCount64;
            Analyze();
        }
    }

    private float Map(float mag)
    {
        float amp = 4f * mag / FftSize;                       // Hann coherent gain = 0.5
        float db = 20f * MathF.Log10(amp + 1e-9f);
        return Math.Clamp((db - MinDb) / DbRange * _settings.Sensitivity, 0f, 1f);
    }

    private static float Mean(float[] a, int lo, int hi)
    {
        float sum = 0; int c = 0;
        for (int i = lo; i <= hi; i++) { sum += a[i]; c++; }
        return c == 0 ? 0 : sum / c;
    }

    private void Analyze()
    {
        for (int i = 0; i < FftSize; i++)
        {
            int idx = (_w + i) & Mask;                        // oldest -> newest
            _fft[i].X = 0.5f * (_l[idx] + _r[idx]) * _window[i];
            _fft[i].Y = 0;
        }
        FastFourierTransform.FFT(true, FftM, _fft);

        for (int i = 1; i < _mapped.Length; i++)
            _mapped[i] = Map(MathF.Sqrt(_fft[i].X * _fft[i].X + _fft[i].Y * _fft[i].Y));

        for (int b = 0; b < AudioFrame.Bins; b++) _spec[b] = Mean(_mapped, _lo[b], _hi[b]);

        // Per-band gains compensate for the natural spectral tilt of music. Tune to taste.
        _bass = Math.Clamp(Mean(_mapped, 1, _bassHi) * 1.25f, 0f, 1f);
        _mid = Math.Clamp(Mean(_mapped, _bassHi + 1, _midHi) * 1.35f, 0f, 1f);
        _high = Math.Clamp(Mean(_mapped, _midHi + 1, _highHi) * 1.9f, 0f, 1f);
    }

    public void Snapshot(AudioFrame f)
    {
        lock (_lock)
        {
            f.Bass = _bass; f.Mid = _mid; f.High = _high;
            f.Active = Environment.TickCount64 - _lastLoud < 500;
            Array.Copy(_spec, f.Spectrum, AudioFrame.Bins);
            float k = Math.Min(1.5f, _settings.Sensitivity);
            for (int p = 0; p < AudioFrame.WavePoints; p++)
            {
                int idx = (_w + p * (FftSize / AudioFrame.WavePoints)) & Mask;
                f.Left[p] = Math.Clamp(_l[idx] * k * 1.5f, -1f, 1f);
                f.Right[p] = Math.Clamp(_r[idx] * k * 1.5f, -1f, 1f);
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        try { _capture?.StopRecording(); } catch { }
        _capture?.Dispose();
    }
}
