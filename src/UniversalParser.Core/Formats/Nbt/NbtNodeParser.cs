using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Embedded;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Nbt;

/// <summary>
/// Re-reads one NBT tag on demand. Tree names are type names only; the tag name and list index
/// are reported here as the title and raw fields.
/// </summary>
internal static class NbtNodeParser
{
    public static NodeParseResult Parse(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        if (node.StableId == NbtConstants.RootStableId)
        {
            return ParseRoot(context, node);
        }

        if (node.StableId == "nbt:trailing")
        {
            return ParseSynthetic(node, "Trailing bytes after the root NBT tag are not interpreted.");
        }

        if (node.StableId?.StartsWith("nbt:unparsed:", StringComparison.Ordinal) == true
            || node.StableId is null)
        {
            return ParseSynthetic(node, "This range is not a decoded NBT tag.");
        }

        if (TryParseElementStableId(node.StableId, out var elementType, out var index))
        {
            return ParseElement(context, node, elementType, index);
        }

        if (node.StableId.StartsWith("nbt:tag:", StringComparison.Ordinal))
        {
            if (!NbtRead.TryReadTagHeader(context.Source, node.Range.Offset, node.Range.End, out var header, out var failure))
            {
                var fields = context.CreateFieldList();
                fields.Warning(NbtRead.GetFailureCode(failure), NbtRead.GetFailureMessage(failure), node.Range);
                return Result(node, node.Name, fields);
            }

            return ParseNamedTag(context, node, header);
        }

        return ParseSynthetic(node, "This range is not a decoded NBT tag.");
    }

    private static NodeParseResult ParseRoot(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        fields.Derived("<FileSize>", node.Range.Length, node.Range);
        fields.Derived("<ChildCount>", node.Children.Length, node.Range);

        if (node.Children.Length > 0
            && node.Children[0].StableId?.StartsWith("nbt:tag:", StringComparison.Ordinal) == true
            && NbtRead.TryReadByte(context.Source, node.Children[0].Range.Offset, node.Range.End, out var rootType))
        {
            fields.Derived("<RootTagType>", NbtConstants.FormatTagType(rootType), node.Children[0].Range);
        }

        fields.Note(
            "Uncompressed NBT only; gzip/zlib-compressed NBT and MCA region files are treated as Raw Data.",
            node.Range);
        return Result(node, NbtConstants.DisplayName, fields);
    }

    private static NodeParseResult ParseNamedTag(ParseContext context, FormatNode node, NbtTagHeader header)
    {
        var fields = context.CreateFieldList();
        fields.Raw("type", NbtConstants.FormatTagType(header.Type), new SourceRange(header.Start, 1));
        fields.Raw("name", header.Name, new SourceRange(header.NameStart, header.NameLength));
        if (header.NameInvalid)
        {
            fields.Warning(
                DiagnosticCodes.InvalidEncoding,
                "The NBT tag name is not valid UTF-8.",
                new SourceRange(header.NameStart, header.NameLength));
        }

        AddPayloadFields(context, fields, node, header.Type, header.PayloadStart);
        return Result(node, NbtConstants.FormatNamedTitle(header.Type, header.Name), fields);
    }

    private static NodeParseResult ParseElement(ParseContext context, FormatNode node, byte type, long index)
    {
        var fields = context.CreateFieldList();
        fields.Derived("<ElementType>", NbtConstants.FormatTagType(type), node.Range);
        fields.Derived("<Index>", index, node.Range);
        AddPayloadFields(context, fields, node, type, node.Range.Offset);
        return Result(node, NbtConstants.FormatElementTitle(type, index), fields);
    }

