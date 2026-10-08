using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StepRecorder.Core;

/// <summary>
/// Dependency-free screen video: frames are JPEG-encoded and written into an MJPEG AVI container.
/// Plays in Windows Media Player, Movies &amp; TV, VLC, browsers via conversion, and imports into editors.
/// Timing is wall-clock based: if capturing falls behind, frames are duplicated so playback speed stays real.
/// </summary>
public sealed class VideoRecorder
{
    private const long MaxFileBytes = 1_900_000_000; // AVI 1.0 (RIFF) must stay below 2 GB
    private Thread? _thread;
    private volatile bool _stop;
    private volatile bool _paused;

    public string? FilePath { get; private set; }
    public bool IsRunning => _thread != null;
    public event Action? LimitReached;
    public event Action<Exception>? Error;

    public bool IsPaused { get => _paused; set => _paused = value; }

    public void Start(string filePath, int fps, int quality, bool allScreens, bool includeCursor)
    {
        if (_thread != null) return;
        FilePath = filePath;
        _stop = false;
        _paused = false;
        fps = Math.Clamp(fps, 1, 30);
        var bounds = allScreens ? SystemInformation.VirtualScreen : Screen.PrimaryScreen!.Bounds;
        _thread = new Thread(() => Run(filePath, fps, quality, bounds, includeCursor))
        {
            IsBackground = true,
            Name = "VideoRecorder",
            Priority = ThreadPriority.BelowNormal
        };
        _thread.Start();
    }

    public void Stop()
    {
        if (_thread == null) return;
        _stop = true;
        _thread.Join(10000);
        _thread = null;
    }

    private void Run(string path, int fps, int quality, Rectangle bounds, bool includeCursor)
    {
        // Scale large desktops down to max. 1920 px width; MJPEG needs even dimensions.
        double scale = Math.Min(1.0, 1920.0 / bounds.Width);
        int width = (int)(bounds.Width * scale) & ~1;
        int height = (int)(bounds.Height * scale) & ~1;

        try
        {
            using var writer = new MjpegAviWriter(path, width, height, fps);
            var clock = Stopwatch.StartNew();
            var pausedTotal = TimeSpan.Zero;
            var pauseStart = TimeSpan.Zero;
            bool wasPaused = false;
            long written = 0;
            byte[]? last = null;
            var frameTime = TimeSpan.FromSeconds(1.0 / fps);

            while (!_stop)
            {
                if (_paused)
                {
                    if (!wasPaused) { wasPaused = true; pauseStart = clock.Elapsed; }
                    Thread.Sleep(50);
                    continue;
                }
                if (wasPaused) { wasPaused = false; pausedTotal += clock.Elapsed - pauseStart; }

                var active = clock.Elapsed - pausedTotal;
                long due = (long)(active.TotalSeconds * fps) + 1;
                if (written < due)
                {
                    using (var shot = ScreenCapture.Capture(bounds, includeCursor))
                    {
                        if (scale < 1.0)
                        {
                            using var small = new Bitmap(shot, new Size(width, height));
                            last = ScreenCapture.ToJpeg(small, quality);
                        }
                        else
                        {
                            using var cropped = shot.Clone(new Rectangle(0, 0, width, height), shot.PixelFormat);
                            last = ScreenCapture.ToJpeg(cropped, quality);
                        }
                    }
                    // Write the new frame; repeat it if we fell behind (keeps real-time playback speed).
                    active = clock.Elapsed - pausedTotal;
                    due = Math.Max(written + 1, (long)(active.TotalSeconds * fps) + 1);
                    while (written < due)
                    {
                        writer.WriteFrame(last);
                        written++;
                    }
                    if (writer.Length > MaxFileBytes)
                    {
                        LimitReached?.Invoke();
                        break;
                    }
                }

                var next = TimeSpan.FromTicks(frameTime.Ticks * written) + pausedTotal - clock.Elapsed;
                if (next > TimeSpan.Zero) Thread.Sleep(next < TimeSpan.FromMilliseconds(200) ? next : TimeSpan.FromMilliseconds(200));
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex);
        }
    }
}
