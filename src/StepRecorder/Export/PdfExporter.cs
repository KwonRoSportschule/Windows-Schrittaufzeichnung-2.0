using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using StepRecorder.Core;
using StepRecorder.Services;

namespace StepRecorder.Export;

/// <summary>
/// PDF report without any third-party library: a small PDF 1.4 writer using the standard
/// Helvetica fonts (WinAnsi encoding, so German umlauts work) and JPEG (DCTDecode) images.
/// </summary>
public static class PdfExporter
{
    private const float PageW = 595.28f, PageH = 841.89f, Margin = 42f;
    private const float ContentW = PageW - 2 * Margin;
    private const float MaxImageH = 330f;
    private const float FooterY = 26f;

    private static readonly (float R, float G, float B) Accent = (0f, 0.404f, 0.753f);
    private static readonly (float R, float G, float B) Gray = (0.42f, 0.42f, 0.42f);
    private static readonly (float R, float G, float B) Dark = (0.1f, 0.1f, 0.1f);

    public static void Export(ExportData data, string path, IProgress<double>? progress = null)
    {
        var doc = new PdfDocument();
        var page = doc.AddPage();
        float y = PageH - Margin;

        // Title block
        foreach (var line in Wrap(data.Title, PdfFont.Bold, 22, ContentW))
        {
            page.Text(Margin, y - 22, PdfFont.Bold, 22, line, Dark);
            y -= 28;
        }
        var meta = $"{Loc.F("Rep_RecordedOn", data.Created.ToString("f", Loc.Instance.Culture))}  ·  {Loc.F("Rep_Steps", data.Steps.Count)}";
        page.Text(Margin, y - 12, PdfFont.Regular, 10, meta, Gray);
        y -= 22;
        page.Rect(Margin, y, ContentW, 2, Accent);
        y -= 22;

        for (int i = 0; i < data.Steps.Count; i++)
        {
            var step = data.Steps[i];
            var descLines = Wrap(step.Description, PdfFont.Regular, 10.5f, ContentW);
            var noteLines = string.IsNullOrWhiteSpace(step.Note)
                ? new List<string>()
                : Wrap(step.Note.Trim(), PdfFont.Italic, 10f, ContentW - 20);

            PdfImage? image = null;
            float imgW = 0, imgH = 0;
            if (File.Exists(step.ImagePath))
            {
                var (jpeg, w, h) = ScreenCapture.LoadAsJpeg(step.ImagePath, data.JpegQuality, 2400);
                image = doc.AddImage(jpeg, w, h);
                float scale = Math.Min(ContentW / w, MaxImageH / h);
                imgW = w * scale; imgH = h * scale;
            }

            float blockH = 20 + descLines.Count * 14 + (noteLines.Count > 0 ? noteLines.Count * 13 + 16 : 0)
                           + (image != null ? imgH + 10 : 0) + 22;
            if (y - blockH < Margin + FooterY && y < PageH - Margin - 1)
            {
                page = doc.AddPage();
                y = PageH - Margin;
            }

            // Heading: "Schritt 3" + time / window on the right
            var heading = Loc.F("Step_N", step.Number);
            page.Text(Margin, y - 13, PdfFont.Bold, 13, heading, Accent);
            var right = step.Time.ToString("T", Loc.Instance.Culture);
            if (!string.IsNullOrWhiteSpace(step.Window))
                right = Truncate(step.Window, PdfFont.Regular, 9, ContentW * 0.55f) + "  ·  " + right;
            page.Text(Margin + ContentW - Measure(right, PdfFont.Regular, 9), y - 12, PdfFont.Regular, 9, right, Gray);
            y -= 20;

            foreach (var line in descLines)
            {
                page.Text(Margin, y - 10.5f, PdfFont.Regular, 10.5f, line, Dark);
                y -= 14;
            }

            if (noteLines.Count > 0)
            {
                y -= 4;
                float boxH = noteLines.Count * 13 + 8;
                page.Rect(Margin, y - boxH, ContentW, boxH, (1f, 0.973f, 0.882f));
                page.Rect(Margin, y - boxH, 3, boxH, (0.95f, 0.69f, 0.09f));
                float ny = y - 4;
                foreach (var line in noteLines)
                {
                    page.Text(Margin + 12, ny - 10, PdfFont.Italic, 10, line, Dark);
                    ny -= 13;
                }
                y -= boxH + 4;
            }

            if (image != null)
            {
                y -= 6;
                page.Rect(Margin - 0.5f, y - imgH - 0.5f, imgW + 1, imgH + 1, (0.8f, 0.8f, 0.8f));
                page.Image(image, Margin, y - imgH, imgW, imgH);
                y -= imgH + 4;
            }
            y -= 22;
            progress?.Report((i + 1) / (double)data.Steps.Count);
        }

        // Footer on every page (total page count is known only now)
        for (int p = 0; p < doc.Pages.Count; p++)
        {
            var pg = doc.Pages[p];
            pg.Rect(Margin, FooterY + 12, ContentW, 0.5f, (0.85f, 0.85f, 0.85f));
            pg.Text(Margin, FooterY, PdfFont.Regular, 8, Truncate(data.Title, PdfFont.Regular, 8, ContentW * 0.5f) + "  ·  " + Loc.T("Rep_CreatedWith"), Gray);
            var num = Loc.F("Rep_Page", p + 1, doc.Pages.Count);
            pg.Text(Margin + ContentW - Measure(num, PdfFont.Regular, 8), FooterY, PdfFont.Regular, 8, num, Gray);
        }

        using var fs = File.Create(path);
        doc.Write(fs, data.Title);
    }

