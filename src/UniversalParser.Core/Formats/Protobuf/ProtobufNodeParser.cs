using System.Buffers.Binary;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Protobuf;

/// <summary>
/// Re-reads one protobuf field on demand. Field number and wire type are preserved exactly;
/// length-delimited payloads also report whether they look like text, a nested message or
/// opaque bytes. No .proto-derived names or semantics are invented.
/// </summary>
internal static class ProtobufNodeParser
{
    public static NodeParseResult Parse(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        if (node.StableId == ProtobufConstants.RootStableId)
        {
            return ParseRoot(node);
        }

        if (node.Name.StartsWith('<'))
        {
            return ParseSynthetic(node);
        }

        if (node.StableId?.StartsWith("protobuf:", StringComparison.Ordinal) != true
            || !ProtobufRead.TryReadFieldHeader(context.Source, node.Range.Offset, node.Range.End, out var header, out var failure))
        {
            return ParseSynthetic(node);
        }

        var fields = context.CreateFieldList();
        AddIdentity(fields, header);

        switch (header.WireType)
        {
            case ProtobufConstants.VarintWireType:
                AddVarint(fields, header);
                break;
            case ProtobufConstants.Fixed64WireType:
                AddFixed(fields, context.Source, header, 8);
                break;
            case ProtobufConstants.Fixed32WireType:
                AddFixed(fields, context.Source, header, 4);
                break;
            case ProtobufConstants.LengthDelimitedWireType:
                AddLengthDelimited(context, fields, node, header);
                break;
            case ProtobufConstants.StartGroupWireType:
                AddGroup(fields, node, header);
                break;
            default:
                fields.Warning(
                    DiagnosticCodes.InvalidWireType,
                    ProtobufRead.GetFailureMessage(failure),
                    node.Range);
                break;
        }

        return Result(node, GetTitle(header), fields);
    }

    private static NodeParseResult ParseRoot(FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<FileSize>", node.Range.Length, node.Range);
        fields.Derived("<TopLevelFieldCount>", node.Children.Length, node.Range);
        fields.Note(
            "No .proto descriptor is loaded; field numbers, wire types and raw encoded values are preserved.",
            node.Range);
        return new NodeParseResult(
            node.Name,
            node.Range,
            node.Range,
            fields.ToImmutable(),
            [.. fields.Diagnostics]);
    }

    private static NodeParseResult ParseSynthetic(FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<Length>", node.Range.Length, node.Range);
        fields.Note("This range is not a decoded protobuf field.", node.Range);
        return new NodeParseResult(
            node.Name,
            node.Range,
            node.Range,
            fields.ToImmutable(),
            [.. fields.Diagnostics]);
    }

    private static void AddIdentity(FieldList fields, ProtobufFieldHeader header)
    {
        fields.Raw("field_number", header.FieldNumber, header.KeyRange);
        fields.Raw(
            "wire_type",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{header.WireType} ({ProtobufConstants.GetWireTypeName(header.WireType)})"),
            header.KeyRange);
        fields.Raw(
            "key",
            string.Create(CultureInfo.InvariantCulture, $"0x{header.Key:X2}"),
            header.KeyRange);
    }

    private static void AddVarint(FieldList fields, ProtobufFieldHeader header)
    {
        fields.Raw("value", header.VarintValue, header.ValueRange);
        if (header.VarintValue > long.MaxValue)
        {
            fields.Derived(
                "<SignedValue>",
                unchecked((long)header.VarintValue).ToString(CultureInfo.InvariantCulture),
                header.ValueRange);
        }

        // Without a schema this is an alternative sint32/sint64 interpretation, not the raw value.
        var zigZag = (long)(header.VarintValue >> 1) ^ -(long)(header.VarintValue & 1);
        fields.Derived("<ZigZagValue>", zigZag, header.ValueRange);
    }

