using System.Buffers.Binary;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Png;

/// <summary>
/// Parsed chunk prefix. A chunk is complete only when its declared length, data, and four-byte CRC
/// all fit inside the parent range; truncated chunks keep the readable range instead of inventing
/// bytes.
/// </summary>
internal readonly record struct PngChunkHeader(
    long Offset,
    uint Length,
    string Type,
    long DataOffset,
    long CrcOffset,
    long End,
    bool IsComplete,
    bool IsLengthValid)
{
    public long TotalLength => PngConstants.ChunkOverhead + (long)Length;

    public static bool TryRead(
        IRandomAccessSource source,
        long offset,
        long limit,
        out PngChunkHeader header)
    {
        ArgumentNullException.ThrowIfNull(source);
        header = default;
        if (limit - offset < PngConstants.ChunkHeaderLength)
        {
            return false;
        }

        Span<byte> prefix = stackalloc byte[PngConstants.ChunkHeaderLength];
        if (SourceRead.ReadAtMost(source, offset, prefix) < prefix.Length)
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(prefix[..4]);
        var type = PngFormatting.DecodeChunkType(prefix[4..8]);
        var valid = length <= PngConstants.MaxChunkLength;
        var total = PngConstants.ChunkOverhead + (long)length;
        var available = limit - offset;
        var complete = valid && total <= available;
        var dataOffset = offset + PngConstants.ChunkHeaderLength;
        var crcOffset = length > long.MaxValue - dataOffset ? long.MaxValue : dataOffset + length;
        header = new PngChunkHeader(
            offset,
            length,
            type,
            dataOffset,
            crcOffset,
            complete ? offset + total : limit,
            complete,
            valid);
        return true;
    }
}
