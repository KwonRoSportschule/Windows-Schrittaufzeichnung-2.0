using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StepRecorder.Core;

/// <summary>Minimal AVI 1.0 (RIFF) writer for a single MJPEG video stream with idx1 index.</summary>
public sealed class MjpegAviWriter : IDisposable
{
    private readonly FileStream _fs;
    private readonly BinaryWriter _w;
    private readonly int _fps;
    private readonly int _width, _height;
    private readonly List<(long Offset, int Size)> _index = new();
    private long _riffSizePos, _totalFramesPos, _lengthPos, _moviSizePos, _moviStart;
    private long _avihBufferPos, _strhBufferPos;
    private int _maxFrame;
    private bool _closed;

    public MjpegAviWriter(string path, int width, int height, int fps)
    {
        _width = width; _height = height; _fps = fps;
        _fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1 << 16);
        _w = new BinaryWriter(_fs, Encoding.ASCII, leaveOpen: true);
        WriteHeaders();
    }

    public long Length => _fs.Length;

    private void FourCC(string s) => _w.Write(Encoding.ASCII.GetBytes(s));

    private void WriteHeaders()
    {
        FourCC("RIFF"); _riffSizePos = _fs.Position; _w.Write(0); FourCC("AVI ");

        FourCC("LIST"); long hdrlSizePos = _fs.Position; _w.Write(0); FourCC("hdrl");

        // MainAVIHeader
        FourCC("avih"); _w.Write(56);
        _w.Write(1_000_000 / _fps);       // dwMicroSecPerFrame
        _w.Write(0);                      // dwMaxBytesPerSec
        _w.Write(0);                      // dwPaddingGranularity
        _w.Write(0x10);                   // dwFlags = AVIF_HASINDEX
        _totalFramesPos = _fs.Position; _w.Write(0); // dwTotalFrames
        _w.Write(0);                      // dwInitialFrames
        _w.Write(1);                      // dwStreams
        _avihBufferPos = _fs.Position; _w.Write(0); // dwSuggestedBufferSize
        _w.Write(_width); _w.Write(_height);
        _w.Write(0); _w.Write(0); _w.Write(0); _w.Write(0); // dwReserved[4]

        FourCC("LIST"); long strlSizePos = _fs.Position; _w.Write(0); FourCC("strl");

        // AVIStreamHeader
        FourCC("strh"); _w.Write(56);
        FourCC("vids"); FourCC("MJPG");
        _w.Write(0);                      // dwFlags
        _w.Write((short)0); _w.Write((short)0); // wPriority, wLanguage
        _w.Write(0);                      // dwInitialFrames
        _w.Write(1);                      // dwScale
        _w.Write(_fps);                   // dwRate
        _w.Write(0);                      // dwStart
        _lengthPos = _fs.Position; _w.Write(0); // dwLength
        _strhBufferPos = _fs.Position; _w.Write(0); // dwSuggestedBufferSize
        _w.Write(-1);                     // dwQuality
        _w.Write(0);                      // dwSampleSize
        _w.Write((short)0); _w.Write((short)0); _w.Write((short)_width); _w.Write((short)_height); // rcFrame

        // BITMAPINFOHEADER
        FourCC("strf"); _w.Write(40);
        _w.Write(40); _w.Write(_width); _w.Write(_height);
        _w.Write((short)1); _w.Write((short)24);
        FourCC("MJPG");
        _w.Write(_width * _height * 3);
        _w.Write(0); _w.Write(0); _w.Write(0); _w.Write(0);

        PatchSize(strlSizePos);
        PatchSize(hdrlSizePos);

        FourCC("LIST"); _moviSizePos = _fs.Position; _w.Write(0);
        _moviStart = _fs.Position; FourCC("movi");
    }

    public void WriteFrame(byte[] jpeg)
    {
        long offset = _fs.Position - _moviStart;
        FourCC("00dc");
        _w.Write(jpeg.Length);
        _w.Write(jpeg);
        if ((jpeg.Length & 1) == 1) _w.Write((byte)0);
        _index.Add((offset, jpeg.Length));
        _maxFrame = Math.Max(_maxFrame, jpeg.Length);
    }

    private void PatchSize(long sizePos)
    {
        long end = _fs.Position;
        _fs.Position = sizePos;
        _w.Write((int)(end - sizePos - 4));
        _fs.Position = end;
    }

    private void Patch(long pos, int value)
    {
        long end = _fs.Position;
        _fs.Position = pos;
        _w.Write(value);
        _fs.Position = end;
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        PatchSize(_moviSizePos);

        FourCC("idx1"); _w.Write(_index.Count * 16);
        foreach (var (offset, size) in _index)
        {
            FourCC("00dc");
            _w.Write(0x10); // AVIIF_KEYFRAME - every MJPEG frame is a keyframe
            _w.Write((int)offset);
            _w.Write(size);
        }

        Patch(_totalFramesPos, _index.Count);
        Patch(_lengthPos, _index.Count);
        Patch(_avihBufferPos, _maxFrame + 8);
        Patch(_strhBufferPos, _maxFrame + 8);
        PatchSize(_riffSizePos);
        _w.Flush();
        _w.Dispose();
        _fs.Dispose();
    }
}
