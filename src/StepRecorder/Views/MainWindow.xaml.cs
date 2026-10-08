using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using StepRecorder.Core;
using StepRecorder.Export;
using StepRecorder.Models;
using StepRecorder.Services;

namespace StepRecorder.Views;

public partial class MainWindow : Window
{
    private const int HotkeyRecordId = 1, HotkeyPauseId = 2, HotkeyCommentId = 3;

    private enum RecState { Idle, Recording, Paused }

    private readonly InputHookService _hooks = new();
    private readonly RecorderEngine _engine = new();
    private readonly VideoRecorder _video = new();
    private readonly TrayIcon _tray = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Stopwatch _recClock = new();
    private readonly List<(int Index, Step Step)> _lastDeleted = new();

    private Session _session = null!;
    private RecordingToolbar? _toolbar;
    private RecState _state = RecState.Idle;
    private string? _videoPath;
    private bool _busy, _exiting, _commentOpen;
    private Point _dragStart;
    private Step? _dragStep;

    private static AppSettings Settings => App.Settings;

    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.Attach(this);

        _hooks.MouseDown += _engine.OnMouse;
        _hooks.KeyDown += _engine.OnKey;
        _hooks.HotkeyPressed += id => Dispatcher.BeginInvoke(() => OnHotkey(id));

        _engine.StepCaptured += step => Dispatcher.BeginInvoke(() => OnStepCaptured(step));
        _engine.StepUpdated += (id, kind, desc) => Dispatcher.BeginInvoke(() =>
        {
            var step = _session.Steps.FirstOrDefault(s => s.Id == id);
            if (step != null) { step.Kind = kind; step.Description = desc; }
        });
        _engine.Error += ex => Dispatcher.BeginInvoke(() => SetStatus(Loc.F("Status_Error", ex.Message)));

        _video.LimitReached += () => Dispatcher.BeginInvoke(() => SetStatus(Loc.T("Status_VideoLimit")));
        _video.Error += ex => Dispatcher.BeginInvoke(() => SetStatus(Loc.F("Status_Error", ex.Message)));

        _tray.RecordClicked += () => Dispatcher.BeginInvoke(ToggleRecording);
        _tray.PauseClicked += () => Dispatcher.BeginInvoke(TogglePause);
        _tray.ShowClicked += () => Dispatcher.BeginInvoke(ShowFromTray);
        _tray.ExitClicked += () => Dispatcher.BeginInvoke(Close);

        _timer.Tick += (_, _) => UpdateRecordingInfo();
        Loc.Instance.LanguageChanged += UpdateUi;

