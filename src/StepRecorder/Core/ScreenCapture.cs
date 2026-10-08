using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StepRecorder.Core;

public enum MarkerKind { None, LeftClick, RightClick, MiddleClick, Focus, Comment }

/// <summary>GDI based screen capture + annotation. Cheap enough to run per click.</summary>
public static class ScreenCapture
{
    private static readonly ImageCodecInfo JpegCodec =
        ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    public static Rectangle GetBounds(Point point, bool allScreens) =>
        allScreens ? SystemInformation.VirtualScreen : Screen.FromPoint(point).Bounds;

    public static Bitmap Capture(Rectangle bounds, bool includeCursor)
    {
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        if (includeCursor) DrawCursor(g, bounds);
        return bmp;
    }

    public static void DrawCursor(Graphics g, Rectangle bounds)
    {
        var ci = new Native.CURSORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.CURSORINFO>() };
        if (!Native.GetCursorInfo(ref ci) || (ci.flags & Native.CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero) return;
        int hx = 0, hy = 0;
        if (Native.GetIconInfo(ci.hCursor, out var info))
        {
            hx = info.xHotspot; hy = info.yHotspot;
            if (info.hbmMask != IntPtr.Zero) Native.DeleteObject(info.hbmMask);
            if (info.hbmColor != IntPtr.Zero) Native.DeleteObject(info.hbmColor);
        }
        var hdc = g.GetHdc();
        try
        {
            Native.DrawIconEx(hdc, ci.ptScreenPos.X - hx - bounds.Left, ci.ptScreenPos.Y - hy - bounds.Top,
                ci.hCursor, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL);
        }
        finally { g.ReleaseHdc(hdc); }
    }

    /// <summary>Draws a PSR-like element frame and a click marker, in bitmap coordinates.</summary>
    public static void Annotate(Bitmap bmp, Rectangle bounds, Point? click, Rectangle? element, MarkerKind kind)
    {
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var color = kind switch
        {
            MarkerKind.RightClick => Color.FromArgb(247, 99, 12),
            MarkerKind.MiddleClick => Color.FromArgb(136, 23, 152),
            MarkerKind.Focus => Color.FromArgb(0, 120, 212),
            _ => Color.FromArgb(232, 17, 35)
        };

        if (element is { } r && r.Width > 2 && r.Height > 2)
        {
            var rect = new Rectangle(r.X - bounds.X, r.Y - bounds.Y, r.Width, r.Height);
            // Skip frames that cover (nearly) the whole capture - they add nothing.
            if (rect.Width < bounds.Width * 0.95 || rect.Height < bounds.Height * 0.95)
            {
                rect.Inflate(2, 2);
                using var pen = new Pen(kind == MarkerKind.Focus ? color : Color.FromArgb(22, 198, 12), 3);
                g.DrawRectangle(pen, rect);
            }
        }

        if (click is { } p && kind is MarkerKind.LeftClick or MarkerKind.RightClick or MarkerKind.MiddleClick)
        {
            float x = p.X - bounds.X, y = p.Y - bounds.Y;
            const float r1 = 18, r2 = 26;
            using var fill = new SolidBrush(Color.FromArgb(70, color));
            using var ring = new Pen(Color.FromArgb(220, color), 3);
            using var outer = new Pen(Color.FromArgb(90, color), 2);
            g.FillEllipse(fill, x - r1, y - r1, r1 * 2, r1 * 2);
            g.DrawEllipse(ring, x - r1, y - r1, r1 * 2, r1 * 2);
            g.DrawEllipse(outer, x - r2, y - r2, r2 * 2, r2 * 2);
        }
    }

    public static void SavePng(Bitmap bmp, string path) => bmp.Save(path, ImageFormat.Png);

    public static void SaveJpeg(Image img, Stream stream, int quality)
    {
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Clamp(quality, 10, 100));
        img.Save(stream, JpegCodec, ep);
    }

    public static byte[] ToJpeg(Image img, int quality)
    {
        using var ms = new MemoryStream();
        SaveJpeg(img, ms, quality);
        return ms.ToArray();
    }

    public static Bitmap Resize(Image src, int maxWidth)
    {
        double scale = Math.Min(1.0, (double)maxWidth / src.Width);
        int w = Math.Max(1, (int)Math.Round(src.Width * scale));
        int h = Math.Max(1, (int)Math.Round(src.Height * scale));
        var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, 0, 0, w, h);
        return dst;
    }

    public static void SaveThumbnail(Image src, string path)
    {
        using var thumb = Resize(src, 360);
        using var fs = File.Create(path);
        SaveJpeg(thumb, fs, 82);
    }

    /// <summary>Loads an image file without locking it, converted to JPEG bytes (for PDF/HTML).</summary>
    public static (byte[] Jpeg, int Width, int Height) LoadAsJpeg(string path, int quality, int maxWidth = 2560)
    {
        using var fs = File.OpenRead(path);
        using var img = Image.FromStream(fs);
        if (img.Width > maxWidth)
        {
            using var small = Resize(img, maxWidth);
            return (ToJpeg(small, quality), small.Width, small.Height);
        }
        // Re-draw into 24bpp so JPEG encoding never sees alpha/palette formats.
        using var copy = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(copy)) g.DrawImage(img, 0, 0, img.Width, img.Height);
        return (ToJpeg(copy, quality), copy.Width, copy.Height);
    }
}
