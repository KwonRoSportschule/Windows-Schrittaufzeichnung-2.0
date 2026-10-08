using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StepRecorder.Core;
using StepRecorder.Services;

namespace StepRecorder.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        ThemeManager.Attach(this);
        _settings = current.Clone();

        Select(LanguageBox, _settings.Language);
        Select(ThemeBox, _settings.Theme.ToString());
        CaptureKeyboardBox.IsChecked = _settings.CaptureKeyboard;
        RecordTextBox.IsChecked = _settings.RecordTypedText;
        HighlightBox.IsChecked = _settings.HighlightClicks;
        CursorBox.IsChecked = _settings.IncludeCursor;
        AllScreensBox.IsChecked = _settings.CaptureAllScreens;
        HideWindowBox.IsChecked = _settings.HideMainWindowWhileRecording;
        ToolbarBox.IsChecked = _settings.ShowRecordingToolbar;
        VideoBox.IsChecked = _settings.RecordVideo;
        Select(FpsBox, _settings.VideoFps.ToString());
        Select(QualityBox, _settings.ExportJpegQuality.ToString());
        HkRecordBox.Text = _settings.HotkeyRecord;
        HkPauseBox.Text = _settings.HotkeyPause;
        HkCommentBox.Text = _settings.HotkeyComment;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"StepRecorder {version?.ToString(3)}  ·  .NET {Environment.Version}";
        SettingsPathBox.Text = AppSettings.FilePath;
    }

    /// <summary>The edited copy; only valid when ShowDialog() returned true.</summary>
    public AppSettings Result => _settings;

    private static void Select(ComboBox box, string value)
    {
        box.SelectedItem = box.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (i.Tag?.ToString() ?? i.Content?.ToString()) == value) ?? box.Items[0];
    }

    private static string Value(ComboBox box)
    {
        var item = (ComboBoxItem)box.SelectedItem;
        return item.Tag?.ToString() ?? item.Content?.ToString() ?? "";
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.Tab) return;
        var hk = Hotkey.FromKey(key, Keyboard.Modifiers);
        if (hk.IsValid) ((TextBox)sender).Text = hk.ToString();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _settings.Language = Value(LanguageBox);
        _settings.Theme = Enum.Parse<ThemeChoice>(Value(ThemeBox));
        _settings.CaptureKeyboard = CaptureKeyboardBox.IsChecked == true;
        _settings.RecordTypedText = RecordTextBox.IsChecked == true;
        _settings.HighlightClicks = HighlightBox.IsChecked == true;
        _settings.IncludeCursor = CursorBox.IsChecked == true;
        _settings.CaptureAllScreens = AllScreensBox.IsChecked == true;
        _settings.HideMainWindowWhileRecording = HideWindowBox.IsChecked == true;
        _settings.ShowRecordingToolbar = ToolbarBox.IsChecked == true;
        _settings.RecordVideo = VideoBox.IsChecked == true;
        _settings.VideoFps = int.Parse(Value(FpsBox));
        _settings.ExportJpegQuality = int.Parse(Value(QualityBox));
        _settings.HotkeyRecord = HkRecordBox.Text;
        _settings.HotkeyPause = HkPauseBox.Text;
        _settings.HotkeyComment = HkCommentBox.Text;
        DialogResult = true;
    }
}
