using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using StepRecorder.Services;
using StepRecorder.Views;

namespace StepRecorder;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool smoke = SmokeTest.TryEnable(e.Args);
        Settings = smoke ? new AppSettings { Language = "de" } : AppSettings.Load();
        Loc.Instance.Language = Settings.Language;

        // Global hooks + hotkeys from two instances would fight each other.
        _singleInstance = new Mutex(true, @"Local\StepRecorder2.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show(Loc.T("Dlg_AlreadyRunning"), Loc.T("App_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log(args.ExceptionObject as Exception);

        try
        {
            ThemeManager.Apply(Settings.Theme);
            ProjectStore.CleanupOldSessions();

            var main = new MainWindow();
            MainWindow = main;
            main.Show();

            if (smoke) SmokeTest.Run(main);
            // "StepRecorder2.exe file.steps" (e.g. double click on a project file)
            else if (e.Args.Length > 0 && File.Exists(e.Args[0])) main.OpenProject(e.Args[0]);
        }
        catch (Exception ex)
        {
            if (smoke) SmokeTest.Fail(ex);
            // ShutdownMode is explicit - never leave an invisible zombie process behind.
            Log(ex);
            MessageBox.Show(ex.ToString(), Loc.T("App_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (SmokeTest.Enabled) SmokeTest.Fail(e.Exception);
        Log(e.Exception);
        MessageBox.Show(e.Exception.Message, Loc.T("App_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void Log(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var path = Path.Combine(ProjectStore.TempRoot, "error.log");
            Directory.CreateDirectory(ProjectStore.TempRoot);
            File.AppendAllText(path, $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
