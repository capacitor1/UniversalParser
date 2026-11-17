using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Flv;

/// <summary>
/// Re-reads one FLV header, tag, or tag body from the source. The tree parser stores only
/// ranges, so selecting a node never depends on a payload copy made during document parsing.
/// </summary>
internal static class FlvNodeParser
{
    public static NodeParseResult Parse(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        if (node.StableId == "flv-root")
        {
            return ParseRoot(node);
        }

        if (node.Name.StartsWith('<'))
        {
            return Placeholder(node);
        }

        if (node.StableId == "flv:header")
        {
            return ParseHeader(context, node);
        }

        if (node.StableId == "flv:prev0")
        {
            return ParsePreviousTagSize0(context, node);
        }

        if (node.StableId?.StartsWith("flv:tag-header:", StringComparison.Ordinal) == true)
        {
            return ParseTagHeader(context, node);
        }

        if (node.StableId?.StartsWith("flv:tag-data:", StringComparison.Ordinal) == true)
        {
            return ParseTagData(context, node);
        }

        if (node.StableId?.StartsWith("flv:prev-size:", StringComparison.Ordinal) == true)
        {
            return ParsePreviousTagSize(context, node);
        }

        if (node.StableId?.StartsWith("flv:tag:", StringComparison.Ordinal) == true)
        {
            return ParseTag(context, node);
        }

        return Failure(node, DiagnosticCodes.InvalidSignature, "The FLV node identity could not be resolved.");
    }

    private static NodeParseResult ParseRoot(FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<FileSize>", node.Range.Length, node.Range);
        fields.Derived("<ChildCount>", node.Children.Length, node.Range);
        var tagCount = node.Children.Count(static child =>
            child.StableId?.StartsWith("flv:tag:", StringComparison.Ordinal) == true);
        fields.Derived("<TagCount>", tagCount, node.Range);
        fields.Note("Synthetic document root; select the FLV header or a tag to inspect its fields.");
        return Result(node, fields);
    }

    private static NodeParseResult ParseHeader(ParseContext context, FormatNode node)
    {
        if (node.Range.Length < FlvConstants.FileHeaderLength)
        {
            return Failure(node, DiagnosticCodes.TruncatedHeader, "The FLV header is truncated.");
        }

        var reader = new SourceReader(context.Source, node.Range.Offset, FlvConstants.FileHeaderLength);
        var signature = reader.ReadBytes(3);
        var version = reader.ReadByte();
        var flags = reader.ReadByte();
        var dataOffset = reader.ReadUInt32(Endian.Big);

        var fields = context.CreateFieldList();
        fields.Raw("Version", version, new SourceRange(node.Range.Offset + 3, 1));
        fields.Raw("Flags", FlvFormatting.FormatFlags(flags), new SourceRange(node.Range.Offset + 4, 1));
        fields.Raw("DataOffset", dataOffset, new SourceRange(node.Range.Offset + 5, 4));

        if (node.Range.Length > FlvConstants.FileHeaderLength)
        {
            var extensionLength = node.Range.Length - FlvConstants.FileHeaderLength;
            fields.Derived("<HeaderExtensionLength>", extensionLength, node.Range);
            var count = (int)Math.Min(64, extensionLength);
            var extensionReader = new SourceReader(context.Source, node.Range.Offset + FlvConstants.FileHeaderLength, count);
            var bytes = extensionReader.ReadBytes(count);
            fields.Derived(
                "<HeaderExtension>",
                FlvFormatting.FormatHex(bytes, count),
                new SourceRange(node.Range.Offset + FlvConstants.FileHeaderLength, count));
            if (extensionLength > count)
            {
                fields.Note(
                    $"Only the first {count} of {extensionLength} header extension byte(s) are shown.",
                    node.Range);
            }
        }

        if (version != 1)
        {
            fields.Warning(
                DiagnosticCodes.UnsupportedVersion,
                $"FLV Version {version} is not the commonly documented version 1; parsing continues.",
                new SourceRange(node.Range.Offset + 3, 1));
        }

        return Result(node, fields);
    }

