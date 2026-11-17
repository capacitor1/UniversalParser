using System.Buffers.Binary;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Png;

/// <summary>
/// The IHDR values needed to interpret colour-dependent ancillary chunks. Reading it is bounded to
/// the first 29 bytes and never depends on a previously parsed tree.
/// </summary>
internal readonly record struct PngImageHeader(
    uint Width,
    uint Height,
    byte BitDepth,
    byte ColorType,
    byte CompressionMethod,
    byte FilterMethod,
    byte InterlaceMethod)
{
    public static bool TryRead(IRandomAccessSource source, out PngImageHeader header)
    {
        ArgumentNullException.ThrowIfNull(source);
        header = default;
        const int required = PngConstants.SignatureLength + PngConstants.ChunkHeaderLength + 13;
        if (source.Length < required)
        {
            return false;
        }

        Span<byte> signature = stackalloc byte[PngConstants.SignatureLength];
        Span<byte> chunk = stackalloc byte[PngConstants.ChunkHeaderLength];
        Span<byte> data = stackalloc byte[13];
        if (SourceRead.ReadAtMost(source, 0, signature) < signature.Length
            || !signature.SequenceEqual(PngConstants.Signature)
            || SourceRead.ReadAtMost(source, PngConstants.SignatureLength, chunk) < chunk.Length)
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(chunk[..4]) != 13
            || !chunk[4..8].SequenceEqual("IHDR"u8))
        {
            return false;
        }

        if (SourceRead.ReadAtMost(source, PngConstants.SignatureLength + PngConstants.ChunkHeaderLength, data)
            < data.Length)
        {
            return false;
        }

        header = new PngImageHeader(
            BinaryPrimitives.ReadUInt32BigEndian(data[..4]),
            BinaryPrimitives.ReadUInt32BigEndian(data[4..8]),
            data[8],
            data[9],
            data[10],
            data[11],
            data[12]);
        return true;
    }
}
