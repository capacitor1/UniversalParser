using System.Buffers.Binary;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Ogg;

internal enum OggPageReadError
{
    None,
    NotPage,
    TruncatedHeader,
    TruncatedPayload,
}

/// <summary>
/// One Ogg page header (RFC 3533 section 6). Ranges are clamped to the caller's limit, so a
/// truncated final page stays visible instead of being dropped.
/// </summary>
internal readonly record struct OggPageHeader(
    long Start,
    long End,
    long DeclaredEnd,
    long PayloadOffset,
    long PayloadLength,
    byte Version,
    byte HeaderType,
    ulong GranulePosition,
    uint SerialNumber,
    uint SequenceNumber,
    uint Checksum,
    byte PageSegments)
{
    public bool Truncated => End < DeclaredEnd;
}

internal static class OggPage
{
    public const int FixedHeaderLength = 27;
    public const int MaxSegments = 255;
    public const int CapturePatternLength = 4;

    public const byte ContinuedFlag = 0x01;
    public const byte BosFlag = 0x02;
    public const byte EosFlag = 0x04;

    public static ReadOnlySpan<byte> CapturePattern => "OggS"u8;

    public static bool TryRead(
        IRandomAccessSource source,
        long position,
        long limit,
        out OggPageHeader header,
        out OggPageReadError error)
    {
        ArgumentNullException.ThrowIfNull(source);
        header = default;
        error = OggPageReadError.NotPage;

        var available = Math.Min(limit, source.Length) - position;
        if (position < 0 || available < FixedHeaderLength)
        {
            error = OggPageReadError.TruncatedHeader;
            return false;
        }

        Span<byte> fixedBytes = stackalloc byte[FixedHeaderLength];
        if (SourceRead.ReadAtMost(source, position, fixedBytes) != FixedHeaderLength)
        {
            error = OggPageReadError.TruncatedHeader;
            return false;
        }

        if (!fixedBytes[..CapturePatternLength].SequenceEqual(CapturePattern))
        {
            error = OggPageReadError.NotPage;
            return false;
        }

        var segments = fixedBytes[26];
        var payloadOffset = position + FixedHeaderLength + segments;
        if (payloadOffset > limit)
        {
            error = OggPageReadError.TruncatedPayload;
            return false;
        }

        long payloadLength = 0;
        if (segments > 0)
        {
            Span<byte> table = stackalloc byte[MaxSegments];
            if (SourceRead.ReadAtMost(source, position + FixedHeaderLength, table[..segments]) != segments)
            {
                error = OggPageReadError.TruncatedPayload;
                return false;
            }

            foreach (var lacing in table[..segments])
            {
                payloadLength += lacing;
            }
        }

        var declaredEnd = payloadOffset + payloadLength;
        var end = Math.Min(declaredEnd, Math.Min(limit, source.Length));
        header = new OggPageHeader(
            position,
            end,
            declaredEnd,
            payloadOffset,
            payloadLength,
            fixedBytes[4],
            fixedBytes[5],
            BinaryPrimitives.ReadUInt64LittleEndian(fixedBytes[6..14]),
            BinaryPrimitives.ReadUInt32LittleEndian(fixedBytes[14..18]),
            BinaryPrimitives.ReadUInt32LittleEndian(fixedBytes[18..22]),
            BinaryPrimitives.ReadUInt32LittleEndian(fixedBytes[22..26]),
            segments);

        error = end < declaredEnd ? OggPageReadError.TruncatedPayload : OggPageReadError.None;
        return true;
    }

    public static int ReadSegmentTable(IRandomAccessSource source, OggPageHeader header, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        var count = Math.Min(header.PageSegments, destination.Length);
        if (count <= 0)
        {
            return 0;
        }

        var read = SourceRead.ReadAtMost(source, header.Start + FixedHeaderLength, destination[..count]);
        return read;
    }

    public static int ReadPayloadPrefix(IRandomAccessSource source, OggPageHeader header, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        var available = Math.Max(0, header.End - header.PayloadOffset);
        var count = (int)Math.Min(Math.Min(header.PayloadLength, available), destination.Length);
        return count <= 0 ? 0 : SourceRead.ReadAtMost(source, header.PayloadOffset, destination[..count]);
    }
}