        SetSession(ProjectStore.CreateSession());
        RegisterHotkeys();
        SetStatus(Loc.T("Status_Ready"));
    }

    // ================================================================== session

    private void SetSession(Session session)
    {
        if (_session != null)
        {
            _session.PropertyChanged -= OnSessionChanged;
            _session.Steps.CollectionChanged -= OnStepsChanged;
            ProjectStore.DeleteSession(_session);
        }
        _session = session;
        _session.PropertyChanged += OnSessionChanged;
        _session.Steps.CollectionChanged += OnStepsChanged;
        _lastDeleted.Clear();
        DataContext = _session;
        if (_session.Steps.Count > 0) StepList.SelectedIndex = 0;
        UpdateUi();
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Session.Title) or nameof(Session.IsDirty)) UpdateTitle();
    }

    private void OnStepsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateUi();

    /// <summary>Used by the CI smoke test to show an externally captured session.</summary>
    internal void ShowSession(Session session) => SetSession(session);

    private bool ConfirmDiscard()
    {
        if (SmokeTest.Enabled) return true;
        if (!_session.IsDirty || _session.Steps.Count == 0) return true;
        var answer = FluentDialog.Ask(this, Loc.T("Dlg_UnsavedTitle"), Loc.T("Dlg_Unsaved"),
            Loc.T("Btn_Save"), Loc.T("Dlg_DontSave"), Loc.T("Btn_Cancel"));
        return answer switch
        {
            true => Save(false),
            false => true,
            null => false
        };
    }

    public void OpenProject(string path)
    {
        if (_state != RecState.Idle || !ConfirmDiscard()) return;
        try
        {
            SetSession(ProjectStore.Load(path));
            Settings.LastFolder = Path.GetDirectoryName(path);
            SetStatus(Loc.F("Status_Opened", Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            FluentDialog.Info(this, Loc.T("App_Title"), Loc.F("Dlg_OpenFailed", ex.Message));
        }
    }

    private bool Save(bool saveAs)
    {
        var path = _session.FilePath;
        if (saveAs || path == null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = Loc.T("Filter_Project"),
                FileName = SuggestedName() + ".steps",
                InitialDirectory = Settings.LastFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }
        try
        {
            ProjectStore.Save(_session, path);
            Settings.LastFolder = Path.GetDirectoryName(path);
            SetStatus(Loc.F("Status_Saved", path));
            UpdateTitle();
            return true;
        }
        catch (Exception ex)
        {
            FluentDialog.Info(this, Loc.T("App_Title"), Loc.F("Status_Error", ex.Message));
            return false;
        }
    }

    private string SuggestedName()
    {
        var name = string.IsNullOrWhiteSpace(_session.Title)
            ? (Loc.Instance.Language == "de" ? "Aufzeichnung" : "Recording") + $"_{_session.Created:yyyy-MM-dd_HHmm}"
            : _session.Title.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length > 80 ? name[..80] : name;
    }

    private void OnNew(object sender, ExecutedRoutedEventArgs e)
    {
        if (_state != RecState.Idle || !ConfirmDiscard()) return;
        SetSession(ProjectStore.CreateSession());
        SetStatus(Loc.T("Status_Ready"));
    }

    private void OnOpen(object sender, ExecutedRoutedEventArgs e)
    {
        if (_state != RecState.Idle) return;
        var dlg = new OpenFileDialog
        {
            Filter = Loc.T("Filter_Project"),
            InitialDirectory = Settings.LastFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dlg.ShowDialog(this) == true) OpenProject(dlg.FileName);
    }

    private void OnSave(object sender, ExecutedRoutedEventArgs e) { if (_state == RecState.Idle) Save(false); }
    private void OnSaveAs(object sender, ExecutedRoutedEventArgs e) { if (_state == RecState.Idle) Save(true); }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files &&
            files[0].EndsWith(".steps", StringComparison.OrdinalIgnoreCase))
            OpenProject(files[0]);
    }

    // ================================================================== recording

    private void OnRecordClick(object sender, RoutedEventArgs e) => ToggleRecording();
    private void OnPauseClick(object sender, RoutedEventArgs e) => TogglePause();
    private void OnCommentClick(object sender, RoutedEventArgs e) => AddComment();

    private void OnHotkey(int id)
    {
        switch (id)
        {
            case HotkeyRecordId: ToggleRecording(); break;
            case HotkeyPauseId: TogglePause(); break;
            case HotkeyCommentId: AddComment(); break;
        }
    }

    private void ToggleRecording()
    {
        if (_busy) return;
        if (_state == RecState.Idle) StartRecording(); else StopRecording();
    }

    private void StartRecording()
    {
        var ignored = new List<Hotkey>();
        foreach (var text in new[] { Settings.HotkeyRecord, Settings.HotkeyPause, Settings.HotkeyComment })
            if (Hotkey.TryParse(text, out var hk)) ignored.Add(hk);

        _engine.Start(_session.Directory, new RecorderOptions(
            Settings.CaptureKeyboard, Settings.RecordTypedText, Settings.CaptureAllScreens,
            Settings.HighlightClicks, Settings.IncludeCursor, ignored));
        _hooks.TranslateCharacters = Settings.CaptureKeyboard;
        _hooks.InstallHooks();

        _videoPath = null;
        if (Settings.RecordVideo)
        {
            var dir = Path.Combine(_session.Directory, "video");
            Directory.CreateDirectory(dir);
            _videoPath = Path.Combine(dir, $"video_{DateTime.Now:yyyyMMdd_HHmmss}.avi");
            _video.Start(_videoPath, Settings.VideoFps, Settings.VideoQuality, Settings.CaptureAllScreens, Settings.IncludeCursor);
        }

        _state = RecState.Recording;
        _recClock.Restart();
        _timer.Start();

        if (Settings.ShowRecordingToolbar)
        {
            _toolbar = new RecordingToolbar();
            _toolbar.PauseRequested += TogglePause;
            _toolbar.CommentRequested += AddComment;
            _toolbar.StopRequested += StopRecording;
            _toolbar.Show();
        }
        if (Settings.HideMainWindowWhileRecording) Hide();
        else WindowState = WindowState.Minimized;

        UpdateUi();
    }

    private async void StopRecording()
    {
        if (_state == RecState.Idle || _busy) return;
        _busy = true;
        _hooks.UninstallHooks();
        _timer.Stop();
        _recClock.Stop();
        _toolbar?.Close();
        _toolbar = null;
        SetStatus(Loc.T("Status_Ready"));

        // Finishing pending captures and closing the video file can take a moment - keep the UI alive.
        await Task.Run(() =>
        {
            _engine.Stop();
            _video.Stop();
        });

        if (_videoPath != null && File.Exists(_videoPath))
        {
            _session.Videos.Add(_videoPath);
            _session.IsDirty = true;
        }
        _videoPath = null;
        _state = RecState.Idle;
        _busy = false;

        ShowFromTray();
        if (_session.Steps.Count > 0)
        {
            StepList.SelectedIndex = _session.Steps.Count - 1;
            StepList.ScrollIntoView(StepList.SelectedItem);
        }
        UpdateUi();
    }

    private void TogglePause()
    {
        if (_state == RecState.Idle) return;
        bool pause = _state == RecState.Recording;
        _state = pause ? RecState.Paused : RecState.Recording;
        _engine.IsPaused = pause;
        _video.IsPaused = pause;
        if (pause) _recClock.Stop(); else _recClock.Start();
        UpdateUi();
    }

    private async void AddComment()
    {
        if (_state == RecState.Idle || _commentOpen) return;
        _commentOpen = true;
        try
        {
            var owner = IsVisible && WindowState != WindowState.Minimized ? this : null;
            var text = FluentDialog.Prompt(owner, Loc.T("Dlg_CommentTitle"), Loc.T("Dlg_CommentPrompt"),
                Loc.T("Btn_Add"), excludeFromCapture: true);
            if (text == null || _state == RecState.Idle) return;
            await Task.Delay(200); // let the dialog disappear before the screenshot
            _engine.AddComment(text);
        }
        finally { _commentOpen = false; }
    }

    private void OnStepCaptured(Step step)
    {
        _session.Steps.Add(step);
        if (IsVisible) StepList.ScrollIntoView(step);
        UpdateRecordingInfo();
    }

    private void UpdateRecordingInfo()
    {
        var steps = _session.Steps.Count;
        _toolbar?.Update(_recClock.Elapsed, steps, _state == RecState.Paused, _video.IsRunning);
        _tray.Update(_state != RecState.Idle, _state == RecState.Paused, steps);
        ElapsedText.Text = _state == RecState.Idle ? "" : _recClock.Elapsed.ToString(@"hh\:mm\:ss");
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    // ================================================================== hotkeys

    private void RegisterHotkeys()
    {
        var map = new Dictionary<int, Hotkey>();
        var names = new Dictionary<int, string>
        {
            [HotkeyRecordId] = Settings.HotkeyRecord,
            [HotkeyPauseId] = Settings.HotkeyPause,
            [HotkeyCommentId] = Settings.HotkeyComment
        };
        foreach (var (id, text) in names)
            if (Hotkey.TryParse(text, out var hk)) map[id] = hk;
        var failed = _hooks.SetHotkeys(map);
        if (failed.Count > 0)
            SetStatus(Loc.F("Status_HotkeyFailed", string.Join(", ", failed.Select(id => names[id]))));
    }

    private static string Display(string hotkey) =>
        Loc.Instance.Language == "de"
            ? hotkey.Replace("Ctrl", "Strg").Replace("Shift", "Umschalt")
            : hotkey;

    // ================================================================== editing

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateUi();

    private List<Step> SelectedSteps() =>
        StepList.SelectedItems.Cast<Step>().OrderBy(s => _session.Steps.IndexOf(s)).ToList();

    private void OnDelete(object sender, RoutedEventArgs e) => DeleteSelected();

    private void DeleteSelected()
    {
        var selected = SelectedSteps();
        if (selected.Count == 0) return;
        int first = _session.Steps.IndexOf(selected[0]);
        _lastDeleted.Clear();
        foreach (var s in selected) _lastDeleted.Add((_session.Steps.IndexOf(s), s));
        foreach (var s in selected) _session.Steps.Remove(s);
        if (_session.Steps.Count > 0)
            StepList.SelectedIndex = Math.Min(first, _session.Steps.Count - 1);
        UpdateUi();
    }

    private void OnUndo(object sender, ExecutedRoutedEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) { ((TextBox)Keyboard.FocusedElement).Undo(); return; }
        if (_lastDeleted.Count == 0) return;
        StepList.SelectedItems.Clear();
        foreach (var (index, step) in _lastDeleted.OrderBy(x => x.Index))
        {
            _session.Steps.Insert(Math.Min(index, _session.Steps.Count), step);
            StepList.SelectedItems.Add(step);
        }
        _lastDeleted.Clear();
        UpdateUi();
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int delta)
    {
        var selected = SelectedSteps();
        if (selected.Count == 0) return;
        var ordered = delta < 0 ? selected : Enumerable.Reverse(selected).ToList();
        foreach (var s in ordered)
        {
            int i = _session.Steps.IndexOf(s);
            int j = i + delta;
            if (j < 0 || j >= _session.Steps.Count || selected.Contains(_session.Steps[j])) continue;
            _session.Steps.Move(i, j);
        }
        StepList.ScrollIntoView(selected[0]);
        UpdateUi();
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Up) { MoveSelected(-1); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Down) { MoveSelected(1); e.Handled = true; }
    }

    // Drag & drop reordering --------------------------------------------------

    private void OnListMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(StepList);
        _dragStep = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as Step;
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStep == null || e.LeftButton != MouseButtonState.Pressed || Keyboard.Modifiers != ModifierKeys.None) return;
        var delta = e.GetPosition(StepList) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var step = _dragStep;
        _dragStep = null;
        DragDrop.DoDragDrop(StepList, new DataObject(typeof(Step), step), DragDropEffects.Move);
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(Step)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Step)) is not Step source) return;
        e.Handled = true;
        var target = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as Step;
        int from = _session.Steps.IndexOf(source);
        int to = target == null ? _session.Steps.Count - 1 : _session.Steps.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        _session.Steps.Move(from, to);
        StepList.SelectedItem = source;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    // Image actions -------------------------------------------------------------

    private Step? Current => StepList.SelectedItem as Step;

    private void OnCopyImage(object sender, RoutedEventArgs e)
    {
        if (Current is not { } step || !File.Exists(step.ImagePath)) return;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.UriSource = new Uri(step.ImagePath);
            img.EndInit();
            Clipboard.SetImage(img);
        }
        catch (Exception ex) { SetStatus(Loc.F("Status_Error", ex.Message)); }
    }

    private void OnOpenImage(object sender, RoutedEventArgs e)
    {
        if (Current is { } step && File.Exists(step.ImagePath)) ShellOpen(step.ImagePath);
    }

    private void OnImageMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) OnOpenImage(sender, e);
    }

    private static void ShellOpen(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    // ================================================================== export

    private void OnExportPdf(object sender, RoutedEventArgs e) =>
        RunExport("Filter_Pdf", ".pdf", PdfExporter.Export, openAfter: true);

    private void OnExportHtml(object sender, RoutedEventArgs e) =>
        RunExport("Filter_Html", ".html", HtmlExporter.Export, openAfter: true);

    private void OnExportZip(object sender, RoutedEventArgs e) =>
        RunExport("Filter_Zip", ".zip", ZipExporter.Export, openAfter: false);

    private void OnExportVideo(object sender, RoutedEventArgs e)
    {
        ExportToggle.IsChecked = false;
        var video = _session.Videos.LastOrDefault(File.Exists);
        if (video == null) return;
        var dlg = new SaveFileDialog
        {
            Filter = Loc.T("Filter_Video"),
            FileName = SuggestedName() + ".avi",
            InitialDirectory = Settings.LastFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.Copy(video, dlg.FileName, overwrite: true);
            SetStatus(Loc.F("Status_Exported", dlg.FileName));
            RevealInExplorer(dlg.FileName);
        }
        catch (Exception ex) { SetStatus(Loc.F("Status_Error", ex.Message)); }
    }

    private async void RunExport(string filterKey, string extension,
        Action<ExportData, string, IProgress<double>?> exporter, bool openAfter)
    {
        ExportToggle.IsChecked = false;
        if (_busy || _state != RecState.Idle) return;
        if (_session.Steps.Count == 0)
        {
            FluentDialog.Info(this, Loc.T("Btn_Export"), Loc.T("Dlg_NoSteps"));
            return;
        }
        var dlg = new SaveFileDialog
        {
            Filter = Loc.T(filterKey),
            FileName = SuggestedName() + extension,
            InitialDirectory = Settings.LastFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dlg.ShowDialog(this) != true) return;

        var path = dlg.FileName;
        Settings.LastFolder = Path.GetDirectoryName(path);
        var data = ExportData.From(_session, Loc.T("Untitled"), Settings.ExportJpegQuality);
        var progress = new Progress<double>(p => SetStatus($"{Loc.T("Status_Exporting")} {p:P0}"));

        _busy = true;
        CommandBar.IsEnabled = false;
        try
        {
            SetStatus(Loc.T("Status_Exporting"));
            await Task.Run(() => exporter(data, path, progress));
            SetStatus(Loc.F("Status_Exported", path));
            if (openAfter) ShellOpen(path); else RevealInExplorer(path);
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("Status_Error", ex.Message));
            FluentDialog.Info(this, Loc.T("Btn_Export"), Loc.F("Status_Error", ex.Message));
        }
        finally
        {
            _busy = false;
            CommandBar.IsEnabled = true;
        }
    }

    private static void RevealInExplorer(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
    }

    // ================================================================== settings / theme / language

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        // Hotkeys are suspended so they can be typed into the hotkey fields.
        _hooks.SetHotkeys(new Dictionary<int, Hotkey>());
        var dlg = new SettingsWindow(Settings) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            var s = dlg.Result;
            Settings.Language = s.Language;
            Settings.Theme = s.Theme;
            Settings.CaptureKeyboard = s.CaptureKeyboard;
            Settings.RecordTypedText = s.RecordTypedText;
            Settings.HighlightClicks = s.HighlightClicks;
            Settings.IncludeCursor = s.IncludeCursor;
            Settings.CaptureAllScreens = s.CaptureAllScreens;
            Settings.HideMainWindowWhileRecording = s.HideMainWindowWhileRecording;
            Settings.ShowRecordingToolbar = s.ShowRecordingToolbar;
            Settings.RecordVideo = s.RecordVideo;
            Settings.VideoFps = s.VideoFps;
            Settings.ExportJpegQuality = s.ExportJpegQuality;
            Settings.HotkeyRecord = s.HotkeyRecord;
            Settings.HotkeyPause = s.HotkeyPause;
            Settings.HotkeyComment = s.HotkeyComment;
            Settings.Save();
            if (Loc.Instance.Language != Settings.Language) Loc.Instance.Language = Settings.Language;
            ThemeManager.Apply(Settings.Theme);
        }
        RegisterHotkeys();
        UpdateUi();
    }

    private void OnLanguageClick(object sender, RoutedEventArgs e)
    {
        Settings.Language = Loc.Instance.Language == "de" ? "en" : "de";
        Loc.Instance.Language = Settings.Language;
        Settings.Save();
    }

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        Settings.Theme = ThemeManager.IsDark ? ThemeChoice.Light : ThemeChoice.Dark;
        ThemeManager.Apply(Settings.Theme);
        Settings.Save();
    }

    // ================================================================== UI state

    private void SetStatus(string text) => StatusText.Text = text;

    private void UpdateTitle()
    {
        var name = string.IsNullOrWhiteSpace(_session.Title)
            ? (_session.FilePath != null ? Path.GetFileNameWithoutExtension(_session.FilePath) : Loc.T("Untitled"))
            : _session.Title;
        Title = $"{(_session.IsDirty && _session.Steps.Count > 0 ? "● " : "")}{name} – {Loc.T("App_Title")}";
    }

    private void UpdateUi()
    {
        bool idle = _state == RecState.Idle;
        bool recording = _state == RecState.Recording;
        int count = _session.Steps.Count;

        RecordButton.Style = (Style)FindResource(idle ? "AccentButton" : "DangerButton");
        RecordGlyph.Text = idle ? "" : "";
        RecordText.Text = Loc.T(idle ? "Btn_Record" : "Btn_Stop");
        PauseButton.IsEnabled = !idle;
        PauseGlyph.Text = _state == RecState.Paused ? "" : "";
        PauseText.Text = Loc.T(_state == RecState.Paused ? "Btn_Resume" : "Btn_Pause");
        CommentButton.IsEnabled = !idle;
        ExportVideoButton.Visibility = _session.Videos.Any(File.Exists) ? Visibility.Visible : Visibility.Collapsed;

        CountText.Text = count.ToString();
        EmptyState.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = Loc.F("Empty_Text", Display(Settings.HotkeyRecord));

        int selected = StepList.SelectedItems.Count;
        DeleteButton.IsEnabled = selected > 0;
        UpButton.IsEnabled = selected > 0;
        DownButton.IsEnabled = selected > 0;
        UndoButton.Visibility = _lastDeleted.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (Current is { } step)
            StepHeader.Text = Loc.F("Lbl_StepOf", step.Number, count);

        StatusDot.Fill = (Brush)FindResource(recording ? "Brush.Recording" : _state == RecState.Paused ? "Brush.Warning" : "Brush.TextTertiary");
        if (!idle) SetStatus(Loc.T(recording ? "Status_Recording" : "Status_Paused"));
        HotkeyText.Text = Loc.F("Status_Hotkeys", Display(Settings.HotkeyRecord), Display(Settings.HotkeyPause), Display(Settings.HotkeyComment));

        UpdateTitle();
        UpdateRecordingInfo();
    }

    // ================================================================== shutdown

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_exiting) { base.OnClosing(e); return; }

        if (_state != RecState.Idle)
        {
            ShowFromTray();
            var stop = FluentDialog.Ask(this, Loc.T("App_Title"), Loc.T("Dlg_ExitRecording"),
                Loc.T("Tray_Exit"), null, Loc.T("Btn_Cancel"));
            if (stop != true) { e.Cancel = true; return; }
            _hooks.UninstallHooks();
            _engine.Stop();
            _video.Stop();
            _toolbar?.Close();
            if (_videoPath != null && File.Exists(_videoPath)) _session.Videos.Add(_videoPath);
            _state = RecState.Idle;
        }

        if (!ConfirmDiscard()) { e.Cancel = true; return; }

        _exiting = true;
        Settings.Save();
        _timer.Stop();
        _hooks.Dispose();
        _tray.Dispose();
        ProjectStore.DeleteSession(_session);
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current.Shutdown();
    }
}
