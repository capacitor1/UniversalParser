using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Embedded;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Flac;

/// <summary>
/// Re-reads one FLAC metadata block, seek point, cuesheet track/index or the audio frame region
/// on demand. All field keys use the FLAC specification's uppercase identifiers.
/// </summary>
internal static class FlacNodeParser
{
    public static NodeParseResult Parse(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        if (node.StableId == "flac-root")
        {
            return ParseRoot(node);
        }

        if (node.StableId == "flac:audio")
        {
            return ParseAudio(node);
        }

        if (node.StableId?.StartsWith("flac:seekpoint:", StringComparison.Ordinal) == true)
        {
            return ParseSeekPoint(context, node);
        }

        if (node.StableId?.StartsWith("flac:track:", StringComparison.Ordinal) == true)
        {
            return ParseTrack(context, node);
        }

        if (node.StableId?.StartsWith("flac:index:", StringComparison.Ordinal) == true)
        {
            return ParseIndex(context, node);
        }

        if (node.StableId?.StartsWith("flac:block:", StringComparison.Ordinal) == true)
        {
            return ParseBlock(context, node);
        }

        return new NodeParseResult(node.Name, node.Range);
    }

    private static NodeParseResult ParseRoot(FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<FileSize>", node.Range.Length, node.Range);
        fields.Derived(
            "<MetadataBlockCount>",
            node.Children.Count(static child => child.StableId?.StartsWith("flac:block:", StringComparison.Ordinal) == true),
            node.Range);
        fields.Derived(
            "<AudioDataLength>",
            node.Children
                .Where(static child => child.StableId == "flac:audio")
                .Sum(static child => child.Range.Length),
            node.Range);
        fields.Note("Synthetic document root; select a metadata block to inspect its fields.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult ParseAudio(FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<AudioDataLength>", node.Range.Length, node.Range);
        fields.Note("FLAC audio frames are not decoded.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult ParseBlock(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        if (!FlacBlock.TryReadHeader(context.Source, node.Range.Offset, node.Range.End, out var header, out _))
        {
            fields.Warning(DiagnosticCodes.TruncatedHeader, "The FLAC metadata block header is truncated.", node.Range);
            return Result(node, fields);
        }

        fields.Raw(
            "LAST_METADATA_BLOCK_FLAG",
            (header.LastBlock ? 1 : 0).ToString(CultureInfo.InvariantCulture),
            new SourceRange(header.Start, 1));
        if (header.Type > FlacConstants.PictureType)
        {
            fields.Raw(
                "BLOCK_TYPE",
                header.Type.ToString(CultureInfo.InvariantCulture),
                new SourceRange(header.Start, 1));
        }

        fields.Raw("LENGTH", header.Length, new SourceRange(header.Start + 1, 3));

        if (header.Type == FlacConstants.ForbiddenType)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                "Metadata block type 127 is forbidden.",
                new SourceRange(header.Start, 1));
        }