    private static NodeParseResult ParsePreviousTagSize0(ParseContext context, FormatNode node)
    {
        if (node.Range.Length < FlvConstants.PreviousTagSizeLength)
        {
            return Failure(node, DiagnosticCodes.TruncatedHeader, "PreviousTagSize0 is truncated.");
        }

        var reader = new SourceReader(context.Source, node.Range.Offset, FlvConstants.PreviousTagSizeLength);
        var value = reader.ReadUInt32(Endian.Big);
        var fields = context.CreateFieldList();
        fields.Raw("PreviousTagSize0", value, node.Range);
        fields.Derived("<ExpectedPreviousTagSize0>", 0L, node.Range);
        if (value != 0)
        {
            fields.Warning(
                DiagnosticCodes.PreviousTagSizeMismatch,
                $"PreviousTagSize0 is {value}; the FLV specification requires 0.",
                node.Range);
        }

        return Result(node, fields);
    }

    private static NodeParseResult ParseTagHeader(ParseContext context, FormatNode node)
    {
        if (node.Range.Length < FlvConstants.TagHeaderLength)
        {
            return Failure(node, DiagnosticCodes.TruncatedHeader, "The FLV tag header is truncated.");
        }

        var reader = new SourceReader(context.Source, node.Range.Offset, FlvConstants.TagHeaderLength);
        var fields = context.CreateFieldList();
        var header = AddTagHeaderFields(fields, reader);
        fields.Derived("<TimestampMs>", header.FullTimestamp, new SourceRange(node.Range.Offset + 4, 4));
        return Result(node, fields);
    }

    private static NodeParseResult ParseTag(ParseContext context, FormatNode node)
    {
        if (node.Range.Length < FlvConstants.TagHeaderLength)
        {
            return Failure(node, DiagnosticCodes.TruncatedHeader, "The FLV tag is shorter than its header.");
        }

        var reader = new SourceReader(context.Source, node.Range.Offset, FlvConstants.TagHeaderLength);
        var fields = context.CreateFieldList();
        var header = AddTagHeaderFields(fields, reader);
        var dataSize = header.DataSize;

        var previousTagSizeNode = node.Children.FirstOrDefault(static child =>
            child.StableId?.StartsWith("flv:prev-size:", StringComparison.Ordinal) == true);
        var expected = FlvConstants.TagHeaderLength + (long)dataSize;
        if (previousTagSizeNode is { Range.Length: 4 })
        {
            var previousReader = new SourceReader(
                context.Source,
                previousTagSizeNode.Range.Offset,
                FlvConstants.PreviousTagSizeLength);
            var previousTagSize = previousReader.ReadUInt32(Endian.Big);
            fields.Raw("PreviousTagSize", previousTagSize, previousTagSizeNode.Range);
            fields.Derived("<TimestampMs>", header.FullTimestamp, new SourceRange(node.Range.Offset + 4, 4));
            fields.Derived("<ExpectedPreviousTagSize>", expected, previousTagSizeNode.Range);
            if (previousTagSize != expected)
            {
                fields.Warning(
                    DiagnosticCodes.PreviousTagSizeMismatch,
                    $"PreviousTagSize is {previousTagSize}; expected {expected} (11 + DataSize).",
                    previousTagSizeNode.Range);
            }
        }
        else
        {
            fields.Derived("<TimestampMs>", header.FullTimestamp, new SourceRange(node.Range.Offset + 4, 4));
        }

        return Result(node, fields);
    }

    private static NodeParseResult ParsePreviousTagSize(ParseContext context, FormatNode node)
    {
        if (node.Range.Length < FlvConstants.PreviousTagSizeLength)
        {
            return Failure(node, DiagnosticCodes.TruncatedHeader, "PreviousTagSize is truncated.");
        }

        var reader = new SourceReader(context.Source, node.Range.Offset, FlvConstants.PreviousTagSizeLength);
        var value = reader.ReadUInt32(Endian.Big);
        var expected = ReadExpectedPreviousTagSize(node.StableId);
        var fields = context.CreateFieldList();
        fields.Raw("PreviousTagSize", value, node.Range);
        if (expected is { } expectedValue)
        {
            fields.Derived("<ExpectedPreviousTagSize>", expectedValue, node.Range);
            if (value != expectedValue)
            {
                fields.Warning(
                    DiagnosticCodes.PreviousTagSizeMismatch,
                    $"PreviousTagSize is {value}; expected {expectedValue} (11 + DataSize).",
                    node.Range);
            }
        }

        return Result(node, fields);
    }

