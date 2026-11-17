using System.Buffers.Binary;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.IsoBmff;

internal enum IsoBmffBoxHeaderError
{
    None,
    Truncated,
    InvalidSize,
    InvalidType,
}

/// <summary>
/// Parsed ISO BMFF box header. <see cref="HeaderSize"/> includes the 32-bit size, the type,
/// an optional 64-bit largesize and an optional 16-byte UUID user type. <see cref="TotalSize"/>
/// is always the encoded box size after size==0 / overrun clamping.
/// </summary>
internal readonly record struct IsoBmffBoxHeader(
    long Start,
    long HeaderSize,
    long TotalSize,
    string Type,
    string? UserType,
    uint Size32,
    ulong LargeSize,
    bool SizeToEnd,
    bool Truncated)
{
    public long PayloadOffset => Start + HeaderSize;
    public long End => Start + TotalSize;
    public long PayloadLength => Math.Max(0, TotalSize - HeaderSize);

    public static bool TryRead(
        IRandomAccessSource source,
        long offset,
        long limit,
        bool allowNumericType,
        out IsoBmffBoxHeader header,
        out IsoBmffBoxHeaderError error)
    {
        ArgumentNullException.ThrowIfNull(source);
        header = default;
        error = IsoBmffBoxHeaderError.None;

        if (limit - offset < 8)
        {
            error = IsoBmffBoxHeaderError.Truncated;
            return false;
        }

        Span<byte> fixedHeader = stackalloc byte[8];
        if (SourceRead.ReadAtMost(source, offset, fixedHeader) < fixedHeader.Length)
        {
            error = IsoBmffBoxHeaderError.Truncated;
            return false;
        }

        var size32 = BinaryPrimitives.ReadUInt32BigEndian(fixedHeader);
        var typeBytes = fixedHeader[4..8];
        string type;

        if (IsoBmffFourCc.IsValidType(typeBytes))
        {
            type = IsoBmffFourCc.Read(typeBytes);
        }
        else if (allowNumericType)
        {
            var keyIndex = BinaryPrimitives.ReadUInt32BigEndian(typeBytes);
            if (keyIndex == 0 || keyIndex > 0xFFFF)
            {
                error = IsoBmffBoxHeaderError.InvalidType;
                return false;
            }

            type = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"key[{keyIndex}]");
        }
        else
        {
            error = IsoBmffBoxHeaderError.InvalidType;
            return false;
        }

        long headerSize = 8;
        ulong largeSize = 0;
        var sizeToEnd = false;

        if (size32 == 1)
        {
            if (limit - offset < 16)
            {
                error = IsoBmffBoxHeaderError.Truncated;
                return false;
            }

            Span<byte> largeBytes = stackalloc byte[8];
            if (SourceRead.ReadAtMost(source, offset + 8, largeBytes) < largeBytes.Length)
            {
                error = IsoBmffBoxHeaderError.Truncated;
                return false;
            }

            largeSize = BinaryPrimitives.ReadUInt64BigEndian(largeBytes);
            headerSize = 16;
        }
        else if (size32 == 0)
        {
            sizeToEnd = true;
        }

        string? userType = null;
        if (type == "uuid")
        {
            if (limit - offset < headerSize + 16)
            {
                error = IsoBmffBoxHeaderError.Truncated;
                return false;
            }

            Span<byte> userTypeBytes = stackalloc byte[16];
            if (SourceRead.ReadAtMost(source, offset + headerSize, userTypeBytes) < userTypeBytes.Length)
            {
                error = IsoBmffBoxHeaderError.Truncated;
                return false;
            }

            userType = new Guid(userTypeBytes).ToString();
            headerSize += 16;
        }

        long total;
        if (sizeToEnd)
        {
            total = limit - offset;
        }
        else if (size32 == 1)
        {
            if (largeSize > long.MaxValue)
            {
                error = IsoBmffBoxHeaderError.InvalidSize;
                return false;
            }

            total = (long)largeSize;
        }
        else
        {
            total = size32;
        }

        if (total < headerSize)
        {
            error = IsoBmffBoxHeaderError.InvalidSize;
            return false;
        }

        var truncated = false;
        if (total > limit - offset)
        {
            total = limit - offset;
            truncated = true;
        }

        if (total < headerSize)
        {
            error = IsoBmffBoxHeaderError.InvalidSize;
            return false;
        }

        header = new IsoBmffBoxHeader(
            offset,
            headerSize,
            total,
            type,
            userType,
            size32,
            largeSize,
            sizeToEnd,
            truncated);
        return true;
    }
}
