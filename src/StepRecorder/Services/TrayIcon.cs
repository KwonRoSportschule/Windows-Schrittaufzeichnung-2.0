using System;
using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace StepRecorder.Services;

/// <summary>Notification-area icon so the app can keep recording while its window is hidden.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly Icon _idleIcon;
    private readonly Icon _recordingIcon;
    private readonly WinForms.ToolStripMenuItem _record, _pause, _show, _exit;

    public event Action? RecordClicked, PauseClicked, ShowClicked, ExitClicked;

    public TrayIcon()
    {
        _idleIcon = LoadAppIcon();
        _recordingIcon = CreateRecordingIcon();

        _record = new WinForms.ToolStripMenuItem("", null, (_, _) => RecordClicked?.Invoke());
        _pause = new WinForms.ToolStripMenuItem("", null, (_, _) => PauseClicked?.Invoke()) { Enabled = false };
        _show = new WinForms.ToolStripMenuItem("", null, (_, _) => ShowClicked?.Invoke());
        _exit = new WinForms.ToolStripMenuItem("", null, (_, _) => ExitClicked?.Invoke());

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.AddRange(new WinForms.ToolStripItem[] { _record, _pause, new WinForms.ToolStripSeparator(), _show, _exit });

        _icon = new WinForms.NotifyIcon { Icon = _idleIcon, ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) ShowClicked?.Invoke(); };
        Update(false, false, 0);
    }

    public void Update(bool recording, bool paused, int steps)
    {
        _record.Text = Loc.T(recording ? "Btn_Stop" : "Btn_Record");
        _pause.Text = Loc.T(paused ? "Btn_Resume" : "Btn_Pause");
        _pause.Enabled = recording;
        _show.Text = Loc.T("Tray_Show");
        _exit.Text = Loc.T("Tray_Exit");
        _icon.Icon = recording && !paused ? _recordingIcon : _idleIcon;
        var tip = recording
            ? Loc.F("Tray_Recording", steps) + (paused ? $" ({Loc.T("Status_Paused")})" : "")
            : Loc.T("App_Title");
        _icon.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (info != null) return new Icon(info.Stream, WinForms.SystemInformation.SmallIconSize);
        }
        catch { }
        return SystemIcons.Application;
    }

    private static Icon CreateRecordingIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var white = new SolidBrush(Color.White);
            using var red = new SolidBrush(Color.FromArgb(232, 17, 35));
            g.FillEllipse(white, 2, 2, 28, 28);
            g.FillEllipse(red, 5, 5, 22, 22);
        }
        var handle = bmp.GetHicon();
        // Clone so the GDI handle can be released immediately.
        using var temp = Icon.FromHandle(handle);
        var icon = (Icon)temp.Clone();
        Core.Native.DestroyIcon(handle);
        return icon;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _recordingIcon.Dispose();
    }
}
