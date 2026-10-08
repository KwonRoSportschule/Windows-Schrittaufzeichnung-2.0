# Schrittaufzeichnung 2.0 · Steps Recorder 2.0

Moderner, portabler Nachfolger der Windows-Schrittaufzeichnung (`psr.exe`). **Eine einzige `.exe`, keine Installation, keine Registry-Einträge.**

> 🇬🇧 *Modern, portable successor of the Windows Steps Recorder. Single .exe, no install, no registry writes. UI in German and English (switchable at runtime).*

## Features

| Bereich | Was es kann |
|---|---|
| **Aufnahme** | Screenshot bei jedem Maus-Klick (links/rechts/mittel, Doppelklick wird erkannt) und bei Tastatureingaben. Getippter Text wird zu *einem* Schritt zusammengefasst, Tastenkombinationen (Strg+C, F5 …) sind eigene Schritte. |
| **Automatische Beschreibung** | Per UI Automation: *„Linksklick auf „Speichern“ (Schaltfläche) in „Editor“"*. Angeklicktes Element wird grün umrahmt, Klickpunkt rot markiert. |
| **Datenschutz** | Getippter Text wird standardmäßig als `•••` maskiert, **Passwortfelder immer**. Klartext nur per Opt-in. |
| **Bearbeiten** | Schritte löschen (mit Rückgängig), per Drag & Drop oder `Strg+↑/↓` neu anordnen, Beschreibung ändern, Notizen hinzufügen. Titel für die Aufzeichnung. |
| **Export** | **PDF-Bericht** (eigener PDF-Writer, keine Fremdbibliothek), **interaktives HTML** (eine Datei, Suche, Einzel-/Gesamtansicht, Zoom, Hell/Dunkel, Druckansicht), **ZIP** (alle PNGs + `Schritte.txt` + Video). |
| **Projekte** | Speichern/Öffnen als `.steps`-Datei (ZIP). Drag & Drop einer `.steps`-Datei aufs Fenster öffnet sie. |
| **Video (optional)** | Bildschirmvideo parallel als MJPEG-AVI (2–30 FPS) – ohne externe Codecs oder ffmpeg. |
| **Hotkeys (global)** | `Strg+Umschalt+R` Start/Stopp · `Strg+Umschalt+P` Pause · `Strg+Umschalt+N` Kommentar – frei änderbar. |
| **Hintergrundbetrieb** | Hauptfenster verschwindet beim Aufnehmen, kleine schwebende Leiste + Tray-Icon. Die Leiste und eigene Dialoge sind per `WDA_EXCLUDEFROMCAPTURE` **unsichtbar in Screenshots und Video**. |
| **Design** | Fluent / Windows-11-Look, Hell + Dunkel + „wie System“, Titelleiste passt sich an. |
| **Sprache** | Deutsch / Englisch, live umschaltbar (oben rechts). |

## Download & Start

Unter **Actions → letzter Build → Artifacts** (oder bei Tags unter **Releases**) gibt es zwei Varianten:

| Datei | Größe | Voraussetzung |
|---|---|---|
| `StepRecorder2-portable-win-x64.exe` | ~67 MB | **Nichts.** Läuft auf jedem Windows 10/11 x64. |
| `StepRecorder2-net10-win-x64.exe` | ~0,5 MB | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

Einfach starten. Fertig.

## Bedienung

1. **Aufnahme starten** (Button oder `Strg+Umschalt+R`) → Fenster verschwindet, oben erscheint die Aufnahmeleiste.
2. Ganz normal arbeiten. Jeder Klick / jede Eingabe wird ein Schritt.
3. `Strg+Umschalt+N` (oder 💬 in der Leiste) fügt einen Kommentar-Schritt mit Screenshot ein.
4. **Stopp** → Liste prüfen, Beschreibungen/Notizen ergänzen, unnötige Schritte löschen, umsortieren.
5. **Exportieren → PDF / HTML / ZIP**.

| Taste | Aktion |
|---|---|
| `Entf` | Markierte Schritte löschen |
| `Strg+Z` | Löschen rückgängig |
| `Strg+↑ / Strg+↓` | Schritt verschieben |
| `Strg+N / O / S` | Neu / Öffnen / Speichern |
| Doppelklick aufs Bild | In Standard-Bildbetrachter öffnen |

## Portabilität – was wird wo gespeichert?

