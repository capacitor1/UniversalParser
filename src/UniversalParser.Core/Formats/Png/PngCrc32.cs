using System.Buffers;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Png;

/// <summary>
/// CRC-32 (IEEE 802.3) used by every PNG chunk. The CRC covers the four-byte chunk type and the
/// chunk data, never the length field or the CRC itself. Hashing is streamed so a large IDAT chunk
/// is never loaded into memory.
/// </summary>
internal static class PngCrc32
{
    private const int BufferSize = 64 * 1024;

    private static readonly uint[] Table = CreateTable();

    public static uint Compute(
        IRandomAccessSource source,
        long offset,
        long length,
        CancellationToken cancellationToken,
        Action<long, long>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (offset > source.Length || length > source.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "CRC range exceeds the source length.");
        }

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var crc = uint.MaxValue;
            var position = offset;
            var remaining = length;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = (int)Math.Min(remaining, buffer.Length);
                var read = SourceRead.ReadAtMost(source, position, buffer.AsSpan(0, count));
                if (read <= 0)
                {
                    break;
                }

                crc = Append(crc, buffer.AsSpan(0, read));
                position += read;
                remaining -= read;
                progress?.Invoke(length - remaining, length);
            }

            return crc ^ uint.MaxValue;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static uint Append(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB8_8320 ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
