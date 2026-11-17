using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Asf;

/// <summary>
/// Re-reads one ASF object or the Data Object packet region. The tree stores only ranges, so
/// selecting a node never depends on bytes copied during document parsing. Data Packets are
/// embedded payload and are reported as an unparsed region, never split or decoded.
/// </summary>
internal static class AsfNodeParser
{
    private const int HexPreviewBytes = 64;

    public static NodeParseResult Parse(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        if (node.StableId == "asf-root")
        {
            return ParseRoot(node);
        }

        if (node.Name.StartsWith('<'))
        {
            return Placeholder(node);
        }

        if (node.StableId?.StartsWith("asf:data-packets:", StringComparison.Ordinal) == true)
        {
            return ParseDataPackets(node);
        }

        if (TryParseObjectIdentity(node.StableId, out var expectedId, out var offset))
        {
            return ParseObject(context, node, expectedId, offset);
        }

        return Failure(node, DiagnosticCodes.InvalidSignature, "The ASF node identity could not be resolved.");
    }

    private static NodeParseResult ParseRoot(FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<FileSize>", node.Range.Length, node.Range);
        fields.Derived("<ChildCount>", node.Children.Length, node.Range);
        var objectCount = node.Children.Count(static child =>
            child.StableId?.StartsWith("asf:obj:", StringComparison.Ordinal) == true);
        fields.Derived("<ObjectCount>", objectCount, node.Range);
        fields.Note("Synthetic document root; select an ASF object to inspect its fields.");
        return Result(node, fields);
    }

