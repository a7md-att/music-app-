using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using Windows.Media.Control;

namespace AuraWall.Media;

public sealed record MediaSnapshot(string Title, string Artist, string Album, SKImage? Art);

/// <summary>Polls the Windows SMTC (System Media Transport Controls) for the current track.</summary>
public sealed class MediaWatcher : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private volatile MediaSnapshot? _current;
    public MediaSnapshot? Current => _current;

    public void Start() => Task.Run(Loop);

    private async Task Loop()
    {
        GlobalSystemMediaTransportControlsSessionManager? mgr = null;
        string lastKey = "";
        SKImage? art = null;

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                mgr ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var ses = mgr.GetCurrentSession();
                if (ses == null ||
                    ses.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    _current = null;
                }
                else
                {
                    var p = await ses.TryGetMediaPropertiesAsync();
                    string key = $"{p.Title}|{p.Artist}|{p.AlbumTitle}";
                    if (key != lastKey)
                    {
                        lastKey = key;
                        art = null;
                        if (p.Thumbnail != null)
                        {
                            try
                            {
                                using var rs = await p.Thumbnail.OpenReadAsync();
                                using var st = rs.AsStreamForRead();
                                using var ms = new MemoryStream();
                                await st.CopyToAsync(ms);
                                art = SKImage.FromEncodedData(ms.ToArray());
                            }
                            catch { art = null; }
                        }
                    }
                    _current = new MediaSnapshot(p.Title ?? "", p.Artist ?? "", p.AlbumTitle ?? "", art);
                }
            }
            catch { mgr = null; _current = null; }

            try { await Task.Delay(2000, _cts.Token); } catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose() => _cts.Cancel();
}