    private static void AddPayloadFields(
        ParseContext context,
        FieldList fields,
        FormatNode node,
        byte type,
        long payloadStart)
    {
        if (type == NbtConstants.CompoundType)
        {
            AddCompoundFields(fields, node);
            return;
        }

        var reader = new BlockReader(context.Source, payloadStart, node.Range.End - payloadStart);
        switch (type)
        {
            case NbtConstants.ByteType:
                {
                    if (!reader.TryByte(out var raw))
                    {
                        AddTruncated(fields, node, "TAG_Byte");
                        return;
                    }

                    var value = unchecked((sbyte)raw);
                    fields.Raw("value", value.ToString(CultureInfo.InvariantCulture), reader.LastRange);
                    if (value < 0)
                    {
                        fields.Derived("<UnsignedValue>", raw, reader.LastRange);
                    }

                    break;
                }

            case NbtConstants.ShortType:
                {
                    if (!reader.TryUInt16BE(out var raw))
                    {
                        AddTruncated(fields, node, "TAG_Short");
                        return;
                    }

                    var value = unchecked((short)raw);
                    fields.Raw("value", value.ToString(CultureInfo.InvariantCulture), reader.LastRange);
                    if (value < 0)
                    {
                        fields.Derived("<UnsignedValue>", raw, reader.LastRange);
                    }

                    break;
                }

            case NbtConstants.IntType:
                {
                    if (!reader.TryUInt32BE(out var raw))
                    {
                        AddTruncated(fields, node, "TAG_Int");
                        return;
                    }

                    var value = unchecked((int)raw);
                    fields.Raw("value", value.ToString(CultureInfo.InvariantCulture), reader.LastRange);
                    if (value < 0)
                    {
                        fields.Derived("<UnsignedValue>", raw, reader.LastRange);
                    }

                    break;
                }

            case NbtConstants.LongType:
                {
                    if (!reader.TryUInt64BE(out var raw))
                    {
                        AddTruncated(fields, node, "TAG_Long");
                        return;
                    }

                    var value = unchecked((long)raw);
                    fields.Raw("value", value.ToString(CultureInfo.InvariantCulture), reader.LastRange);
                    if (value < 0)
                    {
                        fields.Derived("<UnsignedValue>", raw, reader.LastRange);
                    }

                    break;
                }

            case NbtConstants.FloatType:
                {
                    if (!reader.TryUInt32BE(out var raw))
                    {
                        AddTruncated(fields, node, "TAG_Float");
                        return;
                    }

                    var value = BitConverter.Int32BitsToSingle(unchecked((int)raw));
                    fields.Raw("value", FormatFloat(value), reader.LastRange);
                    fields.Derived(
                        "<RawBits>",
                        string.Create(CultureInfo.InvariantCulture, $"0x{raw:X8}"),
                        reader.LastRange);
                    break;
                }

            case NbtConstants.DoubleType:
                {
                    if (!reader.TryUInt64BE(out var raw))
                    {
                        AddTruncated(fields, node, "TAG_Double");
                        return;
                    }

                    var value = BitConverter.Int64BitsToDouble(unchecked((long)raw));
                    fields.Raw("value", FormatDouble(value), reader.LastRange);
                    fields.Derived(
                        "<RawBits>",
                        string.Create(CultureInfo.InvariantCulture, $"0x{raw:X16}"),
                        reader.LastRange);
                    break;
                }

            case NbtConstants.StringType:
                AddString(context, fields, reader, node);
                break;

            case NbtConstants.ByteArrayType:
                AddByteArray(fields, reader, node);
                break;

            case NbtConstants.IntArrayType:
                AddNumericArray(fields, reader, node, elementSize: 4, isLong: false);
                break;

            case NbtConstants.LongArrayType:
                AddNumericArray(fields, reader, node, elementSize: 8, isLong: true);
                break;

            case NbtConstants.ListType:
                AddList(fields, reader, node);
                break;

            default:
                fields.Warning(DiagnosticCodes.InvalidTagType, "The NBT tag type is not supported.", node.Range);
                break;
        }
    }

