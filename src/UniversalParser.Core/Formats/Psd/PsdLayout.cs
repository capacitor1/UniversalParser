using System.Buffers.Binary;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Formats.Psd;

internal readonly record struct PsdHeader(
    ushort Version,
    ushort Channels,
    uint Height,
    uint Width,
    ushort Depth,
    ushort ColorMode);

internal readonly record struct PsdChannelInfo(short Id, ulong DataLength);

/// <summary>
/// Boundaries of one layer record. <see cref="FixedEnd"/> is the end of the extra-data length
/// field; everything after it belongs to the extra data field.
/// </summary>
internal readonly record struct PsdLayerRecordLayout(
    long Start,
    long End,
    long ChannelsStart,
    long ChannelsEnd,
    long FixedEnd,
    long ExtraEnd,
    long MaskStart,
    long MaskEnd,
    long BlendingStart,
    long BlendingEnd,
    long NameStart,
    long NameEnd,
    long AdditionalStart,
    long AdditionalEnd,
    int Top,
    int Left,
    int Bottom,
    int Right,
    ushort ChannelCount,
    bool Complete,
    string? Error);

internal readonly record struct PsdAdditionalInfoBlock(
    long Start,
    long End,
    long SignatureOffset,
    long KeyOffset,
    long LengthFieldOffset,
    int LengthFieldSize,
    long DataOffset,
    long DataLength,
    string Signature,
    string Key,
    bool Complete,
    string? Error);

/// <summary>
/// Header, section, layer-record and tagged-block scanners. They only read fixed-size headers so a
/// multi-gigabyte PSD is never loaded; the tree stores ranges and the node parser re-reads them.
/// </summary>
internal static class PsdLayout
{
    public static bool TryReadHeader(
        IRandomAccessSource source,
        out PsdHeader header,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(source);
        header = default;
        error = string.Empty;

        if (source.Length < PsdConstants.HeaderLength)
        {
            error = $"The file is shorter than the {PsdConstants.HeaderLength}-byte PSD header.";
            return false;
        }

        Span<byte> bytes = stackalloc byte[PsdConstants.HeaderLength];
        if (SourceRead.ReadAtMost(source, 0, bytes) != bytes.Length)
        {
            error = "The PSD header could not be read.";
            return false;
        }

        if (!bytes[..PsdConstants.SignatureLength].SequenceEqual(PsdConstants.Signature))
        {
            error = $"Expected '8BPS' but found {RawIdentifier.FormatBytes(bytes[..PsdConstants.SignatureLength])}.";
            return false;
        }

        var version = BinaryPrimitives.ReadUInt16BigEndian(bytes[4..]);
        if (version is not (PsdConstants.VersionPsd or PsdConstants.VersionPsb))
        {
            error = string.Create(CultureInfo.InvariantCulture, $"Unsupported PSD version {version}.");
            return false;
        }

        header = new PsdHeader(
            version,
            BinaryPrimitives.ReadUInt16BigEndian(bytes[12..]),
            BinaryPrimitives.ReadUInt32BigEndian(bytes[14..]),
            BinaryPrimitives.ReadUInt32BigEndian(bytes[18..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[22..]),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[24..]));
        return true;
    }

