using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using StepRecorder.Core;
using StepRecorder.Services;

namespace StepRecorder.Views;

/// <summary>Windows 11 ContentDialog look-alike for questions, infos and text input.</summary>
public partial class FluentDialog : Window
{
    private bool? _result;

    private FluentDialog(Window? owner, string title, string message)
    {
        InitializeComponent();
        ThemeManager.Attach(this);
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        if (owner is { IsVisible: true }) Owner = owner;
        else { WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true; }
    }

    /// <summary>Hide this dialog from screenshots/video (used while recording).</summary>
    public bool ExcludeFromCapture { get; set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (ExcludeFromCapture)
            Native.SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, Native.WDA_EXCLUDEFROMCAPTURE);
    }

    private void Configure(string primary, string? secondary, string? cancel)
    {
        PrimaryButton.Content = primary;
        SecondaryButton.Content = secondary;
        CancelButton.Content = cancel;
        SecondaryButton.Visibility = secondary == null ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = cancel == null ? Visibility.Collapsed : Visibility.Visible;
        Buttons.Columns = 1 + (secondary == null ? 0 : 1) + (cancel == null ? 0 : 1);
        if (cancel == null && secondary == null) Buttons.HorizontalAlignment = HorizontalAlignment.Right;
    }

    private void OnPrimary(object sender, RoutedEventArgs e) { _result = true; Close(); }
    private void OnSecondary(object sender, RoutedEventArgs e) { _result = false; Close(); }
    private void OnCancel(object sender, RoutedEventArgs e) { _result = null; Close(); }

    /// <summary>true = primary, false = secondary, null = cancel/closed.</summary>
    public static bool? Ask(Window? owner, string title, string message, string primary, string? secondary, string? cancel)
    {
        var dlg = new FluentDialog(owner, title, message);
        dlg.Configure(primary, secondary, cancel);
        dlg.ShowDialog();
        return dlg._result;
    }

    public static void Info(Window? owner, string title, string message)
    {
        var dlg = new FluentDialog(owner, title, message);
        dlg.Configure("OK", null, null);
        dlg.PrimaryButton.MinWidth = 120;
        dlg.ShowDialog();
    }

    public static string? Prompt(Window? owner, string title, string message, string ok, bool excludeFromCapture = false)
    {
        var dlg = new FluentDialog(owner, title, message) { ExcludeFromCapture = excludeFromCapture };
        dlg.Configure(ok, null, Loc.T("Btn_Cancel"));
        dlg.Input.Visibility = Visibility.Visible;
        dlg.PrimaryButton.IsDefault = false; // Enter = new line; Ctrl+Enter = OK
        dlg.Input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { dlg._result = true; dlg.Close(); e.Handled = true; }
        };
        dlg.Loaded += (_, _) => { dlg.Activate(); dlg.Input.Focus(); };
        dlg.ShowDialog();
        return dlg._result == true ? dlg.Input.Text.Trim() : null;
    }
}
