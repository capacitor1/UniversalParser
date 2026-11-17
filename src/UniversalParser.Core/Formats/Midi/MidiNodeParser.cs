using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Midi;

/// <summary>
/// Re-reads one MIDI header, track, or event on demand. Every field is emitted on its own line;
/// event data is never packed into a single summary string. Only the requested event payload is
/// read, so selecting a track header does not load the whole track into memory.
/// </summary>
internal static class MidiNodeParser
{
    public static NodeParseResult Parse(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        if (node.StableId == "midi-root")
        {
            return ParseRoot(context, node);
        }

        if (node.StableId?.StartsWith("midi:header:", StringComparison.Ordinal) == true)
        {
            return ParseHeader(context, node);
        }

        if (node.StableId?.StartsWith("midi:track:", StringComparison.Ordinal) == true)
        {
            return ParseTrack(context, node);
        }

        if (node.StableId?.StartsWith("midi:event:", StringComparison.Ordinal) == true)
        {
            return ParseEvent(context, node);
        }

        return ParseChunk(context, node);
    }

    private static NodeParseResult ParseRoot(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        fields.Derived("<ChunkCount>", node.Children.Length, node.Range);
        fields.Derived(
            "<TrackCount>",
            node.Children.Count(static child => child.StableId?.StartsWith("midi:track:", StringComparison.Ordinal) == true),
            node.Range);
        fields.Note("Synthetic document root; select a chunk or track event to inspect its fields.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult ParseHeader(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = new SourceReader(context.Source, node.Range.Offset, node.Range.Length);
        if (reader.Remaining < MidiConstants.ChunkHeaderLength)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"The MThd chunk needs {MidiConstants.ChunkHeaderLength} header bytes but only {reader.Remaining} are available.",
                node.Range);
            return Result(node, fields);
        }

