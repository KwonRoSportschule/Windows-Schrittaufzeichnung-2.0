using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace StepRecorder.Core;

public sealed record ElementInfo(string Name, string ControlType, Rectangle? Bounds, bool IsPassword);

public sealed record WindowInfo(IntPtr Handle, string Title, string ProcessName, uint ProcessId);

/// <summary>Resolves "what was clicked" via UI Automation, always with a timeout (hung apps must not block us).</summary>
public static class UiaInspector
{
    private static readonly ConcurrentDictionary<uint, string> ProcessNames = new();
    private static readonly uint OwnPid = (uint)Environment.ProcessId;

    public static WindowInfo GetWindowAt(int x, int y)
    {
        var hwnd = Native.WindowFromPoint(new Native.POINT { X = x, Y = y });
        return Describe(Native.GetAncestor(hwnd, Native.GA_ROOT));
    }

    public static WindowInfo GetForegroundWindow() => Describe(Native.GetForegroundWindow());

    public static bool IsOwnProcess(WindowInfo w) => w.ProcessId == OwnPid;

    private static WindowInfo Describe(IntPtr root)
    {
        Native.GetWindowThreadProcessId(root, out var pid);
        var title = Native.GetWindowTitle(root);
        if (string.IsNullOrWhiteSpace(title))
        {
            var cls = Native.GetWindowClass(root);
            title = cls switch
            {
                "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" => Services.Loc.T("Win_Taskbar"),
                "Progman" or "WorkerW" => Services.Loc.T("Win_Desktop"),
                _ => ""
            };
        }
        var process = ProcessNames.GetOrAdd(pid, id =>
        {
            try { using var p = Process.GetProcessById((int)id); return p.ProcessName; }
            catch { return ""; }
        });
        if (string.IsNullOrWhiteSpace(title)) title = process;
        return new WindowInfo(root, Clean(title, 120), process, pid);
    }

    public static ElementInfo? FromPoint(int x, int y, int timeoutMs = 600) =>
        Run(() => Info(AutomationElement.FromPoint(new System.Windows.Point(x, y))), timeoutMs);

    public static ElementInfo? Focused(int timeoutMs = 600) =>
        Run(() => Info(AutomationElement.FocusedElement), timeoutMs);

    private static ElementInfo? Run(Func<ElementInfo?> func, int timeoutMs)
    {
        try
        {
            var task = Task.Run(() => { try { return func(); } catch { return null; } });
            return task.Wait(timeoutMs) ? task.Result : null;
        }
        catch { return null; }
    }

    private static ElementInfo? Info(AutomationElement? el)
    {
        if (el == null) return null;
        var c = el.Current;
        var name = c.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            try { name = c.LabeledBy?.Current.Name ?? ""; } catch { name = ""; }
        }
        if (string.IsNullOrWhiteSpace(name)) name = c.HelpText;
        var r = c.BoundingRectangle;
        Rectangle? bounds = r.IsEmpty || double.IsInfinity(r.Width)
            ? null
            : new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
        return new ElementInfo(Clean(name, 80), c.LocalizedControlType ?? "", bounds, c.IsPassword);
    }

    public static string Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Length > max ? s[..(max - 1)] + "…" : s;
    }
}