    private static void AddCompoundFields(FieldList fields, FormatNode node)
    {
        fields.Derived("<PayloadLength>", node.PayloadRange?.Length ?? 0, node.PayloadRange ?? node.Range);
        fields.Derived("<ChildCount>", node.Children.Length, node.Range);
        var endTagPresent = node.PayloadRange is { } payload && payload.End < node.Range.End;
        fields.Derived("<EndTagPresent>", endTagPresent ? "true" : "false", node.Range);
        if (!endTagPresent)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedChild,
                "TAG_Compound has no TAG_End before the end of its range.",
                node.Range);
        }
    }

    private static void AddString(
        ParseContext context,
        FieldList fields,
        BlockReader reader,
        FormatNode node)
    {
        if (!reader.TryUInt16BE(out var length))
        {
            AddTruncated(fields, node, "TAG_String length");
            return;
        }

        fields.Raw("length", length, reader.LastRange);
        if (length > reader.Remaining)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The TAG_String payload is truncated.",
                node.Range);
            return;
        }

        var count = (int)Math.Min(length, context.Limits.MaxStringBytes);
        var bytes = new byte[count];
        var dataStart = reader.Position;
        if (!reader.TryReadBytes(bytes))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The TAG_String payload could not be read.", node.Range);
            return;
        }

        reader.Skip(length - count);
        var text = TextDecoding.Utf8(bytes, complete: count == length, out var invalidEncoding);
        fields.Raw(
            "value",
            invalidEncoding ? TextDecoding.Hex(bytes) : TextEscaping.Escape(text),
            new SourceRange(dataStart, count));
        if (count < length)
        {
            fields.Warning(DiagnosticCodes.LimitString, "The TAG_String value is shown as a prefix.", node.Range);
        }

        if (invalidEncoding)
        {
            fields.Warning(DiagnosticCodes.InvalidEncoding, "The TAG_String value is not valid UTF-8.", node.Range);
        }
    }

    private static void AddByteArray(FieldList fields, BlockReader reader, FormatNode node)
    {
        if (!reader.TryUInt32BE(out var rawLength))
        {
            AddTruncated(fields, node, "TAG_Byte_Array length");
            return;
        }

        var length = unchecked((int)rawLength);
        fields.Raw("length", length, reader.LastRange);
        if (length < 0)
        {
            fields.Warning(DiagnosticCodes.InvalidLength, "TAG_Byte_Array declares a negative length.", node.Range);
            return;
        }

        if (length > reader.Remaining)
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The TAG_Byte_Array payload is truncated.", node.Range);
        }

        var previewLength = (int)Math.Min(length, Math.Min(reader.Remaining, NbtConstants.ByteArrayPreviewBytes));
        if (previewLength == 0)
        {
            return;
        }

        Span<byte> preview = stackalloc byte[NbtConstants.ByteArrayPreviewBytes];
        if (!reader.TryReadBytes(preview[..previewLength]))
        {
            return;
        }

        fields.Derived(
            "<HexPreview>",
            HexFormatting.Format(preview[..previewLength], NbtConstants.ByteArrayPreviewBytes),
            reader.LastRange);
    }

    private static void AddNumericArray(
        FieldList fields,
        BlockReader reader,
        FormatNode node,
        int elementSize,
        bool isLong)
    {
        if (!reader.TryUInt32BE(out var rawLength))
        {
            AddTruncated(fields, node, isLong ? "TAG_Long_Array length" : "TAG_Int_Array length");
            return;
        }

        var length = unchecked((int)rawLength);
        fields.Raw("length", length, reader.LastRange);
        if (length < 0)
        {
            fields.Warning(
                DiagnosticCodes.InvalidLength,
                isLong ? "TAG_Long_Array declares a negative length." : "TAG_Int_Array declares a negative length.",
                node.Range);
            return;
        }

        if ((long)length * elementSize > reader.Remaining)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                isLong ? "The TAG_Long_Array payload is truncated." : "The TAG_Int_Array payload is truncated.",
                node.Range);
        }

        var available = (int)Math.Min(length, reader.Remaining / elementSize);
        var previewCount = Math.Min(available, NbtConstants.NumericArrayPreviewCount);
        var values = new List<string>(previewCount);
        for (var index = 0; index < previewCount; index++)
        {
            if (isLong)
            {
                if (!reader.TryUInt64BE(out var raw)) break;
                values.Add(unchecked((long)raw).ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                if (!reader.TryUInt32BE(out var raw)) break;
                values.Add(unchecked((int)raw).ToString(CultureInfo.InvariantCulture));
            }
        }

        if (values.Count > 0)
        {
            var suffix = length > values.Count ? ", ..." : string.Empty;
            fields.Derived("<PreviewValues>", string.Join(", ", values) + suffix, node.Range);
        }
    }

    private static void AddList(FieldList fields, BlockReader reader, FormatNode node)
    {
        if (!reader.TryByte(out var elementType))
        {
            AddTruncated(fields, node, "TAG_List element type");
            return;
        }

        fields.Raw("element_type", NbtConstants.FormatTagType(elementType), reader.LastRange);
        if (!reader.TryUInt32BE(out var rawLength))
        {
            AddTruncated(fields, node, "TAG_List length");
            return;
        }

        var length = unchecked((int)rawLength);
        fields.Raw("length", length, reader.LastRange);
        fields.Derived("<ChildCount>", node.Children.Length, node.Range);
        if (length < 0)
        {
            fields.Warning(DiagnosticCodes.InvalidLength, "TAG_List declares a negative length.", node.Range);
        }
        else if (elementType == NbtConstants.EndType && length == 0)
        {
            fields.Note("Empty TAG_List.", node.Range);
        }
        else if (elementType > NbtConstants.MaxTagType || elementType == NbtConstants.EndType)
        {
            fields.Warning(DiagnosticCodes.InvalidTagType, "TAG_List has an invalid element type.", node.Range);
        }
    }

    private static void AddTruncated(FieldList fields, FormatNode node, string what)
    {
        fields.Warning(DiagnosticCodes.TruncatedPayload, $"{what} is truncated.", node.Range);
    }

    private static string FormatFloat(float value) => ValueFormat.FloatingPoint(value, "G9");

    private static string FormatDouble(double value) => ValueFormat.FloatingPoint(value, "G17");

    private static bool TryParseElementStableId(string? stableId, out byte type, out long index)
    {
        type = 0;
        index = 0;
        if (stableId is null)
        {
            return false;
        }

        var parts = stableId.Split(':');
        if (parts.Length != 5
            || !string.Equals(parts[0], "nbt", StringComparison.Ordinal)
            || !string.Equals(parts[1], "element", StringComparison.Ordinal)
            || !byte.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out type)
            || !long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
        {
            return false;
        }

        return NbtConstants.IsValidTagType(type);
    }

    private static NodeParseResult ParseSynthetic(FormatNode node, string note)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<Length>", node.Range.Length, node.Range);
        fields.Note(note, node.Range);
        return Result(node, node.Name, fields);
    }

    private static NodeParseResult Result(FormatNode node, string title, FieldList fields) =>
        new(
            title,
            node.Range,
            node.Range,
            fields.ToImmutable(),
            [.. fields.Diagnostics]);
}