    private static NodeParseResult ParseTagData(ParseContext context, FormatNode node)
    {
        if (!TryReadTagDataIdentity(node, out var tagType, out var filter))
        {
            return Failure(node, DiagnosticCodes.InvalidSignature, "The FLV tag data identity could not be resolved.");
        }

        var fields = context.CreateFieldList();
        if (node.Range.IsEmpty)
        {
            fields.Derived("<PayloadLength>", 0L, node.Range);
            fields.Note("The tag data is empty.", node.Range);
            return Result(node, fields);
        }

        var reader = new SourceReader(context.Source, node.Range.Offset, node.Range.Length);
        if (filter != 0)
        {
            fields.Derived("<PayloadLength>", reader.Remaining, node.Range);
            fields.Note("Filter is 1; encrypted or filtered tag data is not parsed.", node.Range);
            return Result(node, fields);
        }

        switch (tagType)
        {
            case FlvConstants.TagTypeAudio:
                ParseAudioTagData(reader, fields);
                break;
            case FlvConstants.TagTypeVideo:
                ParseVideoTagData(context, reader, fields);
                break;
            case FlvConstants.TagTypeScript:
                FlvAmf0Parser.ParseScript(context, reader, fields);
                if (reader.Remaining > 0)
                {
                    fields.Derived("<UnparsedLength>", reader.Remaining, reader.CurrentRange);
                }
                break;
            default:
                fields.Derived("<UnparsedLength>", node.Range.Length, node.Range);
                fields.Derived("<PayloadLength>", node.Range.Length, node.Range);
                fields.Note($"TagType {tagType} is not defined by the FLV specification; the tag data is unparsed.", node.Range);
                break;
        }

        return Result(node, fields);
    }

    private static void ParseAudioTagData(SourceReader reader, FieldList fields)
    {
        if (!TryReadByte(fields, reader, out var header, "AudioTagHeader"))
        {
            return;
        }

        var headerRange = new SourceRange(reader.Position - 1, 1);
        var soundFormat = (header >> 4) & 0x0F;
        var soundRate = (header >> 2) & 0x03;
        var soundSize = (header >> 1) & 0x01;
        var soundType = header & 0x01;
        fields.Raw("SoundFormat", FlvFormatting.FormatSoundFormat(soundFormat), headerRange);
        fields.Raw("SoundRate", FlvFormatting.FormatSoundRate(soundRate), headerRange);
        fields.Raw("SoundSize", FlvFormatting.FormatSoundSize(soundSize), headerRange);
        fields.Raw("SoundType", FlvFormatting.FormatSoundType(soundType), headerRange);

        if (soundFormat == FlvConstants.SoundFormatAac)
        {
            if (TryReadByte(fields, reader, out var aacPacketType, "AACPacketType"))
            {
                fields.Raw(
                    "AACPacketType",
                    FlvFormatting.FormatAacPacketType(aacPacketType),
                    new SourceRange(reader.Position - 1, 1));
                if (aacPacketType == 0)
                {
                    fields.Note("The AAC sequence header (AudioSpecificConfig) is not decoded.", reader.CurrentRange);
                }
            }
        }

        fields.Derived("<PayloadLength>", reader.Remaining, reader.CurrentRange);
        fields.Note("Audio payload is not decoded.", reader.CurrentRange);
    }

