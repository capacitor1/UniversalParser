using System.Buffers;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Ogg;

/// <summary>
/// Ogg CRC-32: polynomial 0x04C11DB7, initial value 0, no reflection and no final XOR
/// (RFC 3533 section 6). It is computed over the whole page with the checksum field zeroed.
/// </summary>
internal static class OggCrc
{
    private const uint Polynomial = 0x04C11DB7;
    private const int ChecksumOffset = 22;
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0u;
        foreach (var value in data)
        {
            crc = (crc << 8) ^ Table[((crc >> 24) ^ value) & 0xFF];
        }

        return crc;
    }

    public static bool TryComputePage(IRandomAccessSource source, in OggPageHeader header, out uint checksum)
    {
        checksum = 0;
        if (header.Truncated)
        {
            return false;
        }

        var length = header.End - header.Start;
        if (length <= 0 || length > int.MaxValue)
        {
            return false;
        }

        var buffer = ArrayPool<byte>.Shared.Rent((int)length);
        try
        {
            var span = buffer.AsSpan(0, (int)length);
            if (SourceRead.ReadAtMost(source, header.Start, span) != span.Length)
            {
                return false;
            }

            span[ChecksumOffset..(ChecksumOffset + 4)].Clear();
            checksum = Compute(span);
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (var index = 0; index < table.Length; index++)
        {
            var value = (uint)index << 24;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 0x80000000) != 0 ? (value << 1) ^ Polynomial : value << 1;
            }

            table[index] = value;
        }

        return table;
    }
}
