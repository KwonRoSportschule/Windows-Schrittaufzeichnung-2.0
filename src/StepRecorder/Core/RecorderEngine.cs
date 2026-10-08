using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using StepRecorder.Models;
using StepRecorder.Services;

namespace StepRecorder.Core;

public sealed record RecorderOptions(
    bool CaptureKeyboard, bool RecordTypedText, bool CaptureAllScreens,
    bool Highlight, bool IncludeCursor, IReadOnlyCollection<Hotkey> IgnoredHotkeys);

/// <summary>
/// Turns raw input events into steps. Runs on its own worker thread so hook callbacks stay instant:
/// capture → UI Automation lookup → annotate → save PNG + thumbnail → raise <see cref="StepCaptured"/>.
/// Consecutive typing is merged into a single "typed ..." step.
/// </summary>
public sealed class RecorderEngine
{
    private const int TypingIdleFlushMs = 1500;
    private const int TypingMaxTokens = 300;

    private sealed record CommentRequest(string Text);
    private sealed record FlushRequest;
    private sealed record LastClick(Guid StepId, DateTime Time, int X, int Y, string Target);

    private BlockingCollection<object>? _queue;
    private Thread? _worker;
    private RecorderOptions _options = new(true, false, false, true, true, Array.Empty<Hotkey>());
    private string _imageDir = "";
    private volatile bool _paused;
    private LastClick? _lastClick;
    private readonly TypingBuffer _typing = new();

    public event Action<Step>? StepCaptured;
    /// <summary>A step changed after capture (single click became a double click).</summary>
    public event Action<Guid, StepKind, string>? StepUpdated;
    public event Action<Exception>? Error;

    public bool IsRunning => _worker != null;

    public bool IsPaused
    {
        get => _paused;
        set
        {
            _paused = value;
            if (value) _queue?.TryAdd(new FlushRequest());
        }
    }

    public void Start(string sessionDirectory, RecorderOptions options)
    {
        if (_worker != null) return;
        _options = options;
        _imageDir = Path.Combine(sessionDirectory, "img");
        Directory.CreateDirectory(_imageDir);
        _paused = false;
        _lastClick = null;
        _queue = new BlockingCollection<object>(new ConcurrentQueue<object>(), 256);
        _worker = new Thread(Run) { IsBackground = true, Name = "Recorder", Priority = ThreadPriority.AboveNormal };
        _worker.Start();
    }

    public void Stop()
    {
        if (_worker == null) return;
        _queue?.CompleteAdding();
        _worker.Join(5000);
        _worker = null;
        _queue?.Dispose();
        _queue = null;
    }

    public void OnMouse(MouseInput input)
    {
        if (!_paused) _queue?.TryAdd(input);
    }

    public void OnKey(KeyInput input)
    {
        if (!_paused && _options.CaptureKeyboard) _queue?.TryAdd(input);
    }

    public void AddComment(string text) => _queue?.TryAdd(new CommentRequest(text));

    private void Run()
    {
        var queue = _queue!;
        while (true)
        {
            int timeout = _typing.IsEmpty
                ? Timeout.Infinite
                : Math.Max(0, (int)(_typing.LastInput.AddMilliseconds(TypingIdleFlushMs) - DateTime.Now).TotalMilliseconds);

            object? item;
            try
            {
                if (!queue.TryTake(out item, timeout))
                {
                    Safe(FlushTyping);
                    if (queue.IsCompleted) break;
                    continue;
                }
            }
            catch (ObjectDisposedException) { break; }

            switch (item)
            {
                case MouseInput m: Safe(() => HandleMouse(m)); break;
                case KeyInput k: Safe(() => HandleKey(k)); break;
                case CommentRequest c: Safe(() => { FlushTyping(); CaptureComment(c.Text); }); break;
                case FlushRequest: Safe(FlushTyping); break;
            }
        }
        Safe(FlushTyping);
    }