    private static void ParseVideoTagData(ParseContext context, SourceReader reader, FieldList fields)
    {
        if (!TryReadByte(fields, reader, out var first, "VideoTagHeader"))
        {
            return;
        }

        var headerRange = new SourceRange(reader.Position - 1, 1);
        var isExHeader = (first & 0x80) != 0;
        if (!isExHeader)
        {
            ParseLegacyVideoTagData(reader, fields, first, headerRange);
            return;
        }

        var frameType = (first >> 4) & 0x07;
        var packetType = first & 0x0F;
        fields.Raw("IsExHeader", "1", headerRange);
        fields.Raw("VideoFrameType", FlvFormatting.FormatFrameType(frameType), headerRange);
        fields.Raw("VideoPacketType", FlvFormatting.FormatVideoPacketType(packetType), headerRange);

        var currentPacketType = packetType;
        for (var guard = 0; currentPacketType == FlvConstants.VideoPacketTypeModEx && guard < context.Limits.MaxDataLines; guard++)
        {
            context.ThrowIfCancellationRequested();
            var sizeStart = reader.Position;
            if (!TryReadByte(fields, reader, out var sizeByte, "modExDataSize"))
            {
                return;
            }

            long modExDataSize = sizeByte + 1L;
            if (modExDataSize == 256)
            {
                if (!TryReadUInt16(fields, reader, out var size16, "modExDataSize"))
                {
                    return;
                }

                modExDataSize = size16 + 1L;
            }

            var sizeRange = SourceRange.FromBounds(sizeStart, reader.Position);
            if (modExDataSize > reader.Remaining)
            {
                fields.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    $"modExDataSize is {modExDataSize} but only {reader.Remaining} byte(s) remain.",
                    reader.CurrentRange);
                return;
            }

            var modExStart = reader.Position;
            var previewBytes = checked((int)Math.Min(modExDataSize, Math.Min(32, context.Limits.MaxBlobBytes)));
            var modExData = reader.ReadBytes(previewBytes);
            reader.Skip(modExDataSize - previewBytes);
            fields.Raw("modExDataSize", modExDataSize, sizeRange);
            if (modExData.Length > 0)
            {
                fields.Raw(
                    "modExData",
                    FlvFormatting.FormatHex(modExData, 32) + (modExData.Length < modExDataSize ? " ..." : string.Empty),
                    new SourceRange(modExStart, modExDataSize));
            }

            if (!TryReadByte(fields, reader, out var modExTypeByte, "videoPacketModExType"))
            {
                return;
            }

            var modExType = (modExTypeByte >> 4) & 0x0F;
            currentPacketType = modExTypeByte & 0x0F;
            var modExRange = new SourceRange(reader.Position - 1, 1);
            fields.Raw("videoPacketModExType", (long)modExType, modExRange);
            fields.Raw("videoPacketType", FlvFormatting.FormatVideoPacketType(currentPacketType), modExRange);
            if (modExType == 0 && modExDataSize >= 3)
            {
                var nanoRange = new SourceRange(modExStart, 3);
                var nanoOffset = context.Reader(nanoRange).ReadUInt24(Endian.Big);
                fields.Raw("videoTimestampNanoOffset", nanoOffset, nanoRange);
            }
        }

        if (currentPacketType == FlvConstants.VideoPacketTypeModEx)
        {
            fields.Warning(DiagnosticCodes.LimitRecords, "Video modifier limit reached.", reader.CurrentRange);
            fields.Derived("<UnparsedLength>", reader.Remaining, reader.CurrentRange);
            return;
        }

        if (currentPacketType == FlvConstants.VideoPacketTypeMetadata)
        {
            FlvAmf0Parser.ParseMetadata(context, reader, fields);
            fields.Derived("<PayloadLength>", reader.Remaining, reader.CurrentRange);
            fields.Note("Metadata payload is AMF0; video payload is not decoded.", reader.CurrentRange);
            return;
        }

        if (frameType == 5 && currentPacketType != FlvConstants.VideoPacketTypeMetadata)
        {
            if (TryReadByte(fields, reader, out var command, "VideoCommand"))
            {
                fields.Raw(
                    "VideoCommand",
                    FlvFormatting.FormatVideoCommand(command),
                    new SourceRange(reader.Position - 1, 1));
            }

            fields.Derived("<PayloadLength>", reader.Remaining, reader.CurrentRange);
            fields.Note("Video command payload is not decoded.", reader.CurrentRange);
            return;
        }

