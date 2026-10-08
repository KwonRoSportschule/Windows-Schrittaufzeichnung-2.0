using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using StepRecorder.Models;

namespace StepRecorder.Services;

/// <summary>
/// Working data lives in %TEMP%\StepRecorder2\&lt;session&gt; and is deleted on exit.
/// A project (*.steps) is a plain ZIP: project.json + img/*.png (+ video/*.avi).
/// </summary>
public static class ProjectStore
{
    private sealed class ProjectFile
    {
        public int Version { get; set; } = 1;
        public string Title { get; set; } = "";
        public DateTime Created { get; set; }
        public List<Step> Steps { get; set; } = new();
        public List<string> Videos { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string TempRoot => Path.Combine(Path.GetTempPath(), "StepRecorder2");

    public static Session CreateSession()
    {
        var dir = Path.Combine(TempRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
        Directory.CreateDirectory(Path.Combine(dir, "img"));
        return new Session(dir) { IsDirty = false };
    }

    public static void DeleteSession(Session? session)
    {
        if (session == null) return;
        try { Directory.Delete(session.Directory, true); } catch { /* file still open - cleaned up next start */ }
    }

    /// <summary>Removes leftovers of crashed sessions older than two days.</summary>
    public static void CleanupOldSessions()
    {
        try
        {
            if (!Directory.Exists(TempRoot)) return;
            foreach (var dir in Directory.GetDirectories(TempRoot))
            {
                if (Directory.GetLastWriteTime(dir) < DateTime.Now.AddDays(-2))
                    try { Directory.Delete(dir, true); } catch { }
            }
        }
        catch { }
    }

    public static void Save(Session session, string path)
    {
        var project = new ProjectFile
        {
            Title = session.Title,
            Created = session.Created,
            Steps = session.Steps.ToList(),
            Videos = session.Videos.Where(File.Exists).Select(v => "video/" + Path.GetFileName(v)).ToList()
        };

        var temp = path + ".tmp";
        using (var fs = File.Create(temp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("project.json", CompressionLevel.Optimal);
            using (var s = entry.Open()) JsonSerializer.Serialize(s, project, Json);

            foreach (var step in session.Steps)
            {
                // PNGs are already compressed - storing them is much faster and barely bigger.
                if (File.Exists(step.ImagePath)) zip.CreateEntryFromFile(step.ImagePath, step.ImageFile, CompressionLevel.NoCompression);
                if (File.Exists(step.ThumbPath)) zip.CreateEntryFromFile(step.ThumbPath, step.ThumbFile, CompressionLevel.NoCompression);
            }
            foreach (var video in session.Videos.Where(File.Exists))
                zip.CreateEntryFromFile(video, "video/" + Path.GetFileName(video), CompressionLevel.NoCompression);
        }
        File.Move(temp, path, overwrite: true);
        session.FilePath = path;
        session.IsDirty = false;
    }

    public static Session Load(string path)
    {
        var session = CreateSession();
        try
        {
            ZipFile.ExtractToDirectory(path, session.Directory, overwriteFiles: true);
            var project = JsonSerializer.Deserialize<ProjectFile>(
                File.ReadAllText(Path.Combine(session.Directory, "project.json")), Json) ?? new ProjectFile();

            var root = Path.GetFullPath(session.Directory);
            foreach (var step in project.Steps)
            {
                step.ImagePath = SafePath(root, step.ImageFile);
                step.ThumbPath = SafePath(root, step.ThumbFile);
                session.Steps.Add(step);
            }
            foreach (var v in project.Videos)
            {
                var full = SafePath(root, v);
                if (File.Exists(full)) session.Videos.Add(full);
            }
            session.Title = project.Title;
            session.Created = project.Created == default ? File.GetCreationTime(path) : project.Created;
            session.FilePath = path;
            session.IsDirty = false;
            return session;
        }
        catch
        {
            DeleteSession(session);
            throw;
        }
    }

    /// <summary>Resolves a relative path from a project file and refuses anything outside the session folder.</summary>
    private static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return "";
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : "";
    }
}
