using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Ebml;

/// <summary>
/// Lazily re-reads one EBML element from the source. The tree parser only stores ranges, so
/// selecting a node never depends on a payload copy made during document parsing.
/// </summary>
internal static class EbmlNodeParser
{
    public static NodeParseResult Parse(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        if (node.StableId == "ebml-root")
        {
            return new NodeParseResult(
                node.Name,
                node.Range,
                node.Range,
                [
                    Field.Derived("<FileSize>", node.Range.Length.ToString(CultureInfo.InvariantCulture), node.Range),
                    Field.Derived("<ChildCount>", node.Children.Length.ToString(CultureInfo.InvariantCulture), node.Range),
                    Field.Note("Synthetic document root; select an element to inspect its fields."),
                ]);
        }

        if (node.Name.StartsWith('<'))
        {
            return new NodeParseResult(
                node.Name,
                node.Range,
                node.Range,
                [
                    Field.Derived("<Length>", node.Range.Length.ToString(CultureInfo.InvariantCulture), node.Range),
                    Field.Note("This synthetic range is not a parsed EBML element."),
                ]);
        }

        var expectedId = ResolveId(node);
        if (expectedId is null)
        {
            return Failure(node, DiagnosticCodes.InvalidSignature, "The EBML element ID could not be resolved from the node identity.");
        }

        if (!EbmlRead.TryReadHeader(context.Source, node.Range.Offset, node.Range.End, out var header, out var failure))
        {
            var (code, message) = failure switch
            {
                EbmlHeaderFailure.InvalidVint => (DiagnosticCodes.InvalidVint, "The EBML element header contains an invalid VINT."),
                EbmlHeaderFailure.LengthOverflow => (DiagnosticCodes.LengthOverflow, "The EBML element size overflows Int64."),
                _ => (DiagnosticCodes.TruncatedHeader, "The EBML element header is truncated."),
            };
            return Failure(node, code, message);
        }

        var fields = context.CreateFieldList();
        if (header.Id != expectedId.Value)
        {
            fields.Warning(
                DiagnosticCodes.InvalidSignature,
                $"The node identifies element {EbmlFormatting.FormatId(expectedId.Value)} but the source contains {EbmlFormatting.FormatId(header.Id)}.",
                new SourceRange(header.Offset, header.IdLength));
        }

        var payloadStart = header.PayloadOffset;
        if (payloadStart > node.Range.End)
        {
            return Failure(node, DiagnosticCodes.TruncatedHeader, "The EBML element payload starts beyond the node range.");
        }

        var payloadEnd = Math.Min(header.PayloadEnd ?? node.Range.End, node.Range.End);
        if (payloadEnd < payloadStart)
        {
            return Failure(node, DiagnosticCodes.InvalidLength, "The EBML element payload range is inverted.");
        }

        var dataRange = SourceRange.FromBounds(payloadStart, payloadEnd);
        var declaredEnd = header.PayloadEnd;
        if (declaredEnd is { } end && end > node.Range.End)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"The element declares {header.DataSize} payload byte(s) but only {node.Range.End - payloadStart} are available.",
                dataRange);
        }

        if (node.Kind == NodeKind.Unparsed)
        {
            fields.Derived("<ElementDataSize>", header.IsUnknownSize ? "unknown" : header.DataSize!.Value.ToString(CultureInfo.InvariantCulture),
                new SourceRange(header.Offset + header.IdLength, header.SizeLength));
            fields.Derived("<UnparsedLength>", node.Range.Length, node.Range);
            fields.Derived("<PayloadLength>", dataRange.Length, dataRange);
            fields.Note(
                EbmlSchema.IsHighEntropyBlock(header.Id)
                    ? "High-entropy block payload; frame headers and codec bytes are not parsed."
                    : "Unknown element; the entire block is unparsed.",
                node.Range);
            return Result(node, fields);
        }

        if (!EbmlSchema.TryGet(header.Id, out var definition))
        {
            fields.Derived("<ElementDataSize>", header.IsUnknownSize ? "unknown" : header.DataSize!.Value.ToString(CultureInfo.InvariantCulture),
                new SourceRange(header.Offset + header.IdLength, header.SizeLength));
            fields.Derived("<UnparsedLength>", node.Range.Length, node.Range);
            fields.Derived("<PayloadLength>", dataRange.Length, dataRange);
            fields.Note("Element is not present in the schema; the entire block is unparsed.", node.Range);
            return Result(node, fields);
        }

        var name = definition.Name;
        if (header.IsUnknownSize && definition.Type != ElementType.Master)
        {
            fields.Warning(
                DiagnosticCodes.UnknownSize,
                $"{name} has an unknown size but is not a master element; the node boundary is used.",
                dataRange);
        }

        if (header.Id == EbmlSchema.VoidId)
        {
            fields.Derived("<PaddingLength>", dataRange.Length, dataRange);
            fields.Note("Void element; its payload is padding.", dataRange);
            return Result(node, fields);
        }

        switch (definition.Type)
        {
            case ElementType.Master:
                AddMasterFields(fields, header, node, dataRange);
                break;
            case ElementType.UnsignedInteger:
                ParseUnsigned(context, fields, name, dataRange);
                break;
            case ElementType.SignedInteger:
                ParseSigned(context, fields, name, dataRange);
                break;
            case ElementType.Float:
                ParseFloat(context, fields, name, dataRange);
                break;
            case ElementType.String:
            case ElementType.Utf8String:
                ParseString(context, fields, name, dataRange);
                break;
            case ElementType.Date:
                ParseDate(context, fields, name, dataRange);
                break;
            case ElementType.Binary:
                ParseBinary(context, fields, name, dataRange);
                break;
            default:
                fields.Warning(DiagnosticCodes.SchemaMismatch, $"The schema type for element {EbmlFormatting.FormatId(header.Id)} is unknown.", dataRange);
                break;
        }

        return Result(node, fields);
    }

    private static void AddMasterFields(FieldList fields, EbmlElementHeader header, FormatNode node, SourceRange dataRange)
    {
        fields.Derived("<ElementDataSize>", header.IsUnknownSize ? "unknown" : header.DataSize!.Value.ToString(CultureInfo.InvariantCulture),
            new SourceRange(header.Offset + header.IdLength, header.SizeLength));
        fields.Derived("<ChildCount>", node.Children.Length, dataRange);
        if (header.IsUnknownSize)
        {
            fields.Note("Unknown-sized master; its end is determined by the next sibling or the parent boundary.", node.Range);
        }
    }

    private static void ParseUnsigned(ParseContext context, FieldList fields, string name, SourceRange dataRange)
    {
        if (dataRange.Length > 8)
        {
            fields.Error(DiagnosticCodes.InvalidLength, $"{name} is {dataRange.Length} bytes; an EBML unsigned integer is at most 8 bytes.", dataRange);
            return;
        }

        var data = ReadPayload(context, dataRange, 8, fields);
        var value = EbmlFormatting.ReadUnsigned(data);
        fields.Raw(name, value, dataRange);

        if (name.StartsWith("Flag", StringComparison.Ordinal) && value <= 1)
        {
            fields.Derived($"<{name}>", value == 0 ? "false" : "true", dataRange);
        }

        if (name == "TrackType")
        {
            fields.Derived("<TrackType>", EbmlFormatting.TryGetTrackType(value, out var trackType) ? trackType ?? "unknown" : "unknown", dataRange);
        }
    }

    private static void ParseSigned(ParseContext context, FieldList fields, string name, SourceRange dataRange)
    {
        if (dataRange.Length > 8)
        {
            fields.Error(DiagnosticCodes.InvalidLength, $"{name} is {dataRange.Length} bytes; an EBML signed integer is at most 8 bytes.", dataRange);
            return;
        }

        var data = ReadPayload(context, dataRange, 8, fields);
        fields.Raw(name, EbmlFormatting.ReadSigned(data), dataRange);
    }

    private static void ParseFloat(ParseContext context, FieldList fields, string name, SourceRange dataRange)
    {
        if (dataRange.Length is not (0 or 4 or 8))
        {
            fields.Error(DiagnosticCodes.InvalidLength, $"{name} is {dataRange.Length} bytes; an EBML float is 0, 4, or 8 bytes.", dataRange);
            return;
        }

        if (dataRange.Length == 0)
        {
            fields.Raw(name, "0", dataRange);
            return;
        }

        var data = ReadPayload(context, dataRange, 8, fields);
        if (data.Length < dataRange.Length)
        {
            return;
        }

        var value = dataRange.Length == 4
            ? EbmlFormatting.FormatFloat(EbmlFormatting.ReadSingle(data))
            : EbmlFormatting.FormatFloat(EbmlFormatting.ReadDouble(data));
        fields.Raw(name, value, dataRange);
    }

    private static void ParseString(ParseContext context, FieldList fields, string name, SourceRange dataRange)
    {
        var maxBytes = context.Limits.MaxStringBytes;
        var data = ReadPayload(context, dataRange, maxBytes, fields);
        fields.Raw(name, EbmlFormatting.DecodeText(data), dataRange);
        if (dataRange.Length > maxBytes)
        {
            fields.Warning(DiagnosticCodes.LimitString, $"{name} is {dataRange.Length} bytes; only the first {maxBytes} are shown.", dataRange);
            fields.Derived("<UnparsedPayloadLength>", dataRange.Length - data.Length, dataRange);
        }
    }

    private static void ParseDate(ParseContext context, FieldList fields, string name, SourceRange dataRange)
    {
        if (dataRange.Length != 8)
        {
            fields.Error(DiagnosticCodes.InvalidLength, $"{name} is {dataRange.Length} bytes; an EBML date is exactly 8 bytes.", dataRange);
            return;
        }

        var data = ReadPayload(context, dataRange, 8, fields);
        var nanoseconds = EbmlFormatting.ReadSigned(data);
        fields.Raw(name, nanoseconds, dataRange);
        fields.Derived($"<{name}>", EbmlFormatting.FormatDate(nanoseconds), dataRange);
    }

    private static void ParseBinary(ParseContext context, FieldList fields, string name, SourceRange dataRange)
    {
        var maxBytes = (int)Math.Min(EbmlFormatting.MaxInlineBinaryBytes, context.Limits.MaxBlobBytes);
        var data = ReadPayload(context, dataRange, maxBytes, fields);
        fields.Raw(name, EbmlFormatting.FormatHex(data), dataRange);

        if (name == "CRC-32" && dataRange.Length != 4)
        {
            fields.Warning(DiagnosticCodes.InvalidLength, $"CRC-32 is {dataRange.Length} bytes; the specification requires 4 bytes.", dataRange);
        }

        if (dataRange.Length > maxBytes)
        {
            fields.Derived("<UnparsedPayloadLength>", dataRange.Length - data.Length, dataRange);
            fields.Note($"Only the first {maxBytes} byte(s) of {name} are shown inline.", dataRange);
        }
    }

    private static byte[] ReadPayload(ParseContext context, SourceRange range, int maxBytes, FieldList fields)
    {
        var count = (int)Math.Min(range.Length, maxBytes);
        if (count == 0)
        {
            return [];
        }

        var buffer = new byte[count];
        var read = SourceRead.ReadAtMost(context.Source, range.Offset, buffer);
        if (read < count)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"Only {read} of {count} requested payload byte(s) could be read.",
                range);
            return buffer[..read];
        }

        return buffer;
    }

    private static NodeParseResult Result(FormatNode node, FieldList fields) =>
        new(TitleFor(node), node.Range, node.Range, fields.ToImmutable(), ImmutableArray.CreateRange(fields.Diagnostics));

    private static string TitleFor(FormatNode node)
    {
        if (node.StableId == "ebml-root" || node.Name.StartsWith('<'))
        {
            return node.Name;
        }

        var id = ResolveId(node);
        if (id is null)
        {
            return node.Name;
        }

        return EbmlSchema.TryGet(id.Value, out var definition)
            ? $"{definition.Name} '{EbmlFormatting.FormatId(id.Value)}'"
            : $"UnknownElement '{EbmlFormatting.FormatId(id.Value)}'";
    }

    private static NodeParseResult Failure(FormatNode node, string code, string message) =>
        new(
            TitleFor(node),
            node.Range,
            node.Range,
            ImmutableArray.Create(Field.Error(message, range: node.Range)),
            ImmutableArray.Create(new Diagnostic(DiagnosticSeverity.Error, code, message, node.Range)));

    private static ulong? ResolveId(FormatNode node)
    {
        if (node.StableId is { } stable && stable.StartsWith("ebml:", StringComparison.Ordinal))
        {
            var value = stable["ebml:".Length..];
            if (ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            {
                return id;
            }
        }

        var marker = node.Name.IndexOf("'0x", StringComparison.Ordinal);
        if (marker >= 0)
        {
            var end = node.Name.IndexOf('\'', marker + 1);
            if (end > marker + 3
                && ulong.TryParse(node.Name.AsSpan(marker + 3, end - marker - 3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            {
                return id;
            }
        }

        return null;
    }
}
