using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using MediaPlayerWpf = System.Windows.Media.MediaPlayer;
using MediaPlayerVlc = LibVLCSharp.Shared.MediaPlayer;

namespace DiceChaosDisplay.Services;

/// <summary>
/// Airspace-free looping video battlemap controller supporting MP4 and WEBM across multiple windows.
/// Uses zero-copy GPU MediaPlayer/VideoDrawing when supported by Windows Media Foundation,
/// and automatically falls back to LibVLC offscreen Bgr32 frame streaming for VP8/VP9/AV1 WebM files.
/// </summary>
public sealed class VideoMapInstance : IDisposable
{
    private static readonly Lazy<LibVLC> SharedLibVlc = new(() =>
    {
        Core.Initialize();
        return new LibVLC("--no-osd", "--no-video-title-show", "--quiet");
    });

    private readonly Dispatcher _dispatcher;
    private readonly string _filePath;

    // Native WPF MediaPlayer path
    private MediaPlayerWpf? _wpfPlayer;
    private DrawingBrush? _wpfVideoBrush;

    // LibVLC fallback path
    private MediaPlayerVlc? _vlcPlayer;
    private Media? _vlcMedia;
    private WriteableBitmap? _vlcBitmap;
    private IntPtr _stagingBuffer;
    private int _stagingStride;
    private uint _videoWidth;
    private uint _videoHeight;
    private int _frameUpdateQueued;

    // Keep delegates rooted so GC never collects unmanaged callbacks
    private MediaPlayerVlc.LibVLCVideoFormatCb? _formatCb;
    private MediaPlayerVlc.LibVLCVideoCleanupCb? _cleanupCb;
    private MediaPlayerVlc.LibVLCVideoLockCb? _lockCb;
    private MediaPlayerVlc.LibVLCVideoDisplayCb? _displayCb;

    private bool _isMuted = true;
    private double _volume = 0.5;
    private bool _isPlaying = true;
    private bool _disposed;

    public int PixelWidth { get; private set; } = 1920;
    public int PixelHeight { get; private set; } = 1080;

    /// <summary>
    /// If using native WPF GPU video path, this Brush paints both windows zero-copy.
    /// </summary>
    public Brush? VideoBrush => _wpfVideoBrush;

    /// <summary>
    /// If using LibVLC offscreen path, this WriteableBitmap updates live for both windows.
    /// </summary>
    public BitmapSource? VideoBitmap => _vlcBitmap;

    public BitmapSource? Thumbnail { get; private set; }

    public event EventHandler? MediaReady;