    private static void AddFixed(
        FieldList fields,
        IRandomAccessSource source,
        ProtobufFieldHeader header,
        int byteCount)
    {
        Span<byte> buffer = stackalloc byte[8];
        var read = SourceRead.ReadAtMost(source, header.ValueStart, buffer[..byteCount]);
        if (read < byteCount)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"The fixed{byteCount * 8} value is truncated.",
                header.ValueRange);
            return;
        }

        var unsigned = byteCount == 8
            ? BinaryPrimitives.ReadUInt64LittleEndian(buffer)
            : BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        if (byteCount == 8)
        {
            fields.Raw(
                "value",
                string.Create(CultureInfo.InvariantCulture, $"0x{unsigned:X16}"),
                header.ValueRange);
            fields.Derived("<UnsignedValue>", unsigned, header.ValueRange);
            if (unsigned > long.MaxValue)
            {
                fields.Derived(
                    "<SignedValue>",
                    unchecked((long)unsigned).ToString(CultureInfo.InvariantCulture),
                    header.ValueRange);
            }

            fields.Derived(
                "<DoubleValue>",
                ValueFormat.FloatingPoint(BinaryPrimitives.ReadDoubleLittleEndian(buffer)),
                header.ValueRange);
            return;
        }

        var value32 = (uint)unsigned;
        fields.Raw(
            "value",
            string.Create(CultureInfo.InvariantCulture, $"0x{value32:X8}"),
            header.ValueRange);
        fields.Derived("<UnsignedValue>", value32, header.ValueRange);
        fields.Derived(
            "<FloatValue>",
            ValueFormat.FloatingPoint(BinaryPrimitives.ReadSingleLittleEndian(buffer)),
            header.ValueRange);
    }

    private static void AddLengthDelimited(
        ParseContext context,
        FieldList fields,
        FormatNode node,
        ProtobufFieldHeader header)
    {
        fields.Raw("length", header.DeclaredLength, header.LengthRange);
        if (header.Truncated)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The length-delimited payload extends beyond the enclosing range.",
                node.Range);
            return;
        }

        var payloadRange = SourceRange.FromBounds(header.ValueStart, header.ValueEnd);
        var payloadKind = node.Kind == NodeKind.Container
            ? ProtobufPayloadKind.Message
            : payloadRange.IsEmpty
                ? ProtobufPayloadKind.Empty
                : ProtobufPayloadClassifier.Classify(
                    context.Source,
                    payloadRange.Offset,
                    payloadRange.Length,
                    context.CancellationToken);
        fields.Derived("<PayloadKind>", GetPayloadKindName(payloadKind), payloadRange);

        switch (payloadKind)
        {
            case ProtobufPayloadKind.Empty:
                fields.Note("The length-delimited payload is empty.", payloadRange);
                break;

            case ProtobufPayloadKind.Message:
                fields.Derived("<FieldCount>", node.Children.Length, payloadRange);
                fields.Note(
                    "Payload parses as a nested message; field numbers and wire types are preserved.",
                    payloadRange);
                break;

            case ProtobufPayloadKind.Text:
                AddText(context, fields, payloadRange);
                break;

            default:
                AddHexPreview(context, fields, payloadRange);
                break;
        }
    }

    private static void AddGroup(FieldList fields, FormatNode node, ProtobufFieldHeader header)
    {
        if (node.PayloadRange is not { } payloadRange)
        {
            fields.Derived("<PayloadLength>", 0, node.Range);
            return;
        }

        fields.Derived("<PayloadLength>", payloadRange.Length, payloadRange);
        if (payloadRange.End < node.Range.End)
        {
            fields.Raw(
                "end_group_field_number",
                header.FieldNumber,
                SourceRange.FromBounds(payloadRange.End, node.Range.End));
        }
        else
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The start-group field has no matching end-group key.",
                node.Range);
        }

        if (node.Children.Length > 0)
        {
            fields.Note("Group fields are child nodes.", payloadRange);
        }
    }

    private static void AddText(ParseContext context, FieldList fields, SourceRange payloadRange)
    {
        var byteCount = (int)Math.Min(payloadRange.Length, context.Limits.MaxStringBytes);
        var bytes = new byte[byteCount];
        var read = SourceRead.ReadAtMost(context.Source, payloadRange.Offset, bytes);
        var text = TextDecoding.Utf8(bytes.AsSpan(0, read), complete: read == payloadRange.Length, out var invalidEncoding);
        fields.Derived(
            "<Text>",
            invalidEncoding ? TextDecoding.Hex(bytes.AsSpan(0, read)) : TextEscaping.Escape(text),
            new SourceRange(payloadRange.Offset, read));

        if (read < payloadRange.Length)
        {
            fields.Warning(
                DiagnosticCodes.LimitString,
                "The text payload is shown as a prefix.",
                payloadRange);
        }

        if (invalidEncoding)
        {
            fields.Warning(
                DiagnosticCodes.InvalidEncoding,
                "The text payload is not valid UTF-8.",
                payloadRange);
        }
    }

    private static void AddHexPreview(ParseContext context, FieldList fields, SourceRange payloadRange)
    {
        Span<byte> buffer = stackalloc byte[ProtobufConstants.HexPreviewBytes];
        var read = SourceRead.ReadAtMost(context.Source, payloadRange.Offset, buffer);
        fields.Derived(
            "<HexPreview>",
            HexFormatting.Format(buffer[..read], ProtobufConstants.HexPreviewBytes),
            new SourceRange(payloadRange.Offset, read));
    }

    private static string GetPayloadKindName(ProtobufPayloadKind payloadKind) => payloadKind switch
    {
        ProtobufPayloadKind.Empty => "empty",
        ProtobufPayloadKind.Text => "string",
        ProtobufPayloadKind.Message => "message",
        ProtobufPayloadKind.Bytes => "bytes",
        _ => "none",
    };

    private static string GetTitle(ProtobufFieldHeader header) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{ProtobufConstants.GetFieldTitleName(header.WireType)} '{header.FieldNumber}'");

    private static NodeParseResult Result(
        FormatNode node,
        string title,
        FieldList fields) =>
        new(
            title,
            node.Range,
            node.Range,
            fields.ToImmutable(),
            [.. fields.Diagnostics]);
}