    // ------------------------------------------------------------ text layout

    private static string Truncate(string text, PdfFont font, float size, float max)
    {
        if (Measure(text, font, size) <= max) return text;
        while (text.Length > 1 && Measure(text + "…", font, size) > max) text = text[..^1];
        return text + "…";
    }

    public static List<string> Wrap(string text, PdfFont font, float size, float max)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var current = new StringBuilder();
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (Measure(candidate, font, size) <= max) { current.Clear().Append(candidate); continue; }
                if (current.Length > 0) { lines.Add(current.ToString()); current.Clear(); }
                // Hard-break words that are longer than a line.
                var w = word;
                while (Measure(w, font, size) > max && w.Length > 1)
                {
                    int n = w.Length;
                    while (n > 1 && Measure(w[..n], font, size) > max) n--;
                    lines.Add(w[..n]);
                    w = w[n..];
                }
                current.Append(w);
            }
            lines.Add(current.ToString());
        }
        return lines;
    }

    public static float Measure(string text, PdfFont font, float size)
    {
        float units = 0;
        foreach (var b in PdfDocument.Encode(text)) units += HelveticaWidth(b);
        if (font == PdfFont.Bold) units *= 1.07f;
        return units * size / 1000f;
    }

    // Helvetica AFM widths for WinAnsi 32..126; Latin-1 letters approximated by their base letter.
    private static readonly short[] Widths =
    {
        278,278,355,556,556,889,667,191,333,333,389,584,278,333,278,278, // space ! " # $ % & ' ( ) * + , - . /
        556,556,556,556,556,556,556,556,556,556,278,278,584,584,584,556, // 0-9 : ; < = > ?
        1015,667,667,722,722,667,611,778,722,278,500,667,556,833,722,778, // @ A-O
        667,778,722,667,611,722,667,944,667,667,611,278,278,278,469,556, // P-Z [ \ ] ^ _
        333,556,556,500,556,556,278,556,556,222,222,500,222,833,556,556, // ` a-o
        556,556,333,500,278,556,500,722,500,500,500,334,260,334,584       // p-z { | } ~
    };

    private static float HelveticaWidth(byte b)
    {
        if (b >= 32 && b <= 126) return Widths[b - 32];
        return b switch
        {
            0xC4 => 667, 0xD6 => 778, 0xDC => 722, 0xDF => 611, // Ä Ö Ü ß
            0x84 or 0x93 or 0x94 or 0x91 or 0x92 => 333,         // quotes
            0x85 or 0x97 => 1000,                                // … —
            0x96 => 556, 0x95 => 350, 0x80 => 556,               // – • €
            _ => 556
        };
    }
}
