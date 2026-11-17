using System.Numerics;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Ebml;

internal enum EbmlHeaderFailure
{
    None,
    Truncated,
    InvalidVint,
    LengthOverflow,
}

/// <summary>Decoded EBML element header. A null data size means the unknown-size sentinel.</summary>
internal readonly record struct EbmlElementHeader(
    long Offset,
    ulong Id,
    int IdLength,
    int SizeLength,
    long? DataSize)
{
    public int HeaderLength => IdLength + SizeLength;
    public long PayloadOffset => Offset + HeaderLength;
    public bool IsUnknownSize => DataSize is null;
    public long? PayloadEnd => DataSize is { } size ? PayloadOffset + size : null;
}

/// <summary>
/// Reads EBML variable-length integers and element headers from a bounded source region.
/// No call reads more than the 12-byte maximum element header.
/// </summary>
internal static class EbmlRead
{
    public const int MaxHeaderLength = 12;

    public static bool TryReadHeader(
        IRandomAccessSource source,
        long offset,
        long limit,
        out EbmlElementHeader header,
        out EbmlHeaderFailure failure)
    {
        header = default;
        failure = EbmlHeaderFailure.None;

        var available = Math.Min(source.Length, limit) - offset;
        if (available < 2)
        {
            failure = EbmlHeaderFailure.Truncated;
            return false;
        }

        Span<byte> buffer = stackalloc byte[MaxHeaderLength];
        var read = SourceRead.ReadAtMost(source, offset, buffer[..(int)Math.Min(MaxHeaderLength, available)]);
        if (read < 1)
        {
            failure = EbmlHeaderFailure.Truncated;
            return false;
        }

        var idLength = VintLength(buffer[0]);
        if (idLength is < 1 or > 4)
        {
            failure = EbmlHeaderFailure.InvalidVint;
            return false;
        }

        if (read < idLength + 1)
        {
            failure = EbmlHeaderFailure.Truncated;
            return false;
        }

        var sizeLength = VintLength(buffer[idLength]);
        if (sizeLength is < 1 or > 8)
        {
            failure = EbmlHeaderFailure.InvalidVint;
            return false;
        }

        var headerLength = idLength + sizeLength;
        if (read < headerLength)
        {
            failure = EbmlHeaderFailure.Truncated;
            return false;
        }

        ulong id = 0;
        for (var index = 0; index < idLength; index++)
        {
            id = (id << 8) | buffer[index];
        }

        var dataBits = 8 - sizeLength;
        var mask = dataBits == 0 ? 0UL : (1UL << dataBits) - 1;
        var value = (ulong)(buffer[idLength] & mask);
        var allOnes = dataBits == 0 || (buffer[idLength] & mask) == mask;
        for (var index = 1; index < sizeLength; index++)
        {
            var next = buffer[idLength + index];
            value = (value << 8) | next;
            allOnes &= next == 0xFF;
        }

        if (offset > long.MaxValue - headerLength)
        {
            failure = EbmlHeaderFailure.LengthOverflow;
            return false;
        }

        long? dataSize = null;
        if (!allOnes)
        {
            var payloadOffset = offset + headerLength;
            if (value > (ulong)(long.MaxValue - payloadOffset))
            {
                failure = EbmlHeaderFailure.LengthOverflow;
                return false;
            }

            dataSize = (long)value;
        }

        header = new EbmlElementHeader(offset, id, idLength, sizeLength, dataSize);
        return true;
    }

    /// <summary>VINT length from the first byte; 0 means an invalid leading byte.</summary>
    public static int VintLength(byte first) => first == 0 ? 0 : BitOperations.LeadingZeroCount((uint)first) - 23;
}
