using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace StepRecorder.Services;

/// <summary>
/// Tiny runtime-switchable localization. XAML: Text="{s:Tr Btn_Record}".
/// Switching the language raises "Item[]" so every binding refreshes instantly.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _language = "de";

    public string Language
    {
        get => _language;
        set
        {
            _language = value == "en" ? "en" : "de";
            Culture = CultureInfo.GetCultureInfo(_language == "de" ? "de-DE" : "en-US");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            LanguageChanged?.Invoke();
        }
    }

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("de-DE");
    public event Action? LanguageChanged;

    public string this[string key]
    {
        get
        {
            var table = _language == "en" ? En : De;
            return table.TryGetValue(key, out var v) ? v : De.TryGetValue(key, out var d) ? d : key;
        }
    }

    public static string T(string key) => Instance[key];
    public static string F(string key, params object?[] args) => string.Format(Instance.Culture, Instance[key], args);

    private static readonly Dictionary<string, string> De = new()
    {
        ["App_Title"] = "Schrittaufzeichnung 2.0",
        ["Untitled"] = "Unbenannte Aufzeichnung",
        ["Win_Taskbar"] = "Taskleiste",
        ["Win_Desktop"] = "Desktop",

        ["Btn_Record"] = "Aufnahme starten",
        ["Btn_Stop"] = "Aufnahme beenden",
        ["Btn_Pause"] = "Pause",
        ["Btn_Resume"] = "Fortsetzen",
        ["Btn_Comment"] = "Kommentar",
        ["Btn_New"] = "Neu",
        ["Btn_Open"] = "Öffnen",
        ["Btn_Save"] = "Speichern",
        ["Btn_Export"] = "Exportieren",
        ["Btn_Settings"] = "Einstellungen",
        ["Btn_Delete"] = "Löschen",
        ["Btn_MoveUp"] = "Nach oben",
        ["Btn_MoveDown"] = "Nach unten",
        ["Btn_Undo"] = "Löschen rückgängig",
        ["Btn_CopyImage"] = "Bild kopieren",
        ["Btn_OpenImage"] = "Bild in Standard-App öffnen",
        ["Btn_OK"] = "Speichern",
        ["Btn_Cancel"] = "Abbrechen",
        ["Btn_Add"] = "Hinzufügen",
        ["Btn_Language"] = "English",

        ["Export_Pdf"] = "PDF-Bericht",
        ["Export_PdfHint"] = "Druckfertiger Bericht mit Screenshots",
        ["Export_Html"] = "HTML (interaktiv)",
        ["Export_HtmlHint"] = "Eine Datei, im Browser durchklickbar",
        ["Export_Zip"] = "ZIP-Archiv",
        ["Export_ZipHint"] = "Alle Bilder + Textdatei (+ Video)",
        ["Export_Video"] = "Video speichern",
        ["Export_VideoHint"] = "Bildschirmvideo als AVI-Datei",

        ["Lbl_Steps"] = "Schritte",
        ["Lbl_Title"] = "Titel",
        ["Lbl_Description"] = "Beschreibung",
        ["Lbl_Note"] = "Notiz",
        ["Ph_Note"] = "Hinweis oder Erklärung zu diesem Schritt…",
        ["Lbl_StepOf"] = "Schritt {0} von {1}",
        ["Empty_Title"] = "Noch keine Schritte",
        ["Empty_Text"] = "Starte eine Aufnahme mit {0} oder über den Button oben. Jeder Klick und jede Eingabe wird automatisch als Schritt mit Screenshot gespeichert.",
        ["NoSelection"] = "Wähle links einen Schritt aus, um ihn zu bearbeiten.",

        ["Status_Ready"] = "Bereit",
        ["Status_Recording"] = "Aufnahme läuft",
        ["Status_Paused"] = "Pausiert",
        ["Status_Exporting"] = "Exportiere…",
        ["Status_Exported"] = "Exportiert: {0}",
        ["Status_Saved"] = "Gespeichert: {0}",
        ["Status_Opened"] = "Geöffnet: {0}",
        ["Status_Error"] = "Fehler: {0}",
        ["Status_Steps"] = "{0} Schritte",
        ["Status_Hotkeys"] = "{0} Start/Stopp  ·  {1} Pause  ·  {2} Kommentar",
        ["Status_HotkeyFailed"] = "Tastenkürzel {0} ist bereits belegt und wurde nicht registriert.",
        ["Status_Video"] = "Video",
        ["Status_VideoLimit"] = "Videoaufnahme gestoppt: Dateigrößenlimit erreicht.",

        ["Step_N"] = "Schritt {0}",
        ["Fmt_TargetElement"] = "„{0}“ ({1}) in „{2}“",
        ["Fmt_TargetElementNoType"] = "„{0}“ in „{1}“",
        ["Fmt_TargetWindow"] = "„{0}“",
        ["Desc_Click"] = "Linksklick auf {0}",
        ["Desc_DoubleClick"] = "Doppelklick auf {0}",
        ["Desc_RightClick"] = "Rechtsklick auf {0}",
        ["Desc_MiddleClick"] = "Mittelklick auf {0}",
        ["Desc_Typed"] = "Eingabe „{0}“ in {1}",
        ["Desc_Shortcut"] = "Tastenkombination {0} in {1}",
        ["Desc_Comment"] = "Kommentar",

        ["Key_Ctrl"] = "Strg",
        ["Key_Shift"] = "Umschalt",
        ["Key_Alt"] = "Alt",
        ["Key_Win"] = "Win",
        ["Key_Enter"] = "Enter",
        ["Key_Tab"] = "Tab",
        ["Key_Esc"] = "Esc",
        ["Key_Back"] = "Rücktaste",
        ["Key_Delete"] = "Entf",
        ["Key_Insert"] = "Einfg",
        ["Key_Home"] = "Pos1",
        ["Key_End"] = "Ende",
        ["Key_PageUp"] = "Bild↑",
        ["Key_PageDown"] = "Bild↓",
        ["Key_Space"] = "Leertaste",

        ["Set_Title"] = "Einstellungen",
        ["Set_General"] = "Allgemein",
        ["Set_Language"] = "Sprache",
        ["Set_Theme"] = "Design",
        ["Theme_System"] = "Systemstandard",
        ["Theme_Light"] = "Hell",
        ["Theme_Dark"] = "Dunkel",
        ["Set_Recording"] = "Aufnahme",
        ["Set_CaptureKeyboard"] = "Tastatureingaben als Schritte aufzeichnen",
        ["Set_RecordText"] = "Getippten Text im Klartext speichern",
        ["Set_RecordTextHint"] = "Aus: Zeichen werden als • maskiert. Passwortfelder werden immer maskiert.",
        ["Set_AllScreens"] = "Alle Bildschirme aufnehmen (statt nur den mit dem Klick)",
        ["Set_Highlight"] = "Klickpunkt und angeklicktes Element markieren",
        ["Set_Cursor"] = "Mauszeiger im Screenshot anzeigen",
        ["Set_HideWindow"] = "Hauptfenster während der Aufnahme ausblenden",
        ["Set_Toolbar"] = "Schwebende Aufnahmeleiste anzeigen",
        ["Set_Video"] = "Video",
        ["Set_RecordVideo"] = "Bildschirmvideo parallel aufzeichnen",
        ["Set_VideoFps"] = "Bilder pro Sekunde",
        ["Set_VideoHint"] = "MJPEG-AVI, ohne externe Codecs. Mehr FPS = flüssiger, aber mehr CPU und Speicher.",
        ["Set_Hotkeys"] = "Globale Tastenkürzel",
        ["Set_HkRecord"] = "Aufnahme starten / beenden",
        ["Set_HkPause"] = "Pause / Fortsetzen",
        ["Set_HkComment"] = "Kommentar hinzufügen",
        ["Set_HkHint"] = "Feld anklicken und neue Kombination drücken (mit Strg, Alt oder Win).",
        ["Set_Export"] = "Export",
        ["Set_JpegQuality"] = "Bildqualität in PDF/HTML",
        ["Set_About"] = "Info",
        ["Set_SettingsFile"] = "Einstellungen werden portabel gespeichert in:",

        ["Dlg_CommentTitle"] = "Kommentar hinzufügen",
        ["Dlg_CommentPrompt"] = "Was soll an dieser Stelle erklärt werden?",
        ["Dlg_UnsavedTitle"] = "Ungespeicherte Änderungen",
        ["Dlg_Unsaved"] = "Die aktuelle Aufzeichnung wurde noch nicht gespeichert. Jetzt speichern?",
        ["Dlg_NoSteps"] = "Es gibt noch keine Schritte zum Exportieren.",
        ["Dlg_ExitRecording"] = "Es läuft noch eine Aufnahme. Beenden und das Programm schließen?",
        ["Dlg_OpenFailed"] = "Die Datei konnte nicht geöffnet werden:\n{0}",
        ["Dlg_AlreadyRunning"] = "Schrittaufzeichnung 2.0 läuft bereits (siehe Infobereich der Taskleiste).",
        ["Dlg_DontSave"] = "Nicht speichern",

        ["Filter_Project"] = "Schrittaufzeichnung (*.steps)|*.steps",
        ["Filter_Pdf"] = "PDF-Dokument (*.pdf)|*.pdf",
        ["Filter_Html"] = "HTML-Datei (*.html)|*.html",
        ["Filter_Zip"] = "ZIP-Archiv (*.zip)|*.zip",
        ["Filter_Video"] = "AVI-Video (*.avi)|*.avi",

        ["Tray_Show"] = "Fenster anzeigen",
        ["Tray_Exit"] = "Beenden",
        ["Tray_Recording"] = "Aufnahme läuft – {0} Schritte",

        ["Rep_RecordedOn"] = "Aufgezeichnet am {0}",
        ["Rep_Steps"] = "{0} Schritte",
        ["Rep_CreatedWith"] = "Erstellt mit StepRecorder 2.0",
        ["Rep_Note"] = "Notiz",
        ["Rep_Page"] = "Seite {0} von {1}",
        ["Rep_Window"] = "Fenster",
        ["Rep_Time"] = "Zeit",
        ["Html_Search"] = "Schritte durchsuchen…",
        ["Html_All"] = "Alle anzeigen",
        ["Html_Single"] = "Einzelansicht",
        ["Html_Prev"] = "Zurück",
        ["Html_Next"] = "Weiter",
        ["Html_Theme"] = "Hell/Dunkel",
        ["Html_Print"] = "Drucken",
        ["Html_Hint"] = "← → blättern · Klick auf das Bild vergrößert",
        ["Html_NoResults"] = "Keine Treffer",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["App_Title"] = "Steps Recorder 2.0",
        ["Untitled"] = "Untitled recording",
        ["Win_Taskbar"] = "Taskbar",
        ["Win_Desktop"] = "Desktop",

        ["Btn_Record"] = "Start recording",
        ["Btn_Stop"] = "Stop recording",
        ["Btn_Pause"] = "Pause",
        ["Btn_Resume"] = "Resume",
        ["Btn_Comment"] = "Comment",
        ["Btn_New"] = "New",
        ["Btn_Open"] = "Open",
        ["Btn_Save"] = "Save",
        ["Btn_Export"] = "Export",
        ["Btn_Settings"] = "Settings",
        ["Btn_Delete"] = "Delete",
        ["Btn_MoveUp"] = "Move up",
        ["Btn_MoveDown"] = "Move down",
        ["Btn_Undo"] = "Undo delete",
        ["Btn_CopyImage"] = "Copy image",
        ["Btn_OpenImage"] = "Open image in default app",
        ["Btn_OK"] = "Save",
        ["Btn_Cancel"] = "Cancel",
        ["Btn_Add"] = "Add",
        ["Btn_Language"] = "Deutsch",

        ["Export_Pdf"] = "PDF report",
        ["Export_PdfHint"] = "Print-ready report with screenshots",
        ["Export_Html"] = "HTML (interactive)",
        ["Export_HtmlHint"] = "Single file, click through in any browser",
        ["Export_Zip"] = "ZIP archive",
        ["Export_ZipHint"] = "All images + text file (+ video)",
        ["Export_Video"] = "Save video",
        ["Export_VideoHint"] = "Screen video as AVI file",

        ["Lbl_Steps"] = "Steps",
        ["Lbl_Title"] = "Title",
        ["Lbl_Description"] = "Description",
        ["Lbl_Note"] = "Note",
        ["Ph_Note"] = "Add a hint or explanation for this step…",
        ["Lbl_StepOf"] = "Step {0} of {1}",
        ["Empty_Title"] = "No steps yet",
        ["Empty_Text"] = "Start recording with {0} or the button above. Every click and every input is saved automatically as a step with a screenshot.",
        ["NoSelection"] = "Select a step on the left to edit it.",

        ["Status_Ready"] = "Ready",
        ["Status_Recording"] = "Recording",
        ["Status_Paused"] = "Paused",
        ["Status_Exporting"] = "Exporting…",
        ["Status_Exported"] = "Exported: {0}",
        ["Status_Saved"] = "Saved: {0}",
        ["Status_Opened"] = "Opened: {0}",
        ["Status_Error"] = "Error: {0}",
        ["Status_Steps"] = "{0} steps",
        ["Status_Hotkeys"] = "{0} start/stop  ·  {1} pause  ·  {2} comment",
        ["Status_HotkeyFailed"] = "Hotkey {0} is already in use and was not registered.",
        ["Status_Video"] = "Video",
        ["Status_VideoLimit"] = "Video recording stopped: file size limit reached.",

        ["Step_N"] = "Step {0}",
        ["Fmt_TargetElement"] = "\"{0}\" ({1}) in \"{2}\"",
        ["Fmt_TargetElementNoType"] = "\"{0}\" in \"{1}\"",
        ["Fmt_TargetWindow"] = "\"{0}\"",
        ["Desc_Click"] = "Left click on {0}",
        ["Desc_DoubleClick"] = "Double click on {0}",
        ["Desc_RightClick"] = "Right click on {0}",
        ["Desc_MiddleClick"] = "Middle click on {0}",
        ["Desc_Typed"] = "Typed \"{0}\" in {1}",
        ["Desc_Shortcut"] = "Pressed {0} in {1}",
        ["Desc_Comment"] = "Comment",

        ["Key_Ctrl"] = "Ctrl",
        ["Key_Shift"] = "Shift",
        ["Key_Alt"] = "Alt",
        ["Key_Win"] = "Win",
        ["Key_Enter"] = "Enter",
        ["Key_Tab"] = "Tab",
        ["Key_Esc"] = "Esc",
        ["Key_Back"] = "Backspace",
        ["Key_Delete"] = "Del",
        ["Key_Insert"] = "Ins",
        ["Key_Home"] = "Home",
        ["Key_End"] = "End",
        ["Key_PageUp"] = "PgUp",
        ["Key_PageDown"] = "PgDn",
        ["Key_Space"] = "Space",

        ["Set_Title"] = "Settings",
        ["Set_General"] = "General",
        ["Set_Language"] = "Language",
        ["Set_Theme"] = "Theme",
        ["Theme_System"] = "Use system setting",
        ["Theme_Light"] = "Light",
        ["Theme_Dark"] = "Dark",
        ["Set_Recording"] = "Recording",
        ["Set_CaptureKeyboard"] = "Record keyboard input as steps",
        ["Set_RecordText"] = "Store typed text in plain text",
        ["Set_RecordTextHint"] = "Off: characters are masked as •. Password fields are always masked.",
        ["Set_AllScreens"] = "Capture all screens (instead of the clicked one)",
        ["Set_Highlight"] = "Highlight click point and clicked element",
        ["Set_Cursor"] = "Show mouse cursor in screenshots",
        ["Set_HideWindow"] = "Hide main window while recording",
        ["Set_Toolbar"] = "Show floating recording toolbar",
        ["Set_Video"] = "Video",
        ["Set_RecordVideo"] = "Record screen video in parallel",
        ["Set_VideoFps"] = "Frames per second",
        ["Set_VideoHint"] = "MJPEG AVI, no external codecs. More FPS = smoother, but more CPU and disk.",
        ["Set_Hotkeys"] = "Global hotkeys",
        ["Set_HkRecord"] = "Start / stop recording",
        ["Set_HkPause"] = "Pause / resume",
        ["Set_HkComment"] = "Add comment",
        ["Set_HkHint"] = "Click a field and press the new combination (with Ctrl, Alt or Win).",
        ["Set_Export"] = "Export",
        ["Set_JpegQuality"] = "Image quality in PDF/HTML",
        ["Set_About"] = "About",
        ["Set_SettingsFile"] = "Settings are stored portably in:",

        ["Dlg_CommentTitle"] = "Add comment",
        ["Dlg_CommentPrompt"] = "What should be explained at this point?",
        ["Dlg_UnsavedTitle"] = "Unsaved changes",
        ["Dlg_Unsaved"] = "The current recording has not been saved. Save it now?",
        ["Dlg_NoSteps"] = "There are no steps to export yet.",
        ["Dlg_ExitRecording"] = "A recording is still running. Stop it and exit?",
        ["Dlg_OpenFailed"] = "The file could not be opened:\n{0}",
        ["Dlg_AlreadyRunning"] = "Steps Recorder 2.0 is already running (see notification area).",
        ["Dlg_DontSave"] = "Don't save",

        ["Filter_Project"] = "Steps recording (*.steps)|*.steps",
        ["Filter_Pdf"] = "PDF document (*.pdf)|*.pdf",
        ["Filter_Html"] = "HTML file (*.html)|*.html",
        ["Filter_Zip"] = "ZIP archive (*.zip)|*.zip",
        ["Filter_Video"] = "AVI video (*.avi)|*.avi",

        ["Tray_Show"] = "Show window",
        ["Tray_Exit"] = "Exit",
        ["Tray_Recording"] = "Recording – {0} steps",

        ["Rep_RecordedOn"] = "Recorded on {0}",
        ["Rep_Steps"] = "{0} steps",
        ["Rep_CreatedWith"] = "Created with StepRecorder 2.0",
        ["Rep_Note"] = "Note",
        ["Rep_Page"] = "Page {0} of {1}",
        ["Rep_Window"] = "Window",
        ["Rep_Time"] = "Time",
        ["Html_Search"] = "Search steps…",
        ["Html_All"] = "Show all",
        ["Html_Single"] = "Single view",
        ["Html_Prev"] = "Previous",
        ["Html_Next"] = "Next",
        ["Html_Theme"] = "Light/Dark",
        ["Html_Print"] = "Print",
        ["Html_Hint"] = "← → to navigate · click image to zoom",
        ["Html_NoResults"] = "No matches",
    };
}

/// <summary>XAML markup extension: {s:Tr Key}</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string key) { Key = key; }

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
