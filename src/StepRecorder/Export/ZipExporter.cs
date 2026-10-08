using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using StepRecorder.Services;

namespace StepRecorder.Export;

/// <summary>ZIP with numbered PNG screenshots, a readable text file and any recorded videos.</summary>
public static class ZipExporter
{
    public static void Export(ExportData data, string path, IProgress<double>? progress = null)
    {
        var culture = Loc.Instance.Culture;
        var imagePrefix = Loc.Instance.Language == "de" ? "Schritt" : "Step";
        var textName = Loc.Instance.Language == "de" ? "Schritte.txt" : "Steps.txt";

        var temp = path + ".tmp";
        using (var fs = File.Create(temp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var txt = new StringBuilder();
            txt.AppendLine(data.Title);
            txt.AppendLine(new string('=', Math.Min(60, Math.Max(10, data.Title.Length))));
            txt.AppendLine(Loc.F("Rep_RecordedOn", data.Created.ToString("f", culture)));
            txt.AppendLine(Loc.F("Rep_Steps", data.Steps.Count));
            txt.AppendLine();

            for (int i = 0; i < data.Steps.Count; i++)
            {
                var s = data.Steps[i];
                var imageName = $"{imagePrefix}_{s.Number:000}.png";
                txt.AppendLine($"{Loc.F("Step_N", s.Number)}  ({s.Time.ToString("T", culture)})");
                txt.AppendLine($"  {s.Description}");
                if (!string.IsNullOrWhiteSpace(s.Window)) txt.AppendLine($"  {Loc.T("Rep_Window")}: {s.Window}");
                if (!string.IsNullOrWhiteSpace(s.Note))
                    txt.AppendLine($"  {Loc.T("Rep_Note")}: {s.Note.Trim().Replace("\n", "\n    ")}");
                if (File.Exists(s.ImagePath))
                {
                    zip.CreateEntryFromFile(s.ImagePath, imageName, CompressionLevel.NoCompression);
                    txt.AppendLine($"  → {imageName}");
                }
                txt.AppendLine();
                progress?.Report((i + 1) / (double)data.Steps.Count);
            }
            txt.AppendLine(Loc.T("Rep_CreatedWith"));

            var entry = zip.CreateEntry(textName, CompressionLevel.Optimal);
            using (var w = new StreamWriter(entry.Open(), new UTF8Encoding(true)))
                w.Write(txt.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"));

            int v = 1;
            foreach (var video in data.Videos)
            {
                if (!File.Exists(video)) continue;
                var name = data.Videos.Count == 1 ? "Video.avi" : $"Video_{v++}.avi";
                zip.CreateEntryFromFile(video, name, CompressionLevel.Fastest);
            }
        }
        File.Move(temp, path, overwrite: true);
    }
}