        var id = reader.ReadBytes(4);
        var declaredLength = reader.ReadUInt32(Endian.Big);
        fields.Raw("length", declaredLength, new SourceRange(node.Range.Offset + 4, 4));
        if (declaredLength < MidiConstants.HeaderDataLength)
        {
            fields.Warning(
                DiagnosticCodes.InvalidLength,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"MThd declares {declaredLength} data byte(s); the Standard MIDI File header requires {MidiConstants.HeaderDataLength}."),
                node.Range);
        }

        if (reader.Remaining >= 2)
        {
            var range = new SourceRange(reader.Position, 2);
            var format = reader.ReadUInt16(Endian.Big);
            fields.Raw("format", format, range);
            fields.Derived("<FormatName>", MidiFormatting.FormatHeaderFormat(format), range);
            if (format > 2)
            {
                fields.Warning(
                    DiagnosticCodes.SchemaMismatch,
                    $"Header format {format} is not defined by the Standard MIDI File specification.",
                    range);
            }
        }
        else
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The MThd format field is missing.", node.Range);
        }

        if (reader.Remaining >= 2)
        {
            var range = new SourceRange(reader.Position, 2);
            fields.Raw("ntrks", reader.ReadUInt16(Endian.Big), range);
        }
        else
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The MThd ntrks field is missing.", node.Range);
        }

        if (reader.Remaining >= 2)
        {
            var range = new SourceRange(reader.Position, 2);
            var division = reader.ReadUInt16(Endian.Big);
            fields.Raw("division", division, range);
            fields.Derived("<Division>", MidiFormatting.FormatDivision(division), range);
        }
        else
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The MThd division field is missing.", node.Range);
        }

        if (declaredLength > MidiConstants.HeaderDataLength)
        {
            var extra = declaredLength - MidiConstants.HeaderDataLength;
            fields.Derived("<ExtraHeaderLength>", extra, node.Range);
            fields.Note("Bytes after the six standard header fields are not interpreted.", node.Range);
        }

        var title = $"MThd '{RawIdentifier.FormatBytes(id)}'";
        return Result(node, fields, title);
    }

    private static NodeParseResult ParseTrack(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = new SourceReader(context.Source, node.Range.Offset, node.Range.Length);
        if (reader.Remaining >= MidiConstants.ChunkHeaderLength)
        {
            var id = reader.ReadBytes(4);
            var declaredLength = reader.ReadUInt32(Endian.Big);
            fields.Raw("length", declaredLength, new SourceRange(node.Range.Offset + 4, 4));
            var payloadLength = node.PayloadRange?.Length ?? 0;
            fields.Derived("<PayloadLength>", payloadLength, node.PayloadRange);
            var eventCount = node.Children.Count(
                static child => child.StableId?.StartsWith("midi:event:", StringComparison.Ordinal) == true);
            fields.Derived("<EventCount>", eventCount, node.Range);
            var hasEnd = node.Children.Any(static child => string.Equals(child.Name, "EndOfTrack", StringComparison.Ordinal));
            fields.Derived("<EndOfTrackPresent>", hasEnd ? "yes" : "no", node.Range);
            fields.Note(
                "Every track event is listed as a child node in file order; select an event to inspect its delta time and data bytes.",
                node.Range);
            var title = $"MTrk '{RawIdentifier.FormatBytes(id)}'";
            return Result(node, fields, title);
        }

        fields.Warning(DiagnosticCodes.TruncatedHeader, "The MTrk chunk header is truncated.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult ParseChunk(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = new SourceReader(context.Source, node.Range.Offset, node.Range.Length);
        if (reader.Remaining < MidiConstants.ChunkHeaderLength)
        {
            fields.Warning(DiagnosticCodes.TruncatedHeader, "The chunk header is truncated.", node.Range);
            return Result(node, fields);
        }

        var id = reader.ReadBytes(4);
        var declaredLength = reader.ReadUInt32(Endian.Big);
        fields.Raw("length", declaredLength, new SourceRange(node.Range.Offset + 4, 4));
        fields.Derived("<PayloadLength>", Math.Min(declaredLength, (long)reader.Remaining), node.Range);
        fields.Note("Unknown chunk type; the payload is not interpreted.", node.Range);
        var title = $"{RawIdentifier.FormatBytes(id)} '{RawIdentifier.FormatBytes(id)}'";
        return Result(node, fields, title);
    }

    private static NodeParseResult ParseEvent(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = new SourceReader(context.Source, node.Range.Offset, node.Range.Length);
        var runningStatus = TryGetRunningStatus(node.StableId, out var storedStatus) ? storedStatus : 0;
        if (!MidiEventScanner.TryScan(reader, ref runningStatus, out var scan, out var error))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, error, node.Range);
            return Result(node, fields);
        }

        fields.Raw("delta_time", scan.DeltaTime, node.Range);
        if (scan.RunningStatus)
        {
            fields.Derived("<RunningStatus>", "yes", node.Range);
        }

        if (scan.IsMeta)
        {
            fields.Raw("status", "0xFF", node.Range);
            fields.RawHex("meta_type", scan.MetaType, 2, node.Range);
            fields.Derived("<MetaEvent>", MidiFormatting.FormatMetaEventDescription(scan.MetaType), node.Range);
            ParseMetaEvent(context, fields, scan);
            var title = $"{scan.Name} '0xFF {scan.MetaType:X2}'";
            return Result(node, fields, title);
        }

        if (scan.IsSysEx)
        {
            fields.Raw("status", $"0x{scan.Status:X2}", node.Range);
            fields.Raw("length", scan.DataLength, node.Range);
            fields.Derived("<PayloadLength>", scan.DataLength, node.Range);
            fields.Note("SysEx payload is not interpreted; the complete bytes remain available in Hex.", node.Range);
            return Result(node, fields, $"{scan.Name} '0x{scan.Status:X2}'");
        }

        fields.Raw("status", $"0x{scan.Status:X2}", node.Range);
        if (scan.Status is >= 0x80 and <= 0xEF)
        {
            ParseChannelEvent(context, fields, scan);
        }
        else
        {
            ParseSystemEvent(context, fields, scan);
        }

        return Result(node, fields, $"{scan.Name} '0x{scan.Status:X2}'");
    }

    private static void ParseChannelEvent(ParseContext context, FieldList fields, MidiEventScan scan)
    {
        fields.Derived("<EventType>", MidiFormatting.FormatChannelEventDescription(scan.Status), scanRange(scan));
        fields.Derived("<Channel>", (scan.Status & 0x0F) + 1, scanRange(scan));

        Span<byte> data = stackalloc byte[2];
        var count = checked((int)scan.DataLength);
        var read = SourceRead.ReadAtMost(context.Source, scan.DataOffset, data[..count]);
        if (read < count)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"The event declares {count} data byte(s) but only {read} could be read.",
                scanRange(scan));
            return;
        }

        switch (scan.Status & 0xF0)
        {
            case 0x80:
                fields.Raw("note", (int)data[0], scanRange(scan));
                fields.Raw("velocity", (int)data[1], scanRange(scan));
                fields.Derived("<NoteName>", MidiFormatting.FormatNote(data[0]), scanRange(scan));
                break;
            case 0x90:
                fields.Raw("note", (int)data[0], scanRange(scan));
                fields.Raw("velocity", (int)data[1], scanRange(scan));
                fields.Derived("<NoteName>", MidiFormatting.FormatNote(data[0]), scanRange(scan));
                if ((int)data[1] == 0)
                {
                    fields.Note("A Note On event with velocity 0 conventionally means Note Off.", scanRange(scan));
                }

                break;
            case 0xA0:
                fields.Raw("note", (int)data[0], scanRange(scan));
                fields.Raw("pressure", (int)data[1], scanRange(scan));
                fields.Derived("<NoteName>", MidiFormatting.FormatNote(data[0]), scanRange(scan));
                break;
            case 0xB0:
                fields.Raw("controller", (int)data[0], scanRange(scan));
                fields.Raw("value", (int)data[1], scanRange(scan));
                fields.Derived("<ControllerName>", MidiFormatting.FormatController(data[0]), scanRange(scan));
                break;
            case 0xC0:
                fields.Raw("program", (int)data[0], scanRange(scan));
                break;
            case 0xD0:
                fields.Raw("pressure", (int)data[0], scanRange(scan));
                break;
            case 0xE0:
                fields.Raw("value_lsb", (int)data[0], scanRange(scan));
                fields.Raw("value_msb", (int)data[1], scanRange(scan));
                var bend = (((int)data[1] << 7) | (int)data[0]) - 8192;
                fields.Derived("<PitchBend>", bend, scanRange(scan));
                break;
            default:
                fields.Note("Undefined channel event.", scanRange(scan));
                break;
        }
    }

    private static void ParseSystemEvent(ParseContext context, FieldList fields, MidiEventScan scan)
    {
        fields.Derived("<EventType>", MidiFormatting.FormatSystemEventDescription(scan.Status), scanRange(scan));
        if (scan.DataLength == 0)
        {
            return;
        }

        Span<byte> data = stackalloc byte[2];
        var count = checked((int)Math.Min(scan.DataLength, 2));
        var read = SourceRead.ReadAtMost(context.Source, scan.DataOffset, data[..count]);
        if (read < count)
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The system event data is truncated.", scanRange(scan));
            return;
        }

        switch (scan.Status)
        {
            case 0xF1:
                fields.Raw("data", (int)data[0], scanRange(scan));
                break;
            case 0xF2:
                fields.Raw("value_lsb", (int)data[0], scanRange(scan));
                fields.Raw("value_msb", (int)data[1], scanRange(scan));
                fields.Derived("<SongPosition>", ((int)data[1] << 7) | (int)data[0], scanRange(scan));
                break;
            case 0xF3:
                fields.Raw("song", (int)data[0], scanRange(scan));
                break;
            default:
                fields.Derived("<DataLength>", scan.DataLength, scanRange(scan));
                break;
        }
    }

    private static void ParseMetaEvent(ParseContext context, FieldList fields, MidiEventScan scan)
    {
        fields.Raw("length", scan.DataLength, scanRange(scan));
        if (MidiConstants.IsTextMetaType(scan.MetaType))
        {
            ParseTextMeta(context, fields, scan);
            return;
        }

        switch (scan.MetaType)
        {
            case MidiConstants.SequenceNumberMetaType:
                if (scan.DataLength >= 2)
                {
                    Span<byte> value = stackalloc byte[2];
                    if (SourceRead.ReadAtMost(context.Source, scan.DataOffset, value) == 2)
                    {
                        fields.Raw("sequence_number", BinaryPrimitives.ReadUInt16BigEndian(value), scanRange(scan));
                    }
                }
                else
                {
                    fields.Note("The sequence number is omitted; the default is the track order.", scanRange(scan));
                }

                break;
            case MidiConstants.ChannelPrefixMetaType:
                if (scan.DataLength >= 1)
                {
                    Span<byte> value = stackalloc byte[1];
                    if (SourceRead.ReadAtMost(context.Source, scan.DataOffset, value) == 1)
                    {
                        fields.Raw("channel", (int)value[0], scanRange(scan));
                    }
                }

                break;
            case MidiConstants.MidiPortMetaType:
                if (scan.DataLength >= 1)
                {
                    Span<byte> value = stackalloc byte[1];
                    if (SourceRead.ReadAtMost(context.Source, scan.DataOffset, value) == 1)
                    {
                        fields.Raw("port", (int)value[0], scanRange(scan));
                    }
                }

                break;
            case MidiConstants.EndOfTrackMetaType:
                if (scan.DataLength != 0)
                {
                    fields.Warning(
                        DiagnosticCodes.SchemaMismatch,
                        $"End of Track declares {scan.DataLength} data byte(s); the specification requires zero.",
                        scanRange(scan));
                }

                break;
            case MidiConstants.SetTempoMetaType:
                ParseTempo(context, fields, scan);
                break;
            case MidiConstants.SmpteOffsetMetaType:
                ParseSmpteOffset(context, fields, scan);
                break;
            case MidiConstants.TimeSignatureMetaType:
                ParseTimeSignature(context, fields, scan);
                break;
            case MidiConstants.KeySignatureMetaType:
                ParseKeySignature(context, fields, scan);
                break;
            case MidiConstants.SequencerSpecificMetaType:
                fields.Note("Sequencer-specific payload is not interpreted.", scanRange(scan));
                break;
            default:
                fields.Note("Meta event data is not interpreted in this version.", scanRange(scan));
                break;
        }
    }

    private static void ParseTextMeta(ParseContext context, FieldList fields, MidiEventScan scan)
    {
        var count = (int)Math.Min(scan.DataLength, context.Limits.MaxStringBytes);
        var bytes = new byte[count];
        var read = SourceRead.ReadAtMost(context.Source, scan.DataOffset, bytes);
        if (read < count)
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The meta event text is truncated.", scanRange(scan));
            return;
        }

        fields.Raw("text", MidiFormatting.FormatText(bytes.AsSpan(0, read), Encoding.Latin1), scanRange(scan));
        fields.Derived("<TextByteLength>", scan.DataLength, scanRange(scan));
        if (scan.DataLength > context.Limits.MaxStringBytes)
        {
            fields.Warning(
                DiagnosticCodes.LimitString,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The text is {scan.DataLength} byte(s); only the first {context.Limits.MaxStringBytes} are shown."),
                scanRange(scan));
        }
    }

    private static void ParseTempo(ParseContext context, FieldList fields, MidiEventScan scan)
    {
        if (scan.DataLength < 3)
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "Set Tempo needs three data bytes.", scanRange(scan));
            return;
        }

        Span<byte> value = stackalloc byte[3];
        if (SourceRead.ReadAtMost(context.Source, scan.DataOffset, value) != 3)
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "Set Tempo data is truncated.", scanRange(scan));
            return;
        }

        var microseconds = (uint)(((int)value[0] << 16) | ((int)value[1] << 8) | (int)value[2]);
        fields.Raw("microseconds_per_quarter_note", microseconds, scanRange(scan));
        fields.Derived("<TempoBPM>", MidiFormatting.FormatTempoBpm(microseconds), scanRange(scan));
    }

    private static void ParseSmpteOffset(ParseContext context, FieldList fields, MidiEventScan scan)
    {
        if (scan.DataLength < 5)
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "SMPTE Offset needs five data bytes.", scanRange(scan));
            return;
        }

        Span<byte> value = stackalloc byte[5];
        if (SourceRead.ReadAtMost(context.Source, scan.DataOffset, value) != 5)
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "SMPTE Offset data is truncated.", scanRange(scan));
            return;
        }

        fields.Raw("hour", (int)value[0], scanRange(scan));
        fields.Raw("minute", (int)value[1], scanRange(scan));
        fields.Raw("second", (int)value[2], scanRange(scan));
        fields.Raw("frame", (int)value[3], scanRange(scan));
        fields.Raw("subframe", (int)value[4], scanRange(scan));
        fields.Derived(
            "<SmpteOffset>",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)value[0]:X2}:{(int)value[1]:X2}:{(int)value[2]:X2}:{(int)value[3]:X2}.{(int)value[4]:X2}"),
            scanRange(scan));
    }

    private static void ParseTimeSignature(ParseContext context, FieldList fields, MidiEventScan scan)
    {
        if (scan.DataLength < 4)
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "Time Signature needs four data bytes.", scanRange(scan));
            return;
        }

        Span<byte> value = stackalloc byte[4];
        if (SourceRead.ReadAtMost(context.Source, scan.DataOffset, value) != 4)
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "Time Signature data is truncated.", scanRange(scan));
            return;
        }

        fields.Raw("numerator", (int)value[0], scanRange(scan));
        fields.Raw("denominator", (int)value[1], scanRange(scan));
        fields.Raw("clocks_per_click", (int)value[2], scanRange(scan));
        fields.Raw("thirty_seconds_per_quarter", (int)value[3], scanRange(scan));
        fields.Derived("<TimeSignature>", MidiFormatting.FormatTimeSignature(value[0], value[1]), scanRange(scan));
    }

    private static void ParseKeySignature(ParseContext context, FieldList fields, MidiEventScan scan)
    {
        if (scan.DataLength < 2)
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "Key Signature needs two data bytes.", scanRange(scan));
            return;
        }

        Span<byte> value = stackalloc byte[2];
        if (SourceRead.ReadAtMost(context.Source, scan.DataOffset, value) != 2)
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "Key Signature data is truncated.", scanRange(scan));
            return;
        }

        fields.Raw("sf", (int)value[0], scanRange(scan));
        fields.Raw("mi", (int)value[1], scanRange(scan));
        fields.Derived("<KeySignature>", MidiFormatting.FormatKeySignature(value[0], value[1]), scanRange(scan));
        if ((int)value[1] > 1)
        {
            fields.Warning(DiagnosticCodes.SchemaMismatch, $"Key Signature mode {(int)value[1]} is not defined.", scanRange(scan));
        }
    }

    private static bool TryGetRunningStatus(string? stableId, out int status)
    {
        status = 0;
        if (stableId is null)
        {
            return false;
        }

        var parts = stableId.Split(':');
        if (parts.Length < 5 || parts[^1] != "1")
        {
            return false;
        }

        if (!byte.TryParse(parts[^2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        status = parsed;
        return true;
    }

    private static SourceRange scanRange(MidiEventScan scan) => SourceRange.FromBounds(scan.Start, scan.End);

    private static NodeParseResult Result(FormatNode node, FieldList fields, string? title = null) =>
        new(
            title ?? node.Name,
            node.Range,
            node.Range,
            fields.ToImmutable(),
            ImmutableArray.CreateRange(fields.Diagnostics));
}