        if (currentPacketType == FlvConstants.VideoPacketTypeMultitrack)
        {
            ParseMultitrackVideo(context, reader, fields);
            return;
        }

        if (!TryReadFourCc(fields, reader, out var fourCc, out var fourCcRange))
        {
            return;
        }

        fields.Raw("VideoFourCc", FlvFormatting.FormatFourCc(fourCc), fourCcRange);
        var fourCcText = FlvFormatting.DecodeFourCc(fourCc);
        var hasCompositionTime = fourCcText is "avc1" or "hvc1" or "hev1" or "vvc1";
        if (currentPacketType == FlvConstants.VideoPacketTypeCodedFrames && hasCompositionTime)
        {
            if (TryReadUInt24(fields, reader, out var compositionTime, "compositionTimeOffset"))
            {
                fields.Raw(
                    "compositionTimeOffset",
                    FlvFormatting.FormatSigned24(compositionTime),
                    new SourceRange(reader.Position - 3, 3));
            }
        }
        else if (currentPacketType == FlvConstants.VideoPacketTypeCodedFramesX && hasCompositionTime)
        {
            fields.Derived("<compositionTimeOffset>", "0 (implied by CodedFramesX)", reader.CurrentRange);
        }
        else if (currentPacketType == FlvConstants.VideoPacketTypeSequenceStart)
        {
            fields.Note(
                $"The {fourCcText} sequence header is not decoded.",
                reader.CurrentRange);
        }
        else if (currentPacketType == FlvConstants.VideoPacketTypeSequenceEnd)
        {
            fields.Note("SequenceEnd carries no video data.", reader.CurrentRange);
        }
        else if (currentPacketType == FlvConstants.VideoPacketTypeMpeg2TsSequenceStart)
        {
            fields.Note("MPEG2TSSequenceStart payload is not decoded.", reader.CurrentRange);
        }
        else
        {
            fields.Note(
                $"VideoPacketType {currentPacketType} is not decoded.",
                reader.CurrentRange);
        }

