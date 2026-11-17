using System.Buffers.Binary;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Jpeg;

internal enum JpegMarkerError
{
    None,
    NotMarker,
    FillOnly,
    StuffedByte,
    MissingLength,
    InvalidLength,
    Truncated,
}

/// <summary>
/// A parsed JPEG marker prefix. <see cref="Start"/> points at the first 0xFF fill byte,
/// <see cref="CodeOffset"/> at the marker code, and <see cref="End"/> at the first byte after
/// the segment. A segment length includes its own two bytes but not the marker.
/// </summary>
internal readonly record struct JpegMarker(
    long Start,
    long CodeOffset,
    byte Code,
    ushort Length,
    long End,
    bool HasLength,
    bool IsComplete)
{
    public long LengthOffset => CodeOffset + 1;
    public long PayloadOffset => CodeOffset + 3;
    public long PayloadLength => HasLength ? Math.Max(0, Length - 2) : 0;
    public int FillBytes => (int)Math.Max(0, CodeOffset - Start - 1);

    public static bool TryRead(
        IRandomAccessSource source,
        long offset,
        long limit,
        out JpegMarker marker,
        out JpegMarkerError error)
    {
        ArgumentNullException.ThrowIfNull(source);
        marker = default;
        error = JpegMarkerError.None;
        if (offset >= limit)
        {
            error = JpegMarkerError.Truncated;
            return false;
        }

        if (ReadByte(source, offset) != JpegConstants.MarkerPrefix)
        {
            error = JpegMarkerError.NotMarker;
            return false;
        }

        var codeOffset = offset;
        while (codeOffset < limit && ReadByte(source, codeOffset) == JpegConstants.MarkerPrefix)
        {
            codeOffset++;
        }

        if (codeOffset >= limit)
        {
            error = JpegMarkerError.FillOnly;
            return false;
        }

        var code = ReadByte(source, codeOffset);
        if (code == 0x00)
        {
            error = JpegMarkerError.StuffedByte;
            return false;
        }

        if (JpegConstants.IsStandalone(code))
        {
            marker = new JpegMarker(offset, codeOffset, code, 0, codeOffset + 1, false, true);
            return true;
        }

        if (codeOffset + 3 > limit)
        {
            marker = new JpegMarker(offset, codeOffset, code, 0, limit, true, false);
            error = JpegMarkerError.MissingLength;
            return false;
        }

        Span<byte> lengthBytes = stackalloc byte[2];
        if (SourceRead.ReadAtMost(source, codeOffset + 1, lengthBytes) < lengthBytes.Length)
        {
            marker = new JpegMarker(offset, codeOffset, code, 0, limit, true, false);
            error = JpegMarkerError.MissingLength;
            return false;
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);
        if (length < 2)
        {
            marker = new JpegMarker(offset, codeOffset, code, length, limit, true, false);
            error = JpegMarkerError.InvalidLength;
            return false;
        }

        var end = codeOffset + 1 + length;
        if (end > limit)
        {
            marker = new JpegMarker(offset, codeOffset, code, length, limit, true, false);
            error = JpegMarkerError.Truncated;
            return false;
        }

        marker = new JpegMarker(offset, codeOffset, code, length, end, true, true);
        return true;
    }

    private static byte ReadByte(IRandomAccessSource source, long offset)
    {
        Span<byte> value = stackalloc byte[1];
        return SourceRead.ReadAtMost(source, offset, value) == 1 ? value[0] : (byte)0;
    }
}