    private void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { Error?.Invoke(ex); }
    }

    // ---------------------------------------------------------------- mouse

    private void HandleMouse(MouseInput m)
    {
        FlushTyping();
        var window = UiaInspector.GetWindowAt(m.X, m.Y);
        if (UiaInspector.IsOwnProcess(window)) return;

        // Low-level hooks never report double clicks; detect them ourselves and upgrade the previous step.
        if (m.Button == MouseButton.Left && _lastClick is { } last &&
            (m.Time - last.Time).TotalMilliseconds <= Native.GetDoubleClickTime() &&
            Math.Abs(m.X - last.X) <= Native.GetSystemMetrics(36 /*SM_CXDOUBLECLK*/) &&
            Math.Abs(m.Y - last.Y) <= Native.GetSystemMetrics(37 /*SM_CYDOUBLECLK*/))
        {
            _lastClick = null;
            StepUpdated?.Invoke(last.StepId, StepKind.DoubleClick, Loc.F("Desc_DoubleClick", last.Target));
            return;
        }

        // Start the (potentially slow) UI Automation lookup in parallel with the screen capture.
        var elementTask = Task.Run(() => UiaInspector.FromPoint(m.X, m.Y));
        var point = new Point(m.X, m.Y);
        var bounds = ScreenCapture.GetBounds(point, _options.CaptureAllScreens);
        using var bmp = ScreenCapture.Capture(bounds, _options.IncludeCursor);
        var element = elementTask.Wait(700) ? elementTask.Result : null;

        var (kind, marker, key) = m.Button switch
        {
            MouseButton.Right => (StepKind.RightClick, MarkerKind.RightClick, "Desc_RightClick"),
            MouseButton.Middle => (StepKind.MiddleClick, MarkerKind.MiddleClick, "Desc_MiddleClick"),
            _ => (StepKind.Click, MarkerKind.LeftClick, "Desc_Click")
        };
        if (_options.Highlight) ScreenCapture.Annotate(bmp, bounds, point, element?.Bounds, marker);

        var target = Target(element, window);
        var step = Save(bmp, kind, Loc.F(key, target), window, m.Time);
        _lastClick = m.Button == MouseButton.Left ? new LastClick(step.Id, m.Time, m.X, m.Y, target) : null;
        StepCaptured?.Invoke(step);
    }

    // ---------------------------------------------------------------- keyboard

    private void HandleKey(KeyInput k)
    {
        if (_options.IgnoredHotkeys.Any(h => h.VirtualKey == k.VirtualKey &&
                h.Ctrl == k.Ctrl && h.Shift == k.Shift && h.Alt == k.Alt && h.Win == k.Win))
            return;

        var window = UiaInspector.GetForegroundWindow();
        if (UiaInspector.IsOwnProcess(window)) return;
        _lastClick = null;

        var special = SpecialKeyName(k.VirtualKey);
        bool modified = k.Ctrl || k.Alt || k.Win;
        // A printable key without translated text is a dead key (^, ´, `) - the next key carries the character.
        if (k.Text == null && !modified && special == null && IsPrintableKey(k.VirtualKey)) return;
        bool isShortcut = k.Text == null && (modified || special == null);
        if (isShortcut)
        {
            FlushTyping();
            CaptureShortcut(k, window);
            return;
        }

        if (!_typing.IsEmpty && _typing.Window!.Handle != window.Handle) FlushTyping();
        if (_typing.IsEmpty)
        {
            var focused = UiaInspector.Focused();
            _typing.Begin(window, focused);
        }

        if (k.Text != null) _typing.AddText(k.Text);
        else if (k.VirtualKey == 0x08 /*Back*/) _typing.Backspace(special!);
        else _typing.AddKey(special!);
        _typing.LastInput = DateTime.Now;

        // Enter/Tab/Esc usually "commit" an input - make that a natural step boundary.
        if (k.VirtualKey is 0x0D or 0x09 or 0x1B || _typing.Count >= TypingMaxTokens) FlushTyping();
    }

    private void FlushTyping()
    {
        if (_typing.IsEmpty) return;
        if (_typing.Count == 0) { _typing.Clear(); return; }
        var window = _typing.Window!;
        var focused = _typing.Focused;
        bool mask = !_options.RecordTypedText || focused?.IsPassword == true;
        var text = _typing.Render(mask);
        var time = _typing.Started;
        _typing.Clear();

        var bounds = _options.CaptureAllScreens
            ? SystemInformation.VirtualScreen
            : Screen.FromHandle(window.Handle).Bounds;
        using var bmp = ScreenCapture.Capture(bounds, _options.IncludeCursor);
        if (_options.Highlight && focused?.Bounds is { } fb)
            ScreenCapture.Annotate(bmp, bounds, null, fb, MarkerKind.Focus);

        var step = Save(bmp, StepKind.Keyboard, Loc.F("Desc_Typed", text, Target(focused, window)), window, time);
        StepCaptured?.Invoke(step);
    }

    private void CaptureShortcut(KeyInput k, WindowInfo window)
    {
        var parts = new List<string>();
        if (k.Ctrl) parts.Add(Loc.T("Key_Ctrl"));
        if (k.Shift) parts.Add(Loc.T("Key_Shift"));
        if (k.Alt) parts.Add(Loc.T("Key_Alt"));
        if (k.Win) parts.Add(Loc.T("Key_Win"));
        parts.Add(SpecialKeyName(k.VirtualKey)?.Trim('[', ']') ?? KeyName(k));
        var combo = string.Join("+", parts);

        var bounds = _options.CaptureAllScreens
            ? SystemInformation.VirtualScreen
            : Screen.FromHandle(window.Handle).Bounds;
        using var bmp = ScreenCapture.Capture(bounds, _options.IncludeCursor);
        var step = Save(bmp, StepKind.Shortcut, Loc.F("Desc_Shortcut", combo, Loc.F("Fmt_TargetWindow", window.Title)), window, k.Time);
        StepCaptured?.Invoke(step);
    }

    private void CaptureComment(string text)
    {
        var bounds = ScreenCapture.GetBounds(Cursor.Position, _options.CaptureAllScreens);
        using var bmp = ScreenCapture.Capture(bounds, _options.IncludeCursor);
        var window = UiaInspector.GetForegroundWindow();
        var step = Save(bmp, StepKind.Comment, string.IsNullOrWhiteSpace(text) ? Loc.T("Desc_Comment") : text.Trim(),
            window, DateTime.Now);
        StepCaptured?.Invoke(step);
    }

    // ---------------------------------------------------------------- helpers

    private Step Save(Bitmap bmp, StepKind kind, string description, WindowInfo window, DateTime time)
    {
        var step = new Step
        {
            Kind = kind,
            Description = description,
            Timestamp = time,
            WindowTitle = window.Title,
            ProcessName = window.ProcessName,
        };
        var id = step.Id.ToString("N");
        step.ImageFile = $"img/{id}.png";
        step.ThumbFile = $"img/{id}_t.jpg";
        step.ImagePath = Path.Combine(_imageDir, $"{id}.png");
        step.ThumbPath = Path.Combine(_imageDir, $"{id}_t.jpg");
        ScreenCapture.SavePng(bmp, step.ImagePath);
        ScreenCapture.SaveThumbnail(bmp, step.ThumbPath);
        return step;
    }

    private static string Target(ElementInfo? element, WindowInfo window)
    {
        if (element is { Name.Length: > 0 } el && el.Name != window.Title)
        {
            return string.IsNullOrWhiteSpace(el.ControlType)
                ? Loc.F("Fmt_TargetElementNoType", el.Name, window.Title)
                : Loc.F("Fmt_TargetElement", el.Name, el.ControlType, window.Title);
        }
        return Loc.F("Fmt_TargetWindow", window.Title);
    }

    /// <summary>Keys that belong into a typing sequence (returned as "[Name]"), or null.</summary>
    private static string? SpecialKeyName(int vk) => vk switch
    {
        0x0D => $"[{Loc.T("Key_Enter")}]",
        0x09 => $"[{Loc.T("Key_Tab")}]",
        0x1B => $"[{Loc.T("Key_Esc")}]",
        0x08 => $"[{Loc.T("Key_Back")}]",
        0x2E => $"[{Loc.T("Key_Delete")}]",
        0x2D => $"[{Loc.T("Key_Insert")}]",
        0x24 => $"[{Loc.T("Key_Home")}]",
        0x23 => $"[{Loc.T("Key_End")}]",
        0x21 => $"[{Loc.T("Key_PageUp")}]",
        0x22 => $"[{Loc.T("Key_PageDown")}]",
        0x25 => "[←]",
        0x26 => "[↑]",
        0x27 => "[→]",
        0x28 => "[↓]",
        0x20 => $"[{Loc.T("Key_Space")}]",
        _ => null
    };

    private static bool IsPrintableKey(int vk) =>
        vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x60 and <= 0x6F or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xE2;

    private static string KeyName(KeyInput k)
    {
        int vk = k.VirtualKey;
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        var sb = new StringBuilder(64);
        int lParam = (k.ScanCode << 16) | (k.Extended ? 1 << 24 : 0);
        return Native.GetKeyNameText(lParam, sb, sb.Capacity) > 0 ? sb.ToString() : $"#{vk}";
    }

    /// <summary>Collects one continuous typing sequence.</summary>
    private sealed class TypingBuffer
    {
        private readonly List<(string Value, bool IsChar)> _tokens = new();

        public WindowInfo? Window { get; private set; }
        public ElementInfo? Focused { get; private set; }
        public DateTime Started { get; private set; }
        public DateTime LastInput { get; set; }
        public bool IsEmpty => Window == null;
        public int Count => _tokens.Count;

        public void Begin(WindowInfo window, ElementInfo? focused)
        {
            Window = window;
            Focused = focused;
            Started = LastInput = DateTime.Now;
        }

        public void AddText(string text)
        {
            foreach (var c in text) _tokens.Add((c.ToString(), true));
        }

        public void AddKey(string name) => _tokens.Add((name, false));

        public void Backspace(string name)
        {
            if (_tokens.Count > 0 && _tokens[^1].IsChar) _tokens.RemoveAt(_tokens.Count - 1);
            else _tokens.Add((name, false));
        }

        public string Render(bool mask)
        {
            var sb = new StringBuilder();
            foreach (var (value, isChar) in _tokens) sb.Append(isChar && mask ? "•" : value);
            return sb.ToString();
        }

        public void Clear()
        {
            _tokens.Clear();
            Window = null;
            Focused = null;
        }
    }
}
