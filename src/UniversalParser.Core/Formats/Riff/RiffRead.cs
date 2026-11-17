using System.Buffers.Binary;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Riff;

/// <summary>Bounded, allocation-free helpers shared by the RIFF document and node parsers.</summary>
internal static class RiffRead
{
    public static bool TryReadChunkHeader(
        IRandomAccessSource source,
        long position,
        long limit,
        Endian endian,
        out string id,
        out uint rawSize)
    {
        id = string.Empty;
        rawSize = 0;

        var available = Math.Min(source.Length, limit) - position;
        if (available < 8)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[8];
        if (ReadAtMost(source, position, header) != header.Length)
        {
            return false;
        }

        id = RiffFormatting.DecodeFourCc(header[..4]);
        rawSize = endian == Endian.Little
            ? BinaryPrimitives.ReadUInt32LittleEndian(header[4..])
            : BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        return true;
    }

    public static int ReadAtMost(IRandomAccessSource source, long offset, Span<byte> buffer) =>
        SourceRead.ReadAtMost(source, offset, buffer);

    public static ushort UInt16(ReadOnlySpan<byte> data, int offset, Endian endian) =>
        endian == Endian.Little
            ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));

    public static uint UInt24(ReadOnlySpan<byte> data, int offset, Endian endian)
    {
        var bytes = data.Slice(offset, 3);
        return endian == Endian.Little
            ? (uint)(bytes[0] | bytes[1] << 8 | bytes[2] << 16)
            : (uint)(bytes[0] << 16 | bytes[1] << 8 | bytes[2]);
    }

    public static uint UInt32(ReadOnlySpan<byte> data, int offset, Endian endian) =>
        endian == Endian.Little
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

    public static ulong UInt64(ReadOnlySpan<byte> data, int offset, Endian endian) =>
        endian == Endian.Little
            ? BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
            : BinaryPrimitives.ReadUInt64BigEndian(data.Slice(offset, 8));
}