    private static NodeParseResult ParseObject(
        ParseContext context,
        FormatNode node,
        Guid expectedId,
        long offset)
    {
        var fields = context.CreateFieldList();
        if (node.Range.Length < AsfConstants.ObjectHeaderLength)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                "The ASF object is shorter than its 24-byte header.",
                node.Range);
            return Result(node, fields);
        }

        var reader = new AsfFieldReader(
            new SourceReader(context.Source, node.Range.Offset, node.Range.Length),
            fields,
            context.Limits);

        if (!reader.TryGuidRaw(out var actualId))
        {
            return Result(node, fields);
        }

        if (!reader.TryUInt64("Object Size", out _))
        {
            return Result(node, fields);
        }

        if (actualId != expectedId)
        {
            fields.Warning(
                DiagnosticCodes.InvalidSignature,
                $"Object GUID is {AsfFormatting.FormatGuid(actualId)}, but the tree expected {AsfFormatting.FormatGuid(expectedId)}.",
                new SourceRange(offset, 16));
        }

        switch (actualId)
        {
            case var id when id == AsfConstants.HeaderObject:
                ParseHeaderObject(node, reader, fields);
                break;
            case var id when id == AsfConstants.DataObject:
                ParseDataObject(reader, fields);
                break;
            case var id when id == AsfConstants.FilePropertiesObject:
                ParseFileProperties(context, reader, fields);
                break;
            case var id when id == AsfConstants.StreamPropertiesObject:
                ParseStreamProperties(context, reader, fields);
                break;
            case var id when id == AsfConstants.HeaderExtensionObject:
                ParseHeaderExtension(node, reader, fields);
                break;
            case var id when id == AsfConstants.ExtendedStreamPropertiesObject:
                ParseExtendedStreamProperties(context.Limits, reader, fields);
                break;
            case var id when id == AsfConstants.ContentDescriptionObject:
                ParseContentDescription(reader, fields);
                break;
            case var id when id == AsfConstants.ExtendedContentDescriptionObject:
                ParseExtendedContentDescription(context.Limits, reader, fields);
                break;
            case var id when id == AsfConstants.StreamBitratePropertiesObject:
                ParseStreamBitrateProperties(reader);
                break;
            case var id when id == AsfConstants.CodecListObject:
                ParseCodecList(reader);
                break;
            case var id when id == AsfConstants.LanguageListObject:
                ParseLanguageList(reader);
                break;
            case var id when id == AsfConstants.MetadataObject:
                ParseMetadata(context.Limits, reader, fields, library: false);
                break;
            case var id when id == AsfConstants.MetadataLibraryObject:
                ParseMetadata(context.Limits, reader, fields, library: true);
                break;
            case var id when id == AsfConstants.CompatibilityObject:
                ParseCompatibility(reader, fields);
                break;
            case var id when id == AsfConstants.PaddingObject:
                ParsePadding(node, fields);
                break;
            case var id when id == AsfConstants.SimpleIndexObject:
                ParseSimpleIndex(reader, fields, context.Limits);
                break;
            case var id when id == AsfConstants.IndexObject:
                ParseIndexObject(id, reader, fields, context.Limits);
                break;
            case var id when id == AsfConstants.MediaObjectIndexObject:
                ParseIndexObject(id, reader, fields, context.Limits);
                break;
            case var id when id == AsfConstants.TimecodeIndexObject:
                ParseIndexObject(id, reader, fields, context.Limits);
                break;
            default:
                ParseUnparsedObject(node, reader, fields);
                break;
        }

        return Result(node, fields);
    }

    private static void ParseHeaderObject(FormatNode node, AsfFieldReader reader, FieldList fields)
    {
        if (!reader.TryUInt32("Number of Header Objects", out _))
        {
            return;
        }

        _ = reader.TryByte("Reserved1", out var reserved1);
        _ = reader.TryByte("Reserved2", out var reserved2);
        if (reserved1 != AsfConstants.HeaderReserved1 || reserved2 != AsfConstants.HeaderReserved2)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                $"Reserved1/Reserved2 are {reserved1}/{reserved2}; the specification requires 1/2.",
                reader.LastRange);
        }

        fields.Derived("<ChildCount>", node.Children.Length, node.Range);
    }

    private static void ParseDataObject(AsfFieldReader reader, FieldList fields)
    {
        _ = reader.TryGuid("File ID", out _);
        _ = reader.TryUInt64("Total Data Packets", out _);
        if (reader.TryUInt16("Reserved", out var reserved) && reserved != AsfConstants.DataObjectReserved)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                $"Reserved is 0x{reserved:X4}; the specification requires 0x0101.",
                reader.LastRange);
        }

        fields.Derived("<DataPacketRegionLength>", reader.Remaining, reader.CurrentRange);
        fields.Note(
            "Data Packets are embedded in the Data Object and are not GUID-delimited objects; they are not split or decoded.",
            reader.CurrentRange);
    }

    private static void ParseFileProperties(ParseContext context, AsfFieldReader reader, FieldList fields)
    {
        _ = reader.TryGuid("File ID", out _);
        var hasFileSize = reader.TryUInt64("File Size", out var fileSize);
        var fileSizeRange = reader.LastRange;
        var hasCreationDate = reader.TryUInt64("Creation Date", out var creationDate);
        var creationDateRange = reader.LastRange;
        _ = reader.TryUInt64("Data Packets Count", out _);
        var hasPlayDuration = reader.TryUInt64("Play Duration", out var playDuration);
        var playDurationRange = reader.LastRange;
        var hasSendDuration = reader.TryUInt64("Send Duration", out var sendDuration);
        var sendDurationRange = reader.LastRange;
        var hasPreroll = reader.TryUInt64("Preroll", out var preroll);
        var prerollRange = reader.LastRange;
        var hasFlags = reader.TryUInt32("Flags", out var flags, static value => AsfFormatting.FormatFlags(
            value,
            (0x0001, "Broadcast"),
            (0x0002, "Seekable")));
        var flagsRange = reader.LastRange;
        _ = reader.TryUInt32("Minimum Data Packet Size", out _);
        _ = reader.TryUInt32("Maximum Data Packet Size", out _);
        _ = reader.TryUInt32("Maximum Bitrate", out _);

        if (hasCreationDate)
        {
            fields.Derived("<Creation Date (UTC)>", AsfFormatting.FormatFileTime(creationDate), creationDateRange);
        }

        if (hasPlayDuration)
        {
            fields.Derived("<Play Duration (ms)>", AsfFormatting.FormatHundredNanoseconds(playDuration), playDurationRange);
        }

        if (hasSendDuration)
        {
            fields.Derived("<Send Duration (ms)>", AsfFormatting.FormatHundredNanoseconds(sendDuration), sendDurationRange);
        }

        if (hasPreroll)
        {
            fields.Derived("<Preroll (ms)>", AsfFormatting.FormatHundredNanoseconds(preroll), prerollRange);
        }

        if (hasFlags)
        {
            fields.Derived("<Broadcast>", (flags & 0x0001) != 0 ? "true" : "false", flagsRange);
            fields.Derived("<Seekable>", (flags & 0x0002) != 0 ? "true" : "false", flagsRange);
        }

        if (hasFileSize && fileSize != (ulong)context.Source.Length)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                $"File Size is {fileSize}, but the source length is {context.Source.Length}.",
                fileSizeRange);
        }
    }

    private static void ParseStreamProperties(ParseContext context, AsfFieldReader reader, FieldList fields)
    {
        _ = reader.TryGuid("Stream Type", out var streamType, AsfFormatting.FormatStreamType);
        _ = reader.TryGuid("Error Correction Type", out _, AsfFormatting.FormatErrorCorrectionType);
        if (reader.TryUInt64("Time Offset", out var timeOffset))
        {
            fields.Derived("<Time Offset (ms)>", AsfFormatting.FormatHundredNanoseconds(timeOffset), reader.LastRange);
        }

        var hasTypeSpecificLength = reader.TryUInt32("Type-Specific Data Length", out var typeSpecificLength);
        var hasErrorCorrectionLength = reader.TryUInt32("Error Correction Data Length", out var errorCorrectionLength);
        _ = reader.TryUInt16("Flags", out _, AsfFormatting.FormatStreamPropertiesFlags);
        _ = reader.TryUInt32("Reserved", out _);

        if (hasTypeSpecificLength && typeSpecificLength > 0)
        {
            var available = Math.Min((long)typeSpecificLength, reader.Remaining);
            if (typeSpecificLength > reader.Remaining)
            {
                fields.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    $"Type-Specific Data Length is {typeSpecificLength}, but only {reader.Remaining} byte(s) remain.",
                    reader.CurrentRange);
            }

            var typeSpecificStart = reader.Position;
            var typeSpecificReader = new AsfFieldReader(
                new SourceReader(context.Source, typeSpecificStart, available),
                fields,
                context.Limits);

            if (streamType == AsfConstants.VideoMedia)
            {
                ParseVideoTypeSpecific(typeSpecificReader, fields);
            }
            else if (streamType == AsfConstants.AudioMedia)
            {
                ParseAudioTypeSpecific(typeSpecificReader, fields);
            }
            else
            {
                fields.Derived("<UnparsedTypeSpecificDataLength>", available, new SourceRange(typeSpecificStart, available));
                fields.Note("Type-specific data for this stream type is not decoded.", new SourceRange(typeSpecificStart, available));
            }

            if (typeSpecificReader.Remaining > 0)
            {
                fields.Derived(
                    "<UnparsedTypeSpecificDataLength>",
                    typeSpecificReader.Remaining,
                    typeSpecificReader.CurrentRange);
            }

            _ = reader.Skip(available, "Type-Specific Data");
        }

        if (hasErrorCorrectionLength && errorCorrectionLength > 0)
        {
            var available = Math.Min((long)errorCorrectionLength, reader.Remaining);
            if (errorCorrectionLength > reader.Remaining)
            {
                fields.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    $"Error Correction Data Length is {errorCorrectionLength}, but only {reader.Remaining} byte(s) remain.",
                    reader.CurrentRange);
            }

            if (available > 0)
            {
                fields.Derived("<ErrorCorrectionDataLength>", available, reader.CurrentRange);
                fields.Note("Error Correction Data is not decoded.", reader.CurrentRange);
                _ = reader.Skip(available, "Error Correction Data");
            }
        }
    }

    private static void ParseVideoTypeSpecific(AsfFieldReader reader, FieldList fields)
    {
        _ = reader.TryUInt32("Encoded Image Width", out _);
        _ = reader.TryUInt32("Encoded Image Height", out _);
        if (reader.TryByte("Reserved Flags", out var reservedFlags) && reservedFlags != 2)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                $"Reserved Flags is {reservedFlags}; the specification requires 2.",
                reader.LastRange);
        }

        if (!reader.TryUInt16("Format Data Size", out var formatDataSize))
        {
            return;
        }

        var formatAvailable = Math.Min((long)formatDataSize, reader.Remaining);
        if (formatDataSize > reader.Remaining)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"Format Data Size is {formatDataSize}, but only {reader.Remaining} byte(s) remain.",
                reader.CurrentRange);
        }

        if (formatAvailable < AsfConstants.BitmapInfoHeaderLength)
        {
            fields.Derived("<UnparsedFormatDataLength>", formatAvailable, reader.CurrentRange);
            fields.Note("Format Data is shorter than the 40-byte BITMAPINFOHEADER.", reader.CurrentRange);
            _ = reader.Skip(formatAvailable, "Format Data");
            return;
        }

        _ = reader.TryUInt32("biSize", out _);
        _ = reader.TryInt32("biWidth", out _);
        _ = reader.TryInt32("biHeight", out _);
        _ = reader.TryUInt16("biPlanes", out _);
        _ = reader.TryUInt16("biBitCount", out _);

        var compressionBytes = reader.ReadValue("biCompression", 4, 4, out var compressionRange);
        if (compressionBytes.Length == 4)
        {
            fields.Raw(
                "biCompression",
                AsfFormatting.FormatCompression(BinaryPrimitives.ReadUInt32LittleEndian(compressionBytes), compressionBytes),
                compressionRange);
        }
        else
        {
            fields.Raw("biCompression", AsfFormatting.FormatHex(compressionBytes, 4), compressionRange);
        }

        _ = reader.TryUInt32("biSizeImage", out _);
        _ = reader.TryInt32("biXPelsPerMeter", out _);
        _ = reader.TryInt32("biYPelsPerMeter", out _);
        _ = reader.TryUInt32("biClrUsed", out _);
        _ = reader.TryUInt32("biClrImportant", out _);

        var codecDataLength = Math.Min(formatAvailable - AsfConstants.BitmapInfoHeaderLength, reader.Remaining);
        if (codecDataLength > 0)
        {
            fields.Derived("<CodecDataLength>", codecDataLength, reader.CurrentRange);
            fields.Note("Codec-specific format data is not decoded.", reader.CurrentRange);
            _ = reader.Skip(codecDataLength, "Codec-specific format data");
        }
    }

    private static void ParseAudioTypeSpecific(AsfFieldReader reader, FieldList fields)
    {
        _ = reader.TryUInt16("wFormatTag", out _, AsfFormatting.FormatWaveFormatTag);
        _ = reader.TryUInt16("nChannels", out _);
        _ = reader.TryUInt32("nSamplesPerSec", out _);
        _ = reader.TryUInt32("nAvgBytesPerSec", out _);
        _ = reader.TryUInt16("nBlockAlign", out _);
        _ = reader.TryUInt16("wBitsPerSample", out _);
        if (!reader.TryUInt16("cbSize", out var codecDataLength))
        {
            return;
        }

        if (codecDataLength > 0)
        {
            var available = Math.Min((long)codecDataLength, reader.Remaining);
            if (codecDataLength > reader.Remaining)
            {
                fields.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    $"cbSize is {codecDataLength}, but only {reader.Remaining} byte(s) remain.",
                    reader.CurrentRange);
            }

            fields.Derived("<CodecDataLength>", available, reader.CurrentRange);
            fields.Note("Audio codec-specific data is not decoded.", reader.CurrentRange);
            _ = reader.Skip(available, "Audio codec data");
        }

        if (reader.Remaining > 0)
        {
            fields.Derived("<UnparsedTypeSpecificDataLength>", reader.Remaining, reader.CurrentRange);
            fields.Note("Bytes follow the WAVEFORMATEX structure.", reader.CurrentRange);
            _ = reader.Skip(reader.Remaining, "Remaining audio type-specific data");
        }
    }

    private static void ParseHeaderExtension(FormatNode node, AsfFieldReader reader, FieldList fields)
    {
        _ = reader.TryGuid("Reserved Field 1", out _, AsfFormatting.FormatGuid);
        if (reader.TryUInt16("Reserved Field 2", out var reserved2) && reserved2 != AsfConstants.HeaderExtensionReserved2)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                $"Reserved Field 2 is {reserved2}; the specification requires 6.",
                reader.LastRange);
        }

        _ = reader.TryUInt32("Header Extension Data Size", out _);
        fields.Derived("<ChildCount>", node.Children.Length, node.Range);
    }

    private static void ParseExtendedStreamProperties(
        ParseLimits limits,
        AsfFieldReader reader,
        FieldList fields)
    {
        var hasStart = reader.TryUInt64("Start Time", out var startTime);
        var startRange = reader.LastRange;
        var hasEnd = reader.TryUInt64("End Time", out var endTime);
        var endRange = reader.LastRange;
        _ = reader.TryUInt32("Data Bitrate", out _);
        _ = reader.TryUInt32("Buffer Size", out _);
        _ = reader.TryUInt32("Initial Buffer Fullness", out _);
        _ = reader.TryUInt32("Alternate Data Bitrate", out _);
        _ = reader.TryUInt32("Alternate Buffer Size", out _);
        _ = reader.TryUInt32("Alternate Initial Buffer Fullness", out _);
        _ = reader.TryUInt32("Maximum Object Size", out _);
        var hasFlags = reader.TryUInt32("Flags", out var flags, static value => AsfFormatting.FormatFlags(
            value,
            (0x0001, "Reliable"),
            (0x0002, "Seekable"),
            (0x0004, "No Cleanpoints"),
            (0x0008, "Resend Live Cleanpoints")));
        var flagsRange = reader.LastRange;
        _ = reader.TryUInt16("Stream Number", out _);
        _ = reader.TryUInt16("Stream Language ID Index", out _);
        var hasFrameTime = reader.TryUInt64("Average Time Per Frame", out var frameTime);
        var frameTimeRange = reader.LastRange;
        var hasStreamNames = reader.TryUInt16("Stream Name Count", out var streamNameCount);
        var hasExtensionCount = reader.TryUInt16("Payload Extension System Count", out var extensionCount);

        if (hasStart)
        {
            fields.Derived("<Start Time (ms)>", AsfFormatting.FormatHundredNanoseconds(startTime), startRange);
        }

        if (hasEnd)
        {
            fields.Derived("<End Time (ms)>", AsfFormatting.FormatHundredNanoseconds(endTime), endRange);
        }

        if (hasFrameTime)
        {
            fields.Derived("<Average Time Per Frame (ms)>", AsfFormatting.FormatHundredNanoseconds(frameTime), frameTimeRange);
        }

        if (hasStreamNames)
        {
            var limit = Math.Min(streamNameCount, Math.Min(limits.MaxDataLines, ushort.MaxValue));
            if (streamNameCount > limit)
            {
                fields.Warning(
                    DiagnosticCodes.LimitRecords,
                    $"Stream Name Count is {streamNameCount}; only the first {limit} entries are shown.",
                    reader.CurrentRange);
            }

            ParseStreamNames(reader, limit);
        }

        if (hasExtensionCount)
        {
            var limit = Math.Min(extensionCount, Math.Min(limits.MaxDataLines, ushort.MaxValue));
            if (extensionCount > limit)
            {
                fields.Warning(
                    DiagnosticCodes.LimitRecords,
                    $"Payload Extension System Count is {extensionCount}; only the first {limit} entries are shown.",
                    reader.CurrentRange);
            }

            ParsePayloadExtensionSystems(reader, limit);
        }

        if (reader.Remaining >= AsfConstants.ObjectHeaderLength)
        {
            var embeddedStart = reader.Position;
            if (reader.TryGuid("Embedded Object GUID", out var embeddedId))
            {
                if (reader.TryUInt64("Embedded Object Size", out _))
                {
                    fields.Derived(
                        "<EmbeddedStreamPropertiesOffset>",
                        embeddedStart,
                        new SourceRange(embeddedStart, AsfConstants.ObjectHeaderLength));
                    fields.Note(
                        embeddedId == AsfConstants.StreamPropertiesObject
                            ? "The embedded Stream Properties Object is not expanded as a child node."
                            : "Bytes follow the Payload Extension Systems; they are not decoded.",
                        reader.CurrentRange);
                }
            }
        }
        else if (reader.Remaining > 0)
        {
            fields.Derived("<UnparsedLength>", reader.Remaining, reader.CurrentRange);
            fields.Note("The remaining Extended Stream Properties bytes are not decoded.", reader.CurrentRange);
        }

        if (hasFlags)
        {
            fields.Derived("<Reliable>", (flags & 0x0001) != 0 ? "true" : "false", flagsRange);
            fields.Derived("<Seekable>", (flags & 0x0002) != 0 ? "true" : "false", flagsRange);
            fields.Derived("<NoCleanpoints>", (flags & 0x0004) != 0 ? "true" : "false", flagsRange);
            fields.Derived("<ResendLiveCleanpoints>", (flags & 0x0008) != 0 ? "true" : "false", flagsRange);
            fields.Derived("<ReservedFlags>", (flags >> 4) & 0x0FFFFFFF, flagsRange);
        }
    }

    private static void ParseStreamNames(AsfFieldReader reader, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var prefix = string.Create(CultureInfo.InvariantCulture, $"Stream Name[{index}]");
            if (!reader.TryUInt16($"{prefix}.Language ID Index", out _))
            {
                return;
            }

            if (!reader.TryUInt16($"{prefix}.Stream Name Length", out var nameLength))
            {
                return;
            }

            _ = reader.TryUtf16($"{prefix}.Stream Name", nameLength);
        }
    }

    private static void ParsePayloadExtensionSystems(AsfFieldReader reader, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var prefix = string.Create(CultureInfo.InvariantCulture, $"Payload Extension System[{index}]");
            if (!reader.TryGuid($"{prefix}.Extension System GUID", out _, AsfFormatting.FormatGuid))
            {
                return;
            }

            if (!reader.TryUInt16($"{prefix}.Extension Data Size", out _))
            {
                return;
            }

            if (!reader.TryUInt32($"{prefix}.Extension System Info Length", out var infoLength))
            {
                return;
            }

            _ = reader.TryHex($"{prefix}.Extension System Info", infoLength, HexPreviewBytes);
        }
    }

    private static void ParseContentDescription(AsfFieldReader reader, FieldList fields)
    {
        var lengths = new ushort[5];
        var names = new[] { "Title", "Author", "Copyright", "Description", "Rating" };
        for (var index = 0; index < lengths.Length; index++)
        {
            if (!reader.TryUInt16($"{names[index]} Length", out lengths[index]))
            {
                return;
            }
        }

        for (var index = 0; index < names.Length; index++)
        {
            _ = reader.TryUtf16(names[index], lengths[index]);
        }

        if (reader.Remaining > 0)
        {
            fields.Derived("<UnparsedLength>", reader.Remaining, reader.CurrentRange);
            fields.Note("Bytes follow the five Content Description strings.", reader.CurrentRange);
        }
    }

    private static void ParseExtendedContentDescription(
        ParseLimits limits,
        AsfFieldReader reader,
        FieldList fields)
    {
        if (!reader.TryUInt16("Content Descriptors Count", out var count))
        {
            return;
        }

        for (var index = 0; index < count; index++)
        {
            var prefix = string.Create(CultureInfo.InvariantCulture, $"Content Descriptor[{index}]");
            if (!reader.TryUInt16($"{prefix}.Descriptor Name Length", out var nameLength))
            {
                return;
            }

            if (!reader.TryUtf16($"{prefix}.Descriptor Name", nameLength))
            {
                return;
            }

            if (!reader.TryUInt16($"{prefix}.Descriptor Value Data Type", out var valueType, AsfFormatting.FormatValueType))
            {
                return;
            }

            if (!reader.TryUInt16($"{prefix}.Descriptor Value Length", out var valueLength))
            {
                return;
            }

            AddValueField(
                limits,
                reader,
                fields,
                $"{prefix}.Descriptor Value",
                valueType,
                valueLength,
                bool32: true,
                allowGuid: false);
        }

        if (reader.Remaining > 0)
        {
            fields.Derived("<UnparsedLength>", reader.Remaining, reader.CurrentRange);
            fields.Note("Bytes follow the Content Descriptors.", reader.CurrentRange);
        }
    }

    private static void ParseMetadata(
        ParseLimits limits,
        AsfFieldReader reader,
        FieldList fields,
        bool library)
    {
        if (!reader.TryUInt16("Description Records Count", out var count))
        {
            return;
        }

        for (var index = 0; index < count; index++)
        {
            var prefix = string.Create(CultureInfo.InvariantCulture, $"Description Record[{index}]");
            if (library)
            {
                _ = reader.TryUInt16($"{prefix}.Language List Index", out _);
            }
            else if (reader.TryUInt16($"{prefix}.Reserved (Must Be Zero)", out var reserved) && reserved != 0)
            {
                fields.Warning(
                    DiagnosticCodes.SchemaMismatch,
                    $"{prefix}.Reserved (Must Be Zero) is {reserved}.",
                    reader.LastRange);
            }

            if (!reader.TryUInt16($"{prefix}.Stream Number", out _))
            {
                return;
            }

            if (!reader.TryUInt16($"{prefix}.Name Length", out var nameLength))
            {
                return;
            }

            if (!reader.TryUInt16($"{prefix}.Data Type", out var dataType, AsfFormatting.FormatValueType))
            {
                return;
            }

            if (!reader.TryUInt32($"{prefix}.Data Length", out var dataLength))
            {
                return;
            }

            _ = reader.TryUtf16($"{prefix}.Name", nameLength);
            AddValueField(
                limits,
                reader,
                fields,
                $"{prefix}.Data",
                dataType,
                dataLength,
                bool32: false,
                allowGuid: library);
        }

        if (reader.Remaining > 0)
        {
            fields.Derived("<UnparsedLength>", reader.Remaining, reader.CurrentRange);
            fields.Note("Bytes follow the Description Records.", reader.CurrentRange);
        }
    }

    private static void ParseStreamBitrateProperties(AsfFieldReader reader)
    {
        if (!reader.TryUInt16("Bitrate Records Count", out var count))
        {
            return;
        }

        var available = reader.Remaining / 6;
        var limit = (int)Math.Min(count, available);
        for (var index = 0; index < limit; index++)
        {
            var prefix = string.Create(CultureInfo.InvariantCulture, $"Bitrate Record[{index}]");
            if (!reader.TryUInt16($"{prefix}.Flags", out _, AsfFormatting.FormatBitrateRecordFlags)
                || !reader.TryUInt32($"{prefix}.Average Bitrate", out _))
            {
                return;
            }
        }
    }

    private static void ParseCodecList(AsfFieldReader reader)
    {
        _ = reader.TryGuid("Reserved", out _, AsfFormatting.FormatGuid);
        if (!reader.TryUInt32("Codec Entries Count", out var count))
        {
            return;
        }

        for (var index = 0u; index < count; index++)
        {
            var prefix = string.Create(CultureInfo.InvariantCulture, $"Codec Entry[{index}]");
            if (!reader.TryUInt16($"{prefix}.Type", out _, AsfFormatting.FormatCodecType))
            {
                return;
            }

            if (!reader.TryUInt16($"{prefix}.Codec Name Length", out var nameLength))
            {
                return;
            }

            _ = reader.TryUtf16($"{prefix}.Codec Name", (long)nameLength * 2);

            if (!reader.TryUInt16($"{prefix}.Codec Description Length", out var descriptionLength))
            {
                return;
            }

            _ = reader.TryUtf16($"{prefix}.Codec Description", (long)descriptionLength * 2);

            if (!reader.TryUInt16($"{prefix}.Codec Information Length", out var informationLength))
            {
                return;
            }

            _ = reader.TryHex($"{prefix}.Codec Information", informationLength, HexPreviewBytes);
        }
    }

    private static void ParseLanguageList(AsfFieldReader reader)
    {
        if (!reader.TryUInt16("Language ID Records Count", out var count))
        {
            return;
        }

        for (var index = 0; index < count; index++)
        {
            var prefix = string.Create(CultureInfo.InvariantCulture, $"Language ID Record[{index}]");
            if (!reader.TryByte($"{prefix}.Language ID Length", out var length))
            {
                return;
            }

            _ = reader.TryUtf16($"{prefix}.Language ID", length);
        }
    }

    private static void ParseCompatibility(AsfFieldReader reader, FieldList fields)
    {
        if (reader.TryByte("Profile", out var profile) && profile != 2)
        {
            fields.Warning(DiagnosticCodes.SchemaMismatch, $"Profile is {profile}; the specification requires 2.", reader.LastRange);
        }

        if (reader.TryByte("Mode", out var mode) && mode != 1)
        {
            fields.Warning(DiagnosticCodes.SchemaMismatch, $"Mode is {mode}; the specification requires 1.", reader.LastRange);
        }
    }

    private static void ParsePadding(FormatNode node, FieldList fields)
    {
        fields.Derived("<PaddingLength>", Math.Max(0, node.Range.Length - AsfConstants.ObjectHeaderLength), node.Range);
        fields.Note("Padding Data is not decoded.", node.Range);
    }

    private static void ParseSimpleIndex(AsfFieldReader reader, FieldList fields, ParseLimits limits)
    {
        _ = reader.TryGuid("File ID", out _);
        if (reader.TryUInt64("Index Entry Time Interval", out var interval))
        {
            fields.Derived("<Index Entry Time Interval (ms)>", AsfFormatting.FormatHundredNanoseconds(interval), reader.LastRange);
        }

        _ = reader.TryUInt32("Maximum Packet Count", out _);
        if (!reader.TryUInt32("Index Entries Count", out var count))
        {
            return;
        }

        var limit = Math.Min(count, (uint)Math.Max(0, limits.MaxDataLines));
        if (count > limit)
        {
            fields.Warning(
                DiagnosticCodes.LimitRecords,
                $"Index Entries Count is {count}; only the first {limit} entries are shown.",
                reader.CurrentRange);
        }

        for (var index = 0u; index < limit; index++)
        {
            var prefix = string.Create(CultureInfo.InvariantCulture, $"Index Entry[{index}]");
            if (!reader.TryUInt32($"{prefix}.Packet Number", out _))
            {
                return;
            }

            if (!reader.TryUInt16($"{prefix}.Packet Count", out _))
            {
                return;
            }
        }

        if (reader.Remaining > 0)
        {
            fields.Derived("<UnparsedLength>", reader.Remaining, reader.CurrentRange);
            fields.Note("Bytes follow the Index Entries.", reader.CurrentRange);
        }
    }

    private static void ParseIndexObject(Guid id, AsfFieldReader reader, FieldList fields, ParseLimits limits)
    {
        if (id == AsfConstants.IndexObject)
        {
            _ = reader.TryUInt32("Index Entry Time Interval", out _);
        }
        else if (id == AsfConstants.MediaObjectIndexObject)
        {
            _ = reader.TryUInt32("Index Entry Count Interval", out _);
        }
        else
        {
            _ = reader.TryUInt32("Reserved", out _);
        }

        if (!reader.TryUInt16("Index Specifiers Count", out var specifiersCount))
        {
            return;
        }

        _ = reader.TryUInt32("Index Blocks Count", out _);
        var limit = Math.Min(specifiersCount, Math.Min(limits.MaxDataLines, ushort.MaxValue));
        for (var index = 0; index < limit; index++)
        {
            var prefix = string.Create(CultureInfo.InvariantCulture, $"Index Specifier[{index}]");
            if (!reader.TryUInt16($"{prefix}.Stream Number", out _))
            {
                return;
            }

            _ = reader.TryUInt16($"{prefix}.Index Type", out _);
        }

        if (reader.Remaining > 0)
        {
            fields.Derived("<UnparsedIndexDataLength>", reader.Remaining, reader.CurrentRange);
            fields.Note("Index Blocks are not decoded.", reader.CurrentRange);
        }
    }

    private static void ParseUnparsedObject(FormatNode node, AsfFieldReader reader, FieldList fields)
    {
        fields.Derived("<UnparsedLength>", reader.Remaining, reader.CurrentRange);
        fields.Note(
            node.Name.StartsWith("UnknownObject", StringComparison.Ordinal)
                ? "The ASF object GUID is not defined by the supported specification."
                : "This ASF object is defined by the specification but is not decoded by this version.",
            node.Range);
    }

    private static void AddValueField(
        ParseLimits limits,
        AsfFieldReader reader,
        FieldList fields,
        string key,
        ushort valueType,
        long valueLength,
        bool bool32,
        bool allowGuid)
    {
        var maxRead = valueType switch
        {
            0x0000 => Math.Max(2, limits.MaxStringBytes & ~1),
            0x0001 => HexPreviewBytes,
            0x0002 => bool32 ? 4 : 2,
            0x0003 => 4,
            0x0004 => 8,
            0x0005 => 2,
            0x0006 when allowGuid => 16,
            _ => HexPreviewBytes,
        };

        var bytes = reader.ReadValue(key, valueLength, maxRead, out var range);
        var formatted = FormatValue(valueType, bytes, bool32, allowGuid);
        fields.Raw(key, formatted, range);
        if (range.Length > bytes.Length)
        {
            fields.Note($"{key} shows the first {bytes.Length} of {range.Length} byte(s).", range);
        }

        if (valueType == 0x0000 && (range.Length & 1) != 0)
        {
            fields.Warning(DiagnosticCodes.InvalidLength, $"{key} has an odd byte length.", range);
        }
    }

    private static string FormatValue(ushort valueType, byte[] bytes, bool bool32, bool allowGuid)
    {
        switch (valueType)
        {
            case 0x0000:
                return AsfFormatting.FormatText(bytes);
            case 0x0001:
                return AsfFormatting.FormatHex(bytes, HexPreviewBytes);
            case 0x0002:
                var boolLength = bool32 ? 4 : 2;
                if (bytes.Length >= boolLength)
                {
                    var boolValue = bool32
                        ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
                        : BinaryPrimitives.ReadUInt16LittleEndian(bytes);
                    return string.Create(
                        CultureInfo.InvariantCulture,
                        $"{boolValue} ({(boolValue != 0 ? "true" : "false")})");
                }

                return AsfFormatting.FormatHex(bytes, HexPreviewBytes);
            case 0x0003:
                return bytes.Length >= 4
                    ? BinaryPrimitives.ReadUInt32LittleEndian(bytes).ToString(CultureInfo.InvariantCulture)
                    : AsfFormatting.FormatHex(bytes, HexPreviewBytes);
            case 0x0004:
                return bytes.Length >= 8
                    ? BinaryPrimitives.ReadUInt64LittleEndian(bytes).ToString(CultureInfo.InvariantCulture)
                    : AsfFormatting.FormatHex(bytes, HexPreviewBytes);
            case 0x0005:
                return bytes.Length >= 2
                    ? BinaryPrimitives.ReadUInt16LittleEndian(bytes).ToString(CultureInfo.InvariantCulture)
                    : AsfFormatting.FormatHex(bytes, HexPreviewBytes);
            case 0x0006 when allowGuid:
                return bytes.Length >= 16
                    ? AsfFormatting.FormatGuid(new Guid(bytes.AsSpan(0, 16)))
                    : AsfFormatting.FormatHex(bytes, HexPreviewBytes);
            default:
                return AsfFormatting.FormatHex(bytes, HexPreviewBytes);
        }
    }

    private static NodeParseResult ParseDataPackets(FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<UnparsedLength>", node.Range.Length, node.Range);
        fields.Note(
            "Data Packet bytes are embedded payload, not GUID-delimited ASF objects; they are not split or decoded.",
            node.Range);
        return Result(node, fields);
    }

    private static bool TryParseObjectIdentity(string? stableId, out Guid id, out long offset)
    {
        id = Guid.Empty;
        offset = 0;
        if (stableId is null)
        {
            return false;
        }

        var parts = stableId.Split(':', 4);
        return parts.Length == 4
            && string.Equals(parts[0], "asf", StringComparison.Ordinal)
            && string.Equals(parts[1], "obj", StringComparison.Ordinal)
            && Guid.TryParse(parts[2], out id)
            && long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);
    }

    private static NodeParseResult Placeholder(FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<Length>", node.Range.Length, node.Range);
        fields.Note("This synthetic range is not a parsed ASF structure.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult Result(FormatNode node, FieldList fields) =>
        new(
            TitleFor(node),
            node.Range,
            node.Range,
            fields.ToImmutable(),
            ImmutableArray.CreateRange(fields.Diagnostics));

    private static string TitleFor(FormatNode node) =>
        TryParseObjectIdentity(node.StableId, out var id, out _)
            ? AsfGuids.GetObjectTitle(id)
            : node.Name;

    private static NodeParseResult Failure(FormatNode node, string code, string message)
    {
        var diagnostic = new Diagnostic(DiagnosticSeverity.Error, code, message, node.Range);
        return new NodeParseResult(
            TitleFor(node),
            node.Range,
            node.Range,
            [Field.Error(message, range: node.Range)],
            [diagnostic]);
    }
}
