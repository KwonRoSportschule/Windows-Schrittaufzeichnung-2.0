using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace StepRecorder.Export;

public enum PdfFont { Regular, Bold, Italic }

public sealed class PdfImage
{
    public int ObjectId;
    public string Name = "";
    public byte[] Jpeg = Array.Empty<byte>();
    public int Width, Height;
}

public sealed class PdfPage
{
    public readonly StringBuilder Content = new();
    public readonly List<PdfImage> Images = new();

    private static string N(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public void Text(float x, float y, PdfFont font, float size, string text, (float R, float G, float B) color)
    {
        var f = font switch { PdfFont.Bold => "F2", PdfFont.Italic => "F3", _ => "F1" };
        Content.Append($"{N(color.R)} {N(color.G)} {N(color.B)} rg BT /{f} {N(size)} Tf {N(x)} {N(y)} Td (")
               .Append(PdfDocument.Escape(text)).Append(") Tj ET\n");
    }

    public void Rect(float x, float y, float w, float h, (float R, float G, float B) color) =>
        Content.Append($"{N(color.R)} {N(color.G)} {N(color.B)} rg {N(x)} {N(y)} {N(w)} {N(h)} re f\n");

    public void Image(PdfImage img, float x, float y, float w, float h)
    {
        if (!Images.Contains(img)) Images.Add(img);
        Content.Append($"q {N(w)} 0 0 {N(h)} {N(x)} {N(y)} cm /{img.Name} Do Q\n");
    }
}

public sealed class PdfDocument
{
    public List<PdfPage> Pages { get; } = new();
    private readonly List<PdfImage> _images = new();

    public PdfPage AddPage()
    {
        var p = new PdfPage();
        Pages.Add(p);
        return p;
    }

    public PdfImage AddImage(byte[] jpeg, int width, int height)
    {
        var img = new PdfImage { Jpeg = jpeg, Width = width, Height = height, Name = "Im" + (_images.Count + 1) };
        _images.Add(img);
        return img;
    }

    /// <summary>Unicode → Windows-1252 bytes (what /WinAnsiEncoding expects). Unknown chars become '?'.</summary>
    public static byte[] Encode(string s)
    {
        var bytes = new byte[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bytes[i] = c switch
            {
                < (char)0x80 => (byte)c,
                >= (char)0xA0 and <= (char)0xFF => (byte)c,
                '€' => 0x80, '‚' => 0x82, '„' => 0x84, '…' => 0x85, '‘' => 0x91, '’' => 0x92,
                '“' => 0x93, '”' => 0x94, '•' => 0x95, '–' => 0x96, '—' => 0x97, '™' => 0x99,
                '←' or '↑' or '→' or '↓' => (byte)'?',
                _ => (byte)'?'
            };
        }
        return bytes;
    }

    public static string Escape(string text)
    {
        // Arrows are not in WinAnsi; spell them out instead of printing '?'.
        text = text.Replace("←", "<-").Replace("→", "->").Replace("↑", "^").Replace("↓", "v");
        var sb = new StringBuilder(text.Length + 8);
        foreach (var b in Encode(text))
        {
            if (b == '(' || b == ')' || b == '\\') sb.Append('\\').Append((char)b);
            else if (b < 32 || b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else sb.Append((char)b);
        }
        return sb.ToString();
    }

    public void Write(Stream output, string title)
    {
        var offsets = new List<long>();
        var latin = Encoding.Latin1;
        void Raw(string s) { var b = latin.GetBytes(s); output.Write(b, 0, b.Length); }
        void BeginObj(int id) { while (offsets.Count < id) offsets.Add(0); offsets[id - 1] = output.Position; Raw($"{id} 0 obj\n"); }

        // Object numbering: 1 catalog, 2 pages, 3-5 fonts, 6 info, then images, then page+content pairs.
        int nextId = 7;
        foreach (var img in _images) img.ObjectId = nextId++;
        var pageIds = new List<(int Page, int Content)>();
        foreach (var _ in Pages) pageIds.Add((nextId++, nextId++));

        Raw("%PDF-1.4\n%âãÏÓ\n");

        BeginObj(1); Raw("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        BeginObj(2);
        Raw("<< /Type /Pages /Kids [");
        foreach (var (p, _) in pageIds) Raw($"{p} 0 R ");
        Raw($"] /Count {Pages.Count} >>\nendobj\n");
        string[] fonts = { "Helvetica", "Helvetica-Bold", "Helvetica-Oblique" };
        for (int i = 0; i < 3; i++)
        {
            BeginObj(3 + i);
            Raw($"<< /Type /Font /Subtype /Type1 /BaseFont /{fonts[i]} /Encoding /WinAnsiEncoding >>\nendobj\n");
        }
        BeginObj(6);
        Raw($"<< /Title ({Escape(title)}) /Producer (StepRecorder 2.0) /CreationDate (D:{DateTime.Now:yyyyMMddHHmmss}) >>\nendobj\n");

        foreach (var img in _images)
        {
            BeginObj(img.ObjectId);
            Raw($"<< /Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height} /ColorSpace /DeviceRGB " +
                $"/BitsPerComponent 8 /Filter /DCTDecode /Length {img.Jpeg.Length} >>\nstream\n");
            output.Write(img.Jpeg, 0, img.Jpeg.Length);
            Raw("\nendstream\nendobj\n");
        }

        for (int i = 0; i < Pages.Count; i++)
        {
            var page = Pages[i];
            var (pageId, contentId) = pageIds[i];
            BeginObj(pageId);
            var xobjects = new StringBuilder();
            foreach (var img in page.Images) xobjects.Append($"/{img.Name} {img.ObjectId} 0 R ");
            Raw($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.89] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> /XObject << {xobjects}>> >> " +
                $"/Contents {contentId} 0 R >>\nendobj\n");

            var raw = latin.GetBytes(page.Content.ToString());
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw, 0, raw.Length);
            BeginObj(contentId);
            Raw($"<< /Length {ms.Length} /Filter /FlateDecode >>\nstream\n");
            ms.Position = 0;
            ms.CopyTo(output);
            Raw("\nendstream\nendobj\n");
        }

        long xref = output.Position;
        Raw($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Raw($"{o:D10} 00000 n \n");
        Raw($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 6 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }
}