        fields.Derived("<PayloadLength>", reader.Remaining, reader.CurrentRange);
        fields.Note("Video payload is not decoded.", reader.CurrentRange);
    }

    private static void ParseMultitrackVideo(ParseContext context, SourceReader reader, FieldList fields)
    {
        if (!TryReadByte(fields, reader, out var packetInfo, "videoMultitrackType"))
        {
            return;
        }

        var infoRange = new SourceRange(reader.Position - 1, 1);
        var multitrackType = (packetInfo >> 4) & 0x0F;
        var trackPacketType = packetInfo & 0x0F;
        fields.Raw("videoMultitrackType", FlvFormatting.FormatMultitrackType(multitrackType), infoRange);
        fields.Raw("videoPacketType", FlvFormatting.FormatVideoPacketType(trackPacketType), infoRange);

        if (multitrackType is not (
                FlvConstants.AvMultitrackTypeOneTrack
                or FlvConstants.AvMultitrackTypeManyTracks
                or FlvConstants.AvMultitrackTypeManyTracksManyCodecs))
        {
            fields.Warning(
                DiagnosticCodes.InvalidEncoding,
                $"videoMultitrackType {multitrackType} is reserved; the multitrack body is not decoded.",
                infoRange);
            fields.Derived("<PayloadLength>", reader.Remaining, reader.CurrentRange);
            return;
        }

        byte[]? commonFourCc = null;
        if (multitrackType != FlvConstants.AvMultitrackTypeManyTracksManyCodecs)
        {
            if (!TryReadFourCc(fields, reader, out var fourCc, out var fourCcRange))
            {
                return;
            }

            commonFourCc = fourCc;
            fields.Raw("VideoFourCc", FlvFormatting.FormatFourCc(fourCc), fourCcRange);
        }

        var trackIndex = 0;
        do
        {
            context.ThrowIfCancellationRequested();
            if (trackIndex >= context.Limits.MaxDataLines || fields.IsFull)
            {
                fields.Warning(
                    DiagnosticCodes.LimitRecords,
                    $"Multitrack limit ({context.Limits.MaxDataLines}) reached; remaining tracks are not expanded.",
                    reader.CurrentRange);
                break;
            }

            var trackPrefix = string.Create(CultureInfo.InvariantCulture, $"Track[{trackIndex}].");
            byte[]? trackFourCc = commonFourCc;
            if (multitrackType == FlvConstants.AvMultitrackTypeManyTracksManyCodecs)
            {
                if (!TryReadFourCc(fields, reader, out var fourCc, out var fourCcRange))
                {
                    return;
                }

                trackFourCc = fourCc;
                fields.Raw(trackPrefix + "videoFourCc", FlvFormatting.FormatFourCc(fourCc), fourCcRange);
            }

            if (!TryReadByte(fields, reader, out var trackId, "videoTrackId"))
            {
                return;
            }

            fields.Raw(trackPrefix + "videoTrackId", trackId, new SourceRange(reader.Position - 1, 1));

            var trackSize = reader.Remaining;
            if (multitrackType != FlvConstants.AvMultitrackTypeOneTrack)
            {
                if (!TryReadUInt24(fields, reader, out var declaredSize, "sizeOfVideoTrack"))
                {
                    return;
                }

                fields.Raw(trackPrefix + "sizeOfVideoTrack", declaredSize, new SourceRange(reader.Position - 3, 3));
                trackSize = declaredSize;
                if (trackSize > reader.Remaining)
                {
                    fields.Warning(
                        DiagnosticCodes.TruncatedPayload,
                        $"sizeOfVideoTrack is {trackSize} but only {reader.Remaining} byte(s) remain.",
                        reader.CurrentRange);
                    trackSize = reader.Remaining;
                }
            }

            var trackStart = reader.Position;
            var trackReader = new SourceReader(context.Source, trackStart, trackSize);
            var trackType = trackPacketType;
            var trackFourCcText = trackFourCc is null ? null : FlvFormatting.DecodeFourCc(trackFourCc);
            if (trackType == FlvConstants.VideoPacketTypeCodedFrames
                && trackFourCcText is "avc1" or "hvc1" or "hev1" or "vvc1")
            {
                if (TryReadUInt24(fields, trackReader, out var compositionTime, "compositionTimeOffset"))
                {
                    fields.Raw(
                        trackPrefix + "compositionTimeOffset",
                        FlvFormatting.FormatSigned24(compositionTime),
                        new SourceRange(trackReader.Position - 3, 3));
                }
            }
            else if (trackType == FlvConstants.VideoPacketTypeCodedFramesX
                && trackFourCcText is "avc1" or "hvc1" or "hev1" or "vvc1")
            {
                fields.Derived($"<{trackPrefix}compositionTimeOffset>", "0 (implied by CodedFramesX)", trackReader.CurrentRange);
            }
            else if (trackType == FlvConstants.VideoPacketTypeSequenceStart)
            {
                fields.Note($"{trackPrefix}{trackFourCcText ?? "track"} sequence header is not decoded.", trackReader.CurrentRange);
            }
            else if (trackType == FlvConstants.VideoPacketTypeSequenceEnd)
            {
                fields.Note($"{trackPrefix}SequenceEnd carries no video data.", trackReader.CurrentRange);
            }
            else if (trackType == FlvConstants.VideoPacketTypeMetadata)
            {
                FlvAmf0Parser.ParseMetadata(context, trackReader, fields, trackPrefix.TrimEnd('.'));
            }
            else if (trackType == FlvConstants.VideoPacketTypeMpeg2TsSequenceStart)
            {
                fields.Note($"{trackPrefix}MPEG2TSSequenceStart payload is not decoded.", trackReader.CurrentRange);
            }

            fields.Derived($"<{trackPrefix}PayloadLength>", trackReader.Remaining, trackReader.CurrentRange);
            fields.Note($"{trackPrefix}video payload is not decoded.", trackReader.CurrentRange);

            trackReader.Skip(trackReader.Remaining);
            reader.Skip(trackSize);
            trackIndex++;

            // A zero-length track is legal (e.g. SequenceEnd). Its ID and size fields already
            // advanced the parent reader; never mistake it for a non-progressing loop.
            if (multitrackType == FlvConstants.AvMultitrackTypeOneTrack) break;
        }
        while (reader.Remaining > 0);

        fields.Derived("<TrackCount>", trackIndex, reader.CurrentRange);
    }

    private static void ParseLegacyVideoTagData(
        SourceReader reader,
        FieldList fields,
        byte first,
        SourceRange headerRange)
    {
        var frameType = (first >> 4) & 0x0F;
        var codecId = first & 0x0F;
        fields.Raw("FrameType", FlvFormatting.FormatFrameType(frameType), headerRange);
        fields.Raw("CodecID", FlvFormatting.FormatCodecId(codecId), headerRange);

        if (frameType == 5)
        {
            if (TryReadByte(fields, reader, out var command, "VideoCommand"))
            {
                fields.Raw(
                    "VideoCommand",
                    FlvFormatting.FormatVideoCommand(command),
                    new SourceRange(reader.Position - 1, 1));
            }

            fields.Derived("<PayloadLength>", reader.Remaining, reader.CurrentRange);
            fields.Note("Video command payload is not decoded.", reader.CurrentRange);
            return;
        }

        if (codecId is FlvConstants.CodecIdAvc or FlvConstants.CodecIdHevcLegacy)
        {
            var packetTypeName = codecId == FlvConstants.CodecIdAvc ? "AVCPacketType" : "HEVCPacketType";
            if (TryReadByte(fields, reader, out var packetType, packetTypeName))
            {
                fields.Raw(
                    packetTypeName,
                    codecId == FlvConstants.CodecIdAvc
                        ? FlvFormatting.FormatAvcPacketType(packetType)
                        : FlvFormatting.FormatHevcPacketType(packetType),
                    new SourceRange(reader.Position - 1, 1));
            }

            if (TryReadUInt24(fields, reader, out var compositionTime, "CompositionTime"))
            {
                fields.Raw(
                    "CompositionTime",
                    FlvFormatting.FormatSigned24(compositionTime),
                    new SourceRange(reader.Position - 3, 3));
            }

            if (codecId == FlvConstants.CodecIdHevcLegacy)
            {
                fields.Note(
                    "CodecID 12 is the legacy HEVC convention; Enhanced FLV signals HEVC with FourCC 'hvc1' or 'hev1'.",
                    headerRange);
            }
        }
        else
        {
            fields.Note(
                $"CodecID {codecId} payload is not decoded.",
                headerRange);
        }

        fields.Derived("<PayloadLength>", reader.Remaining, reader.CurrentRange);
        fields.Note("Video payload is not decoded.", reader.CurrentRange);
    }

    private static TagHeaderInfo AddTagHeaderFields(FieldList fields, SourceReader reader)
    {
        var start = reader.Position;
        var tagTypeByte = reader.ReadByte();
        var tagType = (byte)(tagTypeByte & 0x1F);
        var filter = (byte)((tagTypeByte >> 5) & 0x01);
        var reserved = (byte)((tagTypeByte >> 6) & 0x03);
        var dataSize = reader.ReadUInt24(Endian.Big);
        var timestamp = reader.ReadUInt24(Endian.Big);
        var timestampExtended = reader.ReadByte();
        var streamId = reader.ReadUInt24(Endian.Big);

        fields.Raw("TagType", FlvFormatting.FormatTagType(tagType), new SourceRange(start, 1));
        fields.Raw("Filter", (long)filter, new SourceRange(start, 1));
        fields.Raw("Reserved", (long)reserved, new SourceRange(start, 1));

        fields.Raw("DataSize", dataSize, new SourceRange(start + 1, 3));
        fields.Raw("Timestamp", timestamp, new SourceRange(start + 4, 3));
        fields.Raw("TimestampExtended", timestampExtended, new SourceRange(start + 7, 1));
        fields.Raw("StreamID", streamId, new SourceRange(start + 8, 3));
        var fullTimestamp = (long)timestampExtended << 24 | timestamp;
        return new TagHeaderInfo(dataSize, fullTimestamp);
    }

    private static bool TryReadTagDataIdentity(FormatNode node, out byte tagType, out byte filter)
    {
        tagType = 0;
        filter = 0;
        var stableId = node.StableId;
        if (stableId is null)
        {
            return false;
        }

        var parts = stableId.Split(':');
        return parts.Length >= 4
            && string.Equals(parts[0], "flv", StringComparison.Ordinal)
            && string.Equals(parts[1], "tag-data", StringComparison.Ordinal)
            && byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out tagType)
            && byte.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out filter);
    }

    private static long? ReadExpectedPreviousTagSize(string? stableId)
    {
        if (stableId is null)
        {
            return null;
        }

        var parts = stableId.Split(':');
        return parts.Length >= 3
            && long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
    }

    private static bool TryReadByte(FieldList fields, SourceReader reader, out byte value, string what)
    {
        if (reader.Remaining < 1)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"The {what} field is truncated.",
                reader.CurrentRange);
            value = 0;
            return false;
        }

        value = reader.ReadByte();
        return true;
    }

    private static bool TryReadUInt16(FieldList fields, SourceReader reader, out ushort value, string what)
    {
        if (reader.Remaining < 2)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"The {what} field is truncated.",
                reader.CurrentRange);
            value = 0;
            return false;
        }

        value = reader.ReadUInt16(Endian.Big);
        return true;
    }

    private static bool TryReadUInt24(FieldList fields, SourceReader reader, out uint value, string what)
    {
        if (reader.Remaining < 3)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"The {what} field is truncated.",
                reader.CurrentRange);
            value = 0;
            return false;
        }

        value = reader.ReadUInt24(Endian.Big);
        return true;
    }

    private static bool TryReadFourCc(
        FieldList fields,
        SourceReader reader,
        out byte[] fourCc,
        out SourceRange range)
    {
        if (reader.Remaining < 4)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                "The VideoFourCc field is truncated.",
                reader.CurrentRange);
            fourCc = [];
            range = reader.CurrentRange;
            return false;
        }

        var offset = reader.Position;
        fourCc = reader.ReadBytes(4);
        range = new SourceRange(offset, 4);
        return true;
    }

    private readonly record struct TagHeaderInfo(uint DataSize, long FullTimestamp);

    private static NodeParseResult Placeholder(FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<Length>", node.Range.Length, node.Range);
        fields.Note("This synthetic range is not a parsed FLV structure.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult Result(FormatNode node, FieldList fields) =>
        new(
            TitleFor(node),
            node.Range,
            node.Range,
            fields.ToImmutable(),
            ImmutableArray.CreateRange(fields.Diagnostics));

    private static string TitleFor(FormatNode node)
    {
        if (node.StableId is not { } stable || !stable.StartsWith("flv:", StringComparison.Ordinal))
        {
            return node.Name;
        }

        var parts = stable.Split(':');
        return parts.Length >= 2
            ? parts[1] switch
            {
                "header" => "FLVHeader 'FLV'",
                "prev0" => "PreviousTagSize0 '0x00000000'",
                "tag" when parts.Length >= 3 && byte.TryParse(parts[2], out var tagType)
                    => FlvDocumentParser.BuildTagTitle(tagType),
                "tag-data" when parts.Length >= 3 && byte.TryParse(parts[2], out var dataType)
                    => FlvDocumentParser.BuildDataTitle(dataType),
                "tag-header" when parts.Length >= 3 && byte.TryParse(parts[2], out var headerType)
                    => $"TagHeader '0x{headerType:X2}'",
                "prev-size" when parts.Length >= 3 && long.TryParse(parts[2], out var size)
                    => $"PreviousTagSize '0x{size:X8}'",
                _ => node.Name,
            }
            : node.Name;
    }

    private static NodeParseResult Failure(FormatNode node, string code, string message)
    {
        var diagnostic = new Diagnostic(DiagnosticSeverity.Error, code, message, node.Range);
        return new NodeParseResult(
            node.Name,
            node.Range,
            node.Range,
            [Field.Error(message, range: node.Range)],
            [diagnostic]);
    }
}