        if (header.Truncated)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The metadata block extends beyond the end of the file.",
                SourceRange.FromBounds(header.Start, header.End));
        }

        var reader = new BlockReader(context.Source, header.DataStart, header.DataLength);
        switch (header.Type)
        {
            case FlacConstants.StreamInfoType:
                ParseStreamInfo(fields, reader, header);
                break;
            case FlacConstants.PaddingType:
                fields.Note($"{header.DataLength} padding byte(s) are not interpreted.", new SourceRange(header.DataStart, header.DataLength));
                reader.SkipRemaining();
                break;
            case FlacConstants.ApplicationType:
                ParseApplication(fields, reader, header);
                break;
            case FlacConstants.SeekTableType:
                ParseSeekTable(fields, node, header.DataStart, header.DataLength);
                break;
            case FlacConstants.VorbisCommentType:
                VorbisComment.Parse(
                    fields,
                    reader,
                    string.Empty,
                    VorbisCommentNaming.UpperCase,
                    hasFramingBit: false,
                    context.Limits.MaxStringBytes);
                break;
            case FlacConstants.CuesheetType:
                ParseCuesheet(fields, reader, node, header);
                break;
            case FlacConstants.PictureType:
                ParsePicture(fields, reader, context.Limits.MaxStringBytes);
                break;
            default:
                fields.Note("This reserved metadata block is not interpreted.", new SourceRange(header.DataStart, header.DataLength));
                reader.SkipRemaining();
                break;
        }

        return Result(node, fields);
    }

    private static void ParseStreamInfo(FieldList fields, BlockReader reader, FlacBlockHeader header)
    {
        if (header.Length != FlacConstants.StreamInfoLength)
        {
            fields.Warning(
                DiagnosticCodes.InvalidLength,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"STREAMINFO must be {FlacConstants.StreamInfoLength} bytes, not {header.Length}."),
                new SourceRange(header.DataStart, header.DataLength));
        }

        if (!FlacStreamInfo.TryParse(fields, reader, string.Empty))
        {
            return;
        }

        if (reader.Remaining > 0)
        {
            fields.Note($"{reader.Remaining} byte(s) after STREAMINFO are not interpreted.", reader.CurrentRange);
            reader.SkipRemaining();
        }
    }

    private static void ParseApplication(FieldList fields, BlockReader reader, FlacBlockHeader header)
    {
        if (!reader.TryUInt32BE(out var applicationId))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The APPLICATION_ID is truncated.", reader.CurrentRange);
            reader.SkipRemaining();
            return;
        }

        fields.Raw("APPLICATION_ID", FormatApplicationId(applicationId), reader.LastRange);
        fields.Derived("<DATA_LENGTH>", Math.Max(0, header.DataLength - 4), new SourceRange(reader.Position, reader.Remaining));
        fields.Note("Application data is not interpreted.", new SourceRange(reader.Position, reader.Remaining));
        reader.SkipRemaining();
    }

    private static void ParseSeekTable(FieldList fields, FormatNode node, long dataStart, long dataLength)
    {
        var pointCount = node.Children.Count(static child => child.StableId?.StartsWith("flac:seekpoint:", StringComparison.Ordinal) == true);
        fields.Derived("<SEEK_POINT_COUNT>", pointCount, new SourceRange(dataStart, dataLength));
        if (pointCount > 0)
        {
            fields.Note("Seek points are child nodes.", new SourceRange(dataStart, dataLength));
        }
    }

    private static void ParseCuesheet(FieldList fields, BlockReader reader, FormatNode node, FlacBlockHeader header)
    {
        if (header.DataLength < FlacConstants.CuesheetHeaderLength)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The CUESHEET metadata block is shorter than its fixed header.",
                new SourceRange(header.DataStart, header.DataLength));
            reader.SkipRemaining();
            return;
        }

        Span<byte> catalog = stackalloc byte[128];
        _ = reader.TryReadBytes(catalog);
        fields.Raw("MEDIA_CATALOG_NUMBER", EscapeAscii(catalog), reader.LastRange);

        _ = reader.TryUInt64BE(out var leadIn);
        fields.Raw("LEAD_IN", leadIn, reader.LastRange);

        _ = reader.TryByte(out var flags);
        fields.Raw(
            "CD_FLAG",
            ((flags >> 7) & 1).ToString(CultureInfo.InvariantCulture),
            reader.LastRange);

        Span<byte> reserved = stackalloc byte[258];
        _ = reader.TryReadBytes(reserved);
        fields.Derived("<RESERVED_LENGTH>", 258, reader.LastRange);

        _ = reader.TryByte(out var trackCount);
        fields.Raw("TRACK_COUNT", trackCount.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        if (node.Children.Length > 0)
        {
            fields.Note("Cuesheet tracks are child nodes.", new SourceRange(reader.Position, reader.Remaining));
        }
    }

    private static void ParsePicture(FieldList fields, BlockReader reader, int maxStringBytes)
    {
        if (!reader.TryUInt32BE(out var pictureType))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The PICTURE_TYPE is truncated.", reader.CurrentRange);
            reader.SkipRemaining();
            return;
        }

        fields.Raw("PICTURE_TYPE", FlacConstants.FormatPictureType(pictureType), reader.LastRange);

        if (!reader.TryUInt32BE(out var mediaTypeLength))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The MEDIA_TYPE_LENGTH is truncated.", reader.CurrentRange);
            reader.SkipRemaining();
            return;
        }

        fields.Raw("MEDIA_TYPE_LENGTH", mediaTypeLength, reader.LastRange);
        if (mediaTypeLength > reader.Remaining)
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "MEDIA_TYPE extends beyond the block.", reader.CurrentRange);
            reader.SkipRemaining();
            return;
        }

        var mediaType = reader.ReadText(mediaTypeLength, maxStringBytes, out var mediaRange, out var mediaTruncated);
        fields.Raw("MEDIA_TYPE", mediaType, mediaRange);
        if (mediaTruncated)
        {
            fields.Warning(DiagnosticCodes.LimitString, "MEDIA_TYPE is shown as a prefix.", mediaRange);
        }

        if (!reader.TryUInt32BE(out var descriptionLength))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The DESCRIPTION_LENGTH is truncated.", reader.CurrentRange);
            reader.SkipRemaining();
            return;
        }

        fields.Raw("DESCRIPTION_LENGTH", descriptionLength, reader.LastRange);
        if (descriptionLength > reader.Remaining)
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "DESCRIPTION extends beyond the block.", reader.CurrentRange);
            reader.SkipRemaining();
            return;
        }

        var description = reader.ReadText(descriptionLength, maxStringBytes, out var descriptionRange, out var descriptionTruncated);
        fields.Raw("DESCRIPTION", description, descriptionRange);
        if (descriptionTruncated)
        {
            fields.Warning(DiagnosticCodes.LimitString, "DESCRIPTION is shown as a prefix.", descriptionRange);
        }

        if (!reader.TryUInt32BE(out var width)
            || !reader.TryUInt32BE(out var height)
            || !reader.TryUInt32BE(out var colorDepth)
            || !reader.TryUInt32BE(out var colors))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The PICTURE dimensions are truncated.", reader.CurrentRange);
            reader.SkipRemaining();
            return;
        }

        fields.Raw("WIDTH", width, reader.LastRange);
        fields.Raw("HEIGHT", height, reader.LastRange);
        fields.Raw("COLOR_DEPTH", colorDepth, reader.LastRange);
        fields.Raw("COLORS", colors, reader.LastRange);

        if (!reader.TryUInt32BE(out var dataLength))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The DATA_LENGTH is truncated.", reader.CurrentRange);
            reader.SkipRemaining();
            return;
        }

        fields.Raw("DATA_LENGTH", dataLength, reader.LastRange);
        if (dataLength > reader.Remaining)
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "Picture data extends beyond the block.", reader.CurrentRange);
        }

        fields.Note(
            "Picture data is not decoded.",
            new SourceRange(reader.Position, Math.Min(dataLength, reader.Remaining)));
        reader.SkipRemaining();
    }

    private static NodeParseResult ParseSeekPoint(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = new BlockReader(context.Source, node.Range.Offset, node.Range.Length);
        if (!reader.TryUInt64BE(out var sampleNumber)
            || !reader.TryUInt64BE(out var offset)
            || !reader.TryUInt16BE(out var frameSamples))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The SEEKPOINT is truncated.", node.Range);
            return Result(node, fields);
        }

        fields.Raw(
            "SAMPLE_NUMBER",
            sampleNumber == ulong.MaxValue
                ? "0xFFFFFFFFFFFFFFFF (placeholder)"
                : sampleNumber.ToString(CultureInfo.InvariantCulture),
            reader.LastRange);
        fields.Raw("OFFSET", offset, reader.LastRange);
        fields.Raw("FRAME_SAMPLES", frameSamples, reader.LastRange);
        return Result(node, fields);
    }

    private static NodeParseResult ParseTrack(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = new BlockReader(context.Source, node.Range.Offset, node.Range.Length);
        if (!reader.TryUInt64BE(out var trackOffset)
            || !reader.TryByte(out var trackNumber))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The CUESHEET_TRACK is truncated.", node.Range);
            return Result(node, fields);
        }

        fields.Raw("TRACK_OFFSET", trackOffset, reader.LastRange);
        fields.Raw("TRACK_NUMBER", trackNumber.ToString(CultureInfo.InvariantCulture), reader.LastRange);

        Span<byte> isrc = stackalloc byte[12];
        if (!reader.TryReadBytes(isrc))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The CUESHEET_TRACK ISRC is truncated.", node.Range);
            return Result(node, fields);
        }

        fields.Raw("ISRC", EscapeAscii(isrc), reader.LastRange);

        if (!reader.TryByte(out var flags))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The CUESHEET_TRACK flags are truncated.", node.Range);
            return Result(node, fields);
        }

        fields.Raw("TRACK_TYPE", ((flags >> 7) & 1).ToString(CultureInfo.InvariantCulture), reader.LastRange);
        fields.Raw("PRE_EMPHASIS", ((flags >> 6) & 1).ToString(CultureInfo.InvariantCulture), reader.LastRange);

        Span<byte> reserved = stackalloc byte[13];
        _ = reader.TryReadBytes(reserved);
        fields.Derived("<RESERVED_LENGTH>", 13, reader.LastRange);

        if (!reader.TryByte(out var indexCount))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The CUESHEET_TRACK index count is truncated.", node.Range);
            return Result(node, fields);
        }

        fields.Raw("INDEX_POINT_COUNT", indexCount.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        if (node.Children.Length > 0)
        {
            fields.Note("Index points are child nodes.", new SourceRange(reader.Position, reader.Remaining));
        }

        return Result(node, fields);
    }

    private static NodeParseResult ParseIndex(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = new BlockReader(context.Source, node.Range.Offset, node.Range.Length);
        if (!reader.TryUInt64BE(out var indexOffset) || !reader.TryByte(out var indexNumber))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The CUESHEET_TRACK_INDEX is truncated.", node.Range);
            return Result(node, fields);
        }

        fields.Raw("INDEX_OFFSET", indexOffset, reader.LastRange);
        fields.Raw("INDEX_NUMBER", indexNumber.ToString(CultureInfo.InvariantCulture), reader.LastRange);

        Span<byte> reserved = stackalloc byte[3];
        _ = reader.TryReadBytes(reserved);
        fields.Derived("<RESERVED_LENGTH>", 3, reader.LastRange);
        return Result(node, fields);
    }

    private static string FormatApplicationId(uint value)
    {
        Span<char> text = stackalloc char[4];
        for (var index = 0; index < 4; index++)
        {
            var character = (byte)(value >> (24 - index * 8));
            text[index] = character is >= 0x20 and <= 0x7E ? (char)character : '.';
        }

        return string.Create(CultureInfo.InvariantCulture, $"0x{value:X8} ({new string(text)})");
    }

    private static string EscapeAscii(ReadOnlySpan<byte> value)
    {
        // The CUESHEET catalog/ISRC fields are fixed-width and NUL-padded, not NUL-terminated.
        var end = value.Length;
        while (end > 0 && value[end - 1] == 0)
        {
            end--;
        }

        return TextDecoding.Display(value[..end], TextEncodings.Ascii);
    }

    private static NodeParseResult Result(FormatNode node, FieldList fields, string? title = null) =>
        new(
            title ?? node.Name,
            node.Range,
            node.Range,
            fields.ToImmutable(),
            [.. fields.Diagnostics]);
}
