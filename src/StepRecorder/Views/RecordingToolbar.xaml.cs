using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using StepRecorder.Core;
using StepRecorder.Services;

namespace StepRecorder.Views;

/// <summary>
/// Small always-on-top control bar shown while recording. It is excluded from screen capture
/// (WDA_EXCLUDEFROMCAPTURE), so it never appears in screenshots or the video.
/// </summary>
public partial class RecordingToolbar : Window
{
    public event Action? PauseRequested, CommentRequested, StopRequested;

    public RecordingToolbar()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + 8;
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, Native.WDA_EXCLUDEFROMCAPTURE);
    }

    public void Update(TimeSpan elapsed, int steps, bool paused, bool video)
    {
        TimeText.Text = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"mm\:ss");
        StepsText.Text = Loc.F("Status_Steps", steps);
        PauseGlyph.Text = paused ? "" : "";
        PauseButton.ToolTip = Loc.T(paused ? "Btn_Resume" : "Btn_Pause");
        Dot.Fill = (System.Windows.Media.Brush)FindResource(paused ? "Brush.TextTertiary" : "Brush.Recording");
        VideoText.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnPause(object sender, RoutedEventArgs e) => PauseRequested?.Invoke();
    private void OnComment(object sender, RoutedEventArgs e) => CommentRequested?.Invoke();
    private void OnStop(object sender, RoutedEventArgs e) => StopRequested?.Invoke();
}