    public VideoMapInstance(string filePath, Dispatcher? dispatcher = null)
    {
        _filePath = filePath;
        _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public Task StartAsync(bool muted = true, double volume = 0.5)
    {
        _isMuted = muted;
        _volume = Math.Clamp(volume, 0.0, 1.0);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string ext = Path.GetExtension(_filePath).ToLowerInvariant();

        // For .webm, go straight to LibVLC so VP8/VP9/AV1 always works reliably with seamless looping
        if (ext == ".webm" || ext == ".mkv")
        {
            StartLibVlc(tcs);
        }
        else
        {
            StartWpfMediaPlayerWithFallback(tcs);
        }

        return tcs.Task;
    }

    public void SetAudio(bool isMuted, double volume)
    {
        _isMuted = isMuted;
        _volume = Math.Clamp(volume, 0.0, 1.0);

        _dispatcher.InvokeAsync(() =>
        {
            if (_wpfPlayer is not null)
            {
                _wpfPlayer.IsMuted = _isMuted;
                _wpfPlayer.Volume = _isMuted ? 0.0 : _volume;
            }
        });

        if (_vlcPlayer is not null)
        {
            _vlcPlayer.Mute = _isMuted;
            _vlcPlayer.Volume = _isMuted ? 0 : (int)Math.Round(_volume * 100.0);
        }
    }

    public void TogglePlayPause()
    {
        _isPlaying = !_isPlaying;
        _dispatcher.InvokeAsync(() =>
        {
            if (_wpfPlayer is not null)
            {
                if (_isPlaying) _wpfPlayer.Play();
                else _wpfPlayer.Pause();
            }
        });

        if (_vlcPlayer is not null)
        {
            _vlcPlayer.SetPause(!_isPlaying);
        }
    }

    public bool IsPlaying => _isPlaying;

    private void StartWpfMediaPlayerWithFallback(TaskCompletionSource tcs)
    {
        _dispatcher.InvokeAsync(() =>
        {
            try
            {
                _wpfPlayer = new MediaPlayerWpf
                {
                    ScrubbingEnabled = true,
                    IsMuted = _isMuted,
                    Volume = _isMuted ? 0.0 : _volume
                };

                _wpfPlayer.MediaOpened += (_, _) =>
                {
                    if (_wpfPlayer is null) return;
                    PixelWidth = Math.Max(64, _wpfPlayer.NaturalVideoWidth);
                    PixelHeight = Math.Max(64, _wpfPlayer.NaturalVideoHeight);

                    var drawing = new VideoDrawing
                    {
                        Player = _wpfPlayer,
                        Rect = new Rect(0, 0, PixelWidth, PixelHeight)
                    };
                    _wpfVideoBrush = new DrawingBrush(drawing) { Stretch = Stretch.Fill };

                    // Capture a quick thumbnail after playback starts
                    CaptureWpfThumbnailDelayed();

                    MediaReady?.Invoke(this, EventArgs.Empty);
                    tcs.TrySetResult();
                };

                _wpfPlayer.MediaFailed += (_, _) =>
                {
                    CleanupWpfPlayer();
                    StartLibVlc(tcs);
                };

                _wpfPlayer.MediaEnded += (_, _) =>
                {
                    if (_wpfPlayer is null) return;
                    _wpfPlayer.Position = TimeSpan.Zero;
                    if (_isPlaying) _wpfPlayer.Play();
                };

                _wpfPlayer.Open(new Uri(_filePath, UriKind.Absolute));
                _wpfPlayer.Play();
            }
            catch
            {
                CleanupWpfPlayer();
                StartLibVlc(tcs);
            }
        });
    }

    private async void CaptureWpfThumbnailDelayed()
    {
        try
        {
            await Task.Delay(250);
            if (_disposed || _wpfVideoBrush is null) return;

            double scale = Math.Min(256.0 / PixelWidth, 256.0 / PixelHeight);
            int tw = Math.Max(1, (int)(PixelWidth * scale));
            int th = Math.Max(1, (int)(PixelHeight * scale));

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(_wpfVideoBrush, null, new Rect(0, 0, tw, th));
            }
            var rtb = new RenderTargetBitmap(tw, th, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            Thumbnail = rtb;
            MediaReady?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Non-critical thumbnail capture
        }
    }

    private unsafe void StartLibVlc(TaskCompletionSource tcs)
    {
        Task.Run(() =>
        {
            try
            {
                var libVlc = SharedLibVlc.Value;
                _vlcPlayer = new MediaPlayerVlc(libVlc);
                _vlcMedia = new Media(libVlc, _filePath, FromType.FromPath, ":input-repeat=65535");

                bool readySignaled = false;

                _formatCb = (ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines) =>
                {
                    ReadOnlySpan<byte> rv32 = "RV32"u8;
                    for (int i = 0; i < 4; i++)
                        Marshal.WriteByte(chroma, i, rv32[i]);

                    _videoWidth = Math.Max(16u, width);
                    _videoHeight = Math.Max(16u, height);
                    pitches = _videoWidth * 4;
                    lines = _videoHeight;
                    _stagingStride = (int)pitches;

                    FreeStagingBuffer();
                    _stagingBuffer = (IntPtr)NativeMemory.AlignedAlloc((nuint)(pitches * lines), 32);

                    _dispatcher.Invoke(() =>
                    {
                        PixelWidth = (int)_videoWidth;
                        PixelHeight = (int)_videoHeight;
                        _vlcBitmap = new WriteableBitmap(PixelWidth, PixelHeight, 96, 96, PixelFormats.Bgr32, null);
                    });

                    if (!readySignaled)
                    {
                        readySignaled = true;
                        _dispatcher.InvokeAsync(() => MediaReady?.Invoke(this, EventArgs.Empty));
                        tcs.TrySetResult();
                    }

                    return 1;
                };

                _cleanupCb = (ref IntPtr opaque) => { };

                _lockCb = (IntPtr opaque, IntPtr planes) =>
                {
                    Marshal.WriteIntPtr(planes, _stagingBuffer);
                    return IntPtr.Zero;
                };

                _displayCb = (IntPtr opaque, IntPtr picture) =>
                {
                    if (_disposed || _stagingBuffer == IntPtr.Zero) return;
                    if (Interlocked.Exchange(ref _frameUpdateQueued, 1) == 0)
                    {
                        _dispatcher.BeginInvoke(DispatcherPriority.Render, CopyVlcFrameToBitmap);
                    }
                };

                _vlcPlayer.SetVideoFormatCallbacks(_formatCb, _cleanupCb);
                _vlcPlayer.SetVideoCallbacks(_lockCb, null, _displayCb);
                _vlcPlayer.Mute = _isMuted;
                _vlcPlayer.Volume = _isMuted ? 0 : (int)Math.Round(_volume * 100.0);

                // Fallback loop handler in case input-repeat resets
                _vlcPlayer.EndReached += (_, _) =>
                {
                    if (_disposed || _vlcPlayer is null) return;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        if (_disposed || _vlcPlayer is null || _vlcMedia is null) return;
                        _vlcPlayer.Stop();
                        _vlcPlayer.Play(_vlcMedia);
                    });
                };

                if (!_vlcPlayer.Play(_vlcMedia))
                {
                    tcs.TrySetException(new InvalidOperationException($"Failed to play video map: {_filePath}"));
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
    }

    private unsafe void CopyVlcFrameToBitmap()
    {
        Interlocked.Exchange(ref _frameUpdateQueued, 0);
        if (_disposed || _vlcBitmap is null || _stagingBuffer == IntPtr.Zero) return;

        int w = _vlcBitmap.PixelWidth;
        int h = _vlcBitmap.PixelHeight;
        if (w != (int)_videoWidth || h != (int)_videoHeight) return;

        _vlcBitmap.Lock();
        try
        {
            byte* src = (byte*)_stagingBuffer.ToPointer();
            byte* dst = (byte*)_vlcBitmap.BackBuffer.ToPointer();
            int dstStride = _vlcBitmap.BackBufferStride;
            int rowBytes = Math.Min(_stagingStride, dstStride);

            for (int y = 0; y < h; y++)
            {
                Buffer.MemoryCopy(src + (long)y * _stagingStride, dst + (long)y * dstStride, dstStride, rowBytes);
            }
            _vlcBitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
        }
        finally
        {
            _vlcBitmap.Unlock();
        }

        if (Thumbnail is null)
        {
            try
            {
                double scale = Math.Min(256.0 / w, 256.0 / h);
                var transformed = new TransformedBitmap(_vlcBitmap, new ScaleTransform(scale, scale));
                var copy = new WriteableBitmap(transformed);
                copy.Freeze();
                Thumbnail = copy;
                MediaReady?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                // Ignore thumbnail error
            }
        }
    }

    private void CleanupWpfPlayer()
    {
        if (_wpfPlayer is not null)
        {
            try
            {
                _wpfPlayer.Stop();
                _wpfPlayer.Close();
            }
            catch { }
            _wpfPlayer = null;
            _wpfVideoBrush = null;
        }
    }

    private unsafe void FreeStagingBuffer()
    {
        IntPtr ptr = Interlocked.Exchange(ref _stagingBuffer, IntPtr.Zero);
        if (ptr != IntPtr.Zero)
        {
            NativeMemory.AlignedFree(ptr.ToPointer());
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _dispatcher.Invoke(() => CleanupWpfPlayer());

        if (_vlcPlayer is not null)
        {
            try { _vlcPlayer.Stop(); } catch { }
            _vlcPlayer.Dispose();
            _vlcPlayer = null;
        }
        _vlcMedia?.Dispose();
        _vlcMedia = null;
        FreeStagingBuffer();
    }
}