    /// <summary>
    /// Reads a section length field at <paramref name="position"/>. The returned
    /// <paramref name="dataStart"/> is the first byte after the field, and
    /// <paramref name="end"/> is clamped to <paramref name="limit"/>.
    /// </summary>
    public static bool TryReadSection(
        IRandomAccessSource source,
        long position,
        long limit,
        bool psb,
        out long length,
        out long dataStart,
        out long end,
        out int fieldSize,
        out bool complete,
        out string error)
    {
        length = 0;
        dataStart = position;
        end = position;
        fieldSize = PsdConstants.LengthFieldSize(psb);
        complete = false;
        error = string.Empty;

        if (position < 0 || limit > source.Length || limit < position)
        {
            error = "The PSD section limit is outside the source.";
            return false;
        }

        if (limit - position < fieldSize)
        {
            error = "The PSD section length field is truncated.";
            return false;
        }

        Span<byte> bytes = stackalloc byte[8];
        var buffer = bytes[..fieldSize];
        if (SourceRead.ReadAtMost(source, position, buffer) != fieldSize)
        {
            error = "The PSD section length field could not be read.";
            return false;
        }

        var value = psb
            ? BinaryPrimitives.ReadUInt64BigEndian(buffer)
            : BinaryPrimitives.ReadUInt32BigEndian(buffer);
        dataStart = position + fieldSize;
        if (value > long.MaxValue)
        {
            error = string.Create(CultureInfo.InvariantCulture, $"The PSD section length {value} is too large.");
            return false;
        }

        length = (long)value;
        complete = length <= limit - dataStart;
        end = complete ? dataStart + length : limit;
        if (!complete)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"The PSD section declares {length} byte(s), but only {limit - dataStart} are available.");
        }

        return true;
    }

    public static bool TryReadAdditionalInfoBlock(
        IRandomAccessSource source,
        long position,
        long limit,
        bool psb,
        out PsdAdditionalInfoBlock block,
        out string error)
    {
        block = default;
        error = string.Empty;

        if (position < 0 || limit > source.Length || limit - position < 8)
        {
            error = "The additional layer information header is truncated.";
            return false;
        }

        Span<byte> header = stackalloc byte[8];
        if (SourceRead.ReadAtMost(source, position, header) != header.Length)
        {
            error = "The additional layer information header could not be read.";
            return false;
        }

        var signatureBytes = header[..4];
        var isSmall = signatureBytes.SequenceEqual(PsdConstants.AdditionalSignature);
        var isLarge = signatureBytes.SequenceEqual(PsdConstants.AdditionalLargeSignature);
        if (!isSmall && !isLarge)
        {
            error = $"Expected '8BIM' or '8B64' but found {RawIdentifier.FormatBytes(signatureBytes)}.";
            return false;
        }

        var keyBytes = header[4..];
        var lengthFieldSize = psb && PsdConstants.UsesLongAdditionalLength(keyBytes)
            ? 8
            : 4;
        if (limit - position < 8 + lengthFieldSize)
        {
            error = "The additional layer information length field is truncated.";
            return false;
        }

        Span<byte> lengthBytes = stackalloc byte[8];
        var lengthBuffer = lengthBytes[..lengthFieldSize];
        if (SourceRead.ReadAtMost(source, position + 8, lengthBuffer) != lengthFieldSize)
        {
            error = "The additional layer information length field could not be read.";
            return false;
        }

        var value = lengthFieldSize == 8
            ? BinaryPrimitives.ReadUInt64BigEndian(lengthBuffer)
            : BinaryPrimitives.ReadUInt32BigEndian(lengthBuffer);
        if (value > long.MaxValue)
        {
            error = string.Create(CultureInfo.InvariantCulture, $"The additional layer information length {value} is too large.");
            return false;
        }

        var dataLength = (long)value;
        var dataOffset = position + 8 + lengthFieldSize;
        var complete = dataLength <= limit - dataOffset;
        var dataEnd = complete ? dataOffset + dataLength : limit;
        if (!complete)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"The additional layer information block declares {dataLength} byte(s), but only {limit - dataOffset} are available.");
        }

        block = new PsdAdditionalInfoBlock(
            position,
            dataEnd,
            position,
            position + 4,
            position + 8,
            lengthFieldSize,
            dataOffset,
            complete ? dataLength : limit - dataOffset,
            RawIdentifier.FormatBytes(signatureBytes),
            RawIdentifier.FormatBytes(keyBytes),
            complete,
            complete ? null : error);
        return true;
    }

    public static bool TryReadLayerRecord(
        IRandomAccessSource source,
        long position,
        long limit,
        bool psb,
        out PsdLayerRecordLayout layout,
        out string error)
    {
        layout = default;
        error = string.Empty;

        if (position < 0 || limit > source.Length || limit - position < 18)
        {
            error = "The layer record header is truncated.";
            return false;
        }

        Span<byte> fixedHeader = stackalloc byte[18];
        if (SourceRead.ReadAtMost(source, position, fixedHeader) != fixedHeader.Length)
        {
            error = "The layer record header could not be read.";
            return false;
        }

        var top = BinaryPrimitives.ReadInt32BigEndian(fixedHeader);
        var left = BinaryPrimitives.ReadInt32BigEndian(fixedHeader[4..]);
        var bottom = BinaryPrimitives.ReadInt32BigEndian(fixedHeader[8..]);
        var right = BinaryPrimitives.ReadInt32BigEndian(fixedHeader[12..]);
        var channelCount = BinaryPrimitives.ReadUInt16BigEndian(fixedHeader[16..]);

        var channelLengthSize = 2 + PsdConstants.LengthFieldSize(psb);
        var channelsStart = position + 18;
        var channelsBytes = (long)channelCount * channelLengthSize;
        if (channelsBytes > limit - channelsStart)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"The layer record declares {channelCount} channel(s), but the channel information is truncated.");
            return false;
        }

        var channelsEnd = channelsStart + channelsBytes;
        if (limit - channelsEnd < 16)
        {
            error = "The layer record blend mode or extra data length is truncated.";
            return false;
        }

        Span<byte> blend = stackalloc byte[16];
        if (SourceRead.ReadAtMost(source, channelsEnd, blend) != blend.Length)
        {
            error = "The layer record blend mode could not be read.";
            return false;
        }

        if (!blend[..4].SequenceEqual(PsdConstants.AdditionalSignature))
        {
            error = $"The layer record blend mode signature is {RawIdentifier.FormatBytes(blend[..4])} instead of '8BIM'.";
            return false;
        }

        var extraLength = BinaryPrimitives.ReadUInt32BigEndian(blend[12..]);
        var fixedEnd = channelsEnd + 16;
        var complete = extraLength <= limit - fixedEnd;
        var extraEnd = complete ? fixedEnd + extraLength : limit;

        // The extra field is a sequence of self-describing parts. Clamp every boundary so a
        // malformed length cannot make a child start before its predecessor.
        var maskStart = fixedEnd;
        var maskEnd = ReadPascalBoundary(source, maskStart, extraEnd, 4, out var maskOk);
        var blendingStart = maskEnd;
        var blendingEnd = ReadPascalBoundary(source, blendingStart, extraEnd, 4, out var blendingOk);
        var nameStart = blendingEnd;
        var nameEnd = ReadNameBoundary(source, nameStart, extraEnd, out var nameOk);
        var additionalStart = nameEnd;
        var additionalEnd = extraEnd;

        var stepError = !complete
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"The layer record declares {extraLength} extra byte(s), but only {limit - fixedEnd} are available.")
            : !maskOk
                ? "The layer mask data is truncated."
                : !blendingOk
                    ? "The layer blending ranges are truncated."
                    : !nameOk
                        ? "The layer name is truncated."
                        : null;

        layout = new PsdLayerRecordLayout(
            position,
            extraEnd,
            channelsStart,
            channelsEnd,
            fixedEnd,
            extraEnd,
            maskStart,
            maskEnd,
            blendingStart,
            blendingEnd,
            nameStart,
            nameEnd,
            additionalStart,
            additionalEnd,
            top,
            left,
            bottom,
            right,
            channelCount,
            stepError is null,
            stepError);
        return true;
    }

    public static IReadOnlyList<PsdChannelInfo> ReadChannels(
        IRandomAccessSource source,
        long channelsStart,
        ushort channelCount,
        bool psb)
    {
        var lengthFieldSize = PsdConstants.LengthFieldSize(psb);
        var entrySize = 2 + lengthFieldSize;
        var channels = new PsdChannelInfo[channelCount];
        Span<byte> entry = stackalloc byte[10];
        for (var index = 0; index < channelCount; index++)
        {
            var offset = channelsStart + ((long)index * entrySize);
            if (SourceRead.ReadAtMost(source, offset, entry[..entrySize]) != entrySize)
            {
                break;
            }

            var id = BinaryPrimitives.ReadInt16BigEndian(entry);
            var length = lengthFieldSize == 8
                ? BinaryPrimitives.ReadUInt64BigEndian(entry[2..])
                : BinaryPrimitives.ReadUInt32BigEndian(entry[2..]);
            channels[index] = new PsdChannelInfo(id, length);
        }

        return channels;
    }

    private static long ReadPascalBoundary(
        IRandomAccessSource source,
        long start,
        long limit,
        int fieldSize,
        out bool ok)
    {
        ok = false;
        if (limit - start < fieldSize)
        {
            return limit;
        }

        Span<byte> bytes = stackalloc byte[4];
        var buffer = bytes[..fieldSize];
        if (SourceRead.ReadAtMost(source, start, buffer) != fieldSize)
        {
            return limit;
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(buffer);
        if (length > limit - start - fieldSize)
        {
            return limit;
        }

        ok = true;
        return start + fieldSize + length;
    }

    private static long ReadNameBoundary(IRandomAccessSource source, long start, long limit, out bool ok)
    {
        ok = false;
        if (start >= limit)
        {
            return limit;
        }

        Span<byte> lengthByte = stackalloc byte[1];
        if (SourceRead.ReadAtMost(source, start, lengthByte) != 1)
        {
            return limit;
        }

        var nameLength = (long)lengthByte[0];
        var total = 1 + nameLength;
        var padded = (total + 3) & ~3L;
        if (padded > limit - start)
        {
            return limit;
        }

        ok = true;
        return start + padded;
    }
}
