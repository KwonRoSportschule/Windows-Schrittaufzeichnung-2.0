using System;
using System.Collections.Generic;
using System.Linq;
using StepRecorder.Models;

namespace StepRecorder.Export;

public sealed record ExportStep(int Number, string Description, string Note, DateTime Time, string Window, string ImagePath);

/// <summary>Immutable snapshot of a session, safe to use from a background thread.</summary>
public sealed record ExportData(string Title, DateTime Created, IReadOnlyList<ExportStep> Steps,
    IReadOnlyList<string> Videos, int JpegQuality)
{
    public static ExportData From(Session session, string fallbackTitle, int jpegQuality) => new(
        string.IsNullOrWhiteSpace(session.Title) ? fallbackTitle : session.Title.Trim(),
        session.Steps.Count > 0 ? session.Steps[0].Timestamp : session.Created,
        session.Steps.Select(s => new ExportStep(s.Number, s.Description, s.Note, s.Timestamp, s.WindowTitle, s.ImagePath)).ToList(),
        session.Videos.ToList(),
        jpegQuality);
}
