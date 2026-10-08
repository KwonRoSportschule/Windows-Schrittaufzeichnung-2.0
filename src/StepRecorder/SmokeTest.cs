using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StepRecorder.Core;
using StepRecorder.Export;
using StepRecorder.Models;
using StepRecorder.Services;
using StepRecorder.Views;

namespace StepRecorder;

/// <summary>
/// "StepRecorder2.exe --smoke-test &lt;dir&gt;": end-to-end self test used by CI on a real Windows desktop.
/// Captures steps through the real engine, renders every window to PNG (light/dark, DE/EN),
/// runs all exporters plus a save/load round trip, then exits with 0 (ok) or 1 (failure).
/// </summary>
internal static class SmokeTest
{
    public static bool Enabled { get; private set; }
    private static string _dir = "";
    private static StreamWriter? _log;

    public static bool TryEnable(string[] args)
    {
        int i = Array.IndexOf(args, "--smoke-test");
        if (i < 0) return false;
        Enabled = true;
        _dir = Path.GetFullPath(i + 1 < args.Length ? args[i + 1] : "smoke");
        Directory.CreateDirectory(_dir);
        _log = new StreamWriter(Path.Combine(_dir, "smoke.log")) { AutoFlush = true };
        Log("smoke test started");
        return true;
    }

    public static void Log(string message) => _log?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

    public static void Fail(Exception ex)
    {
        Log("FAILED: " + ex);
        _log?.Flush();
        Environment.Exit(1);
    }

    public static async void Run(MainWindow main)
    {
        try
        {
            await Task.Delay(1500);
            Render(main, "01-main-empty-light.png");

            // --- capture through the real engine (no global hooks needed: events are injected)
            var session = ProjectStore.CreateSession();
            var captured = new List<Step>();
            var engine = new RecorderEngine();
            engine.StepCaptured += s => { lock (captured) captured.Add(s); };
            engine.Error += ex => Log("engine error: " + ex);
            main.WindowState = WindowState.Minimized;
            await Task.Delay(500);

            engine.Start(session.Directory, new RecorderOptions(true, true, false, true, true, Array.Empty<Hotkey>()));
            engine.OnMouse(new MouseInput(300, 300, MouseButton.Left, DateTime.Now));
            engine.OnMouse(new MouseInput(320, 340, MouseButton.Right, DateTime.Now.AddSeconds(1)));
            engine.AddComment("Smoke test: Kommentar mit Umlauten äöü ß € „Anführung“");
            await Task.Delay(4000);
            await Task.Run(engine.Stop);
            Log($"captured {captured.Count} steps");
            if (captured.Count == 0) throw new Exception("no steps captured");
            foreach (var s in captured)
            {
                Log($"  {s.Kind}: {s.Description} [{s.WindowTitle}] {new FileInfo(s.ImagePath).Length} bytes");
                session.Steps.Add(s);
            }
            session.Steps[0].Note = "Eine Notiz\nmit zwei Zeilen.";
            session.Title = "Smoke-Test Aufzeichnung";

            main.WindowState = WindowState.Normal;
            main.ShowSession(session);
            await Task.Delay(1200);
            Render(main, "02-main-steps-light.png");

            ThemeManager.Apply(ThemeChoice.Dark);
            await Task.Delay(600);
            Render(main, "03-main-steps-dark.png");

            var settings = new SettingsWindow(App.Settings) { Owner = main };
            settings.Show();
            await Task.Delay(800);
            Render(settings, "04-settings-dark.png");
            settings.Close();

            var toolbar = new RecordingToolbar();
            toolbar.Show();
            toolbar.Update(TimeSpan.FromSeconds(83), 7, false, true);
            await Task.Delay(500);
            Render(toolbar, "05-toolbar-dark.png");
            toolbar.Close();

            Loc.Instance.Language = "en";
            ThemeManager.Apply(ThemeChoice.Light);
            await Task.Delay(600);
            Render(main, "06-main-steps-en-light.png");

            // --- exports
            var data = ExportData.From(session, "Untitled", 85);
            await Task.Run(() =>
            {
                PdfExporter.Export(data, Path.Combine(_dir, "report.pdf"));
                HtmlExporter.Export(data, Path.Combine(_dir, "report.html"));
                ZipExporter.Export(data, Path.Combine(_dir, "report.zip"));
            });
            foreach (var f in new[] { "report.pdf", "report.html", "report.zip" })
            {
                var len = new FileInfo(Path.Combine(_dir, f)).Length;
                Log($"{f}: {len} bytes");
                if (len < 500) throw new Exception($"{f} is suspiciously small");
            }

            // --- video (2 seconds)
            var video = new VideoRecorder();
            var videoPath = Path.Combine(_dir, "video.avi");
            video.Error += ex => Log("video error: " + ex);
            video.Start(videoPath, 5, 70, false, true);
            await Task.Delay(2200);
            await Task.Run(video.Stop);
            Log($"video.avi: {new FileInfo(videoPath).Length} bytes");

            // --- project round trip
            var project = Path.Combine(_dir, "roundtrip.steps");
            ProjectStore.Save(session, project);
            var loaded = ProjectStore.Load(project);
            Log($"round trip: {loaded.Steps.Count} steps, title '{loaded.Title}'");
            if (loaded.Steps.Count != session.Steps.Count || loaded.Title != session.Title)
                throw new Exception("project round trip mismatch");
            if (!File.Exists(loaded.Steps[0].ImagePath)) throw new Exception("round trip lost images");
            ProjectStore.DeleteSession(loaded);

            Log("OK");
            main.Close();
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private static void Render(Window window, string name)
    {
        window.UpdateLayout();
        if (window.Content is not FrameworkElement content) return;
        var size = new Size(content.ActualWidth, content.ActualHeight);
        if (size.Width < 1 || size.Height < 1) { Log($"skip {name}: empty"); return; }
        var dpi = VisualTreeHelper.GetDpi(window);
        var rtb = new RenderTargetBitmap((int)(size.Width * dpi.DpiScaleX), (int)(size.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (window.Background is SolidColorBrush { Color.A: > 0 } bg) dc.DrawRectangle(bg, null, new Rect(size));
            dc.DrawRectangle(new VisualBrush(content), null, new Rect(size));
        }
        rtb.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(_dir, name));
        encoder.Save(fs);
        Log($"rendered {name}");
    }
}
