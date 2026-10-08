using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StepRecorder.Services;

public enum ThemeChoice { System, Light, Dark }

/// <summary>
/// Portable settings: stored as JSON next to the .exe. Only if that folder is read-only
/// (e.g. Program Files) we fall back to %APPDATA%\StepRecorder2. Never touches the registry.
/// </summary>
public sealed class AppSettings
{
    public string Language { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    public bool RecordTypedText { get; set; } = false;
    public bool CaptureAllScreens { get; set; } = false;
    public bool HighlightClicks { get; set; } = true;
    public bool IncludeCursor { get; set; } = true;
    public bool HideMainWindowWhileRecording { get; set; } = true;
    public bool ShowRecordingToolbar { get; set; } = true;
    public bool CaptureKeyboard { get; set; } = true;

    public bool RecordVideo { get; set; } = false;
    public int VideoFps { get; set; } = 5;
    public int VideoQuality { get; set; } = 70;

    public int ExportJpegQuality { get; set; } = 88;

    public string HotkeyRecord { get; set; } = "Ctrl+Shift+R";
    public string HotkeyPause { get; set; } = "Ctrl+Shift+P";
    public string HotkeyComment { get; set; } = "Ctrl+Shift+N";

    public string? LastFolder { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string FilePath { get; private set; } = "";

    public static AppSettings Load()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var portable = Path.Combine(exeDir, "StepRecorder2.settings.json");
        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StepRecorder2", "settings.json");

        FilePath = File.Exists(portable) || !File.Exists(roaming) && IsWritable(exeDir) ? portable : roaming;
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch { /* corrupt file -> defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        if (SmokeTest.Enabled) return; // CI self test must not touch real settings
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch { /* settings are best effort */ }
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, Options), Options)!;

    private static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }
}