- **Einstellungen:** `StepRecorder2.settings.json` **neben der .exe**. Nur wenn der Ordner schreibgeschützt ist (z. B. `C:\Program Files`), weicht das Programm auf `%APPDATA%\StepRecorder2\` aus.
- **Arbeitsdaten einer Aufnahme:** `%TEMP%\StepRecorder2\<sitzung>\` – wird beim Beenden gelöscht (Reste abgestürzter Sitzungen nach 2 Tagen).
- **Registry:** wird **nur gelesen** (Hell/Dunkel-Einstellung von Windows), nie beschrieben.
- Hinweis zur Single-File-Technik: Die eigenständige .exe entpackt einige native WPF-DLLs beim ersten Start nach `%TEMP%\.net\`. Das ist ein .NET-Mechanismus; per Umgebungsvariable `DOTNET_BUNDLE_EXTRACT_BASE_DIR` lässt sich der Ort z. B. auf den USB-Stick legen.

## Selbst bauen

Voraussetzung: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (baut auch unter Linux/macOS dank `EnableWindowsTargeting`).

```powershell
# Portable Einzel-EXE ohne Laufzeit-Abhängigkeit (~67 MB)
dotnet publish src/StepRecorder -c Release -r win-x64 --self-contained true -o publish/portable

# Kleine Einzel-EXE (~0,5 MB, braucht .NET 10 Desktop Runtime)
dotnet publish src/StepRecorder -c Release -r win-x64 --self-contained false -o publish/small
```

Oder `StepRecorder.sln` in Visual Studio 2022/2026 öffnen.

### Selbsttest

`StepRecorder2.exe --smoke-test <ordner>` startet einen End-to-End-Test: echte Aufnahme über die Engine, Rendern aller Fenster (hell/dunkel, DE/EN) als PNG, alle Exporte, Video und Speichern/Laden. Läuft in GitHub Actions auf jedem Push; Screenshots und Exporte liegen im Artifact `smoke-test`.

## Architektur

```
src/StepRecorder/
├─ Core/
│  ├─ InputHookService.cs   Eigener Thread mit Message-Loop: WH_MOUSE_LL / WH_KEYBOARD_LL + RegisterHotKey
│  ├─ RecorderEngine.cs     Worker-Thread: Event → Screenshot → UI Automation → Markierung → PNG + Thumbnail
│  ├─ ScreenCapture.cs      GDI-Capture, Cursor, Klick-/Element-Markierung, JPEG/PNG
│  ├─ UiaInspector.cs       „Was wurde angeklickt?“ – immer mit Timeout (hängende Apps blockieren nichts)
│  ├─ VideoRecorder.cs      Echtzeit-Bildschirmvideo
│  └─ MjpegAviWriter.cs     Minimaler AVI-Container (RIFF + idx1)
├─ Export/                  PdfWriter (eigener PDF 1.4-Writer), PdfExporter, HtmlExporter, ZipExporter
├─ Services/                Settings (portabel), Loc (DE/EN), ThemeManager, ProjectStore, TrayIcon
├─ Themes/                  Light.xaml / Dark.xaml (Farben) + Controls.xaml (Fluent-Styles)
└─ Views/                   MainWindow, RecordingToolbar, SettingsWindow, FluentDialog
```

**Warum WPF statt WinUI 3?** WinUI 3 als *unpackaged single-file* ist bis heute fummelig (Windows App SDK Runtime, Bootstrapper, Ressourcen-PRI). WPF + eigene Fluent-Styles ergibt eine echte Einzel-.exe ohne Zusatz-Runtime und läuft auch auf Windows 10.

**Warum .NET 10 statt .NET 8?** .NET 8 hat Support-Ende im November 2026; .NET 10 ist das aktuelle LTS-Release (Support bis November 2028).

**Systemlast:** Hooks werden nur während der Aufnahme installiert und kopieren das Event nur in eine Queue. Capture passiert ereignisgesteuert (kein Polling). Ohne Video ist die CPU-Last im Leerlauf praktisch null.

## Bekannte Grenzen

- Fenster mit höheren Rechten (als Administrator gestartete Programme) sieht ein normal gestartetes Programm nicht – Windows-Sicherheitsmodell (UIPI). Dafür StepRecorder ebenfalls als Admin starten.
- Inhalte mit DRM-/Capture-Schutz (z. B. manche Video-Player) erscheinen schwarz.
- AVI-Videos sind auf ~1,9 GB begrenzt (bei 5 FPS / Full HD ca. 40 Minuten).
- Tote Tasten (`^`, `´`) werden in der Textvorschau nicht kombiniert.
