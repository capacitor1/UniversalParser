using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Embedded;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Jpeg;

/// <summary>
/// Re-reads one JPEG marker or scan range on demand. Marker payloads are read within the segment
/// range; entropy-coded scan data is represented only by its length and is never decoded.
/// </summary>
internal static class JpegNodeParser
{
    public static NodeParseResult Parse(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        if (node.StableId == "jpeg-root")
        {
            return ParseRoot(context, node);
        }

        if (node.StableId?.StartsWith("jpeg:scan:", StringComparison.Ordinal) == true)
        {
            return ParseScan(node);
        }

        if (TryGetMarkerOffset(node.StableId, out var offset))
        {
            return ParseMarker(context, node, offset);
        }

        return Placeholder(node);
    }

    private static NodeParseResult ParseRoot(ParseContext context, FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<FileSize>", node.Range.Length, node.Range);
        fields.Derived(
            "<MarkerCount>",
            node.Children.Count(static child => child.StableId?.StartsWith("jpeg:marker:", StringComparison.Ordinal) == true),
            node.Range);
        fields.Derived(
            "<ScanCount>",
            node.Children.Count(static child => child.StableId?.StartsWith("jpeg:scan:", StringComparison.Ordinal) == true),
            node.Range);
        fields.Note("Synthetic document root; select a marker to inspect its fields.", node.Range);
        _ = context;
        return Result(node, fields);
    }

    private static NodeParseResult ParseScan(FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<EntropyDataLength>", node.Range.Length, node.Range);
        fields.Note("Entropy-coded scan data is not decoded.", node.Range);
        return Result(node, fields, "Scan");
    }

    private static NodeParseResult ParseMarker(ParseContext context, FormatNode node, long offset)
    {
        var fields = context.CreateFieldList();
        if (!JpegMarker.TryRead(context.Source, offset, node.Range.End, out var marker, out var error))
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"The JPEG marker could not be read ({error}).",
                node.Range);
            return Result(node, fields);
        }

        var title = JpegConstants.GetTitle(marker.Code);
        if (marker.FillBytes > 0)
        {
            fields.Derived("<FillBytes>", marker.FillBytes);
        }

        if (!marker.HasLength)
        {
            fields.Note("Standalone marker; it has no length field or payload.", node.Range);
            return Result(node, fields, title);
        }

        fields.Raw("Length", marker.Length, new SourceRange(marker.LengthOffset, 2));
        var payloadRange = new SourceRange(marker.PayloadOffset, marker.PayloadLength);
        fields.Derived("<PayloadLength>", marker.PayloadLength, payloadRange);

        var data = new BigEndianReader(context.Source, marker.PayloadOffset, marker.PayloadLength);
        ParsePayload(context, fields, data, marker.Code, payloadRange);

        return Result(node, fields, title);
    }

    private static void ParsePayload(
        ParseContext context,
        FieldList fields,
        BigEndianReader data,
        byte code,
        SourceRange payloadRange)
    {
        if (JpegConstants.IsApplication(code))
        {
            JpegApplicationDispatcher.Parse(context, fields, payloadRange);
            return;
        }

        if (JpegConstants.IsFrameHeader(code))
        {
            ParseFrameHeader(context, fields, data);
            return;
        }

        switch (code)
        {
            case JpegConstants.Sos:
                ParseScanHeader(context, fields, data);
                break;
            case JpegConstants.Dqt:
                ParseQuantizationTables(context, fields, data);
                break;
            case JpegConstants.Dht:
                ParseHuffmanTables(context, fields, data);
                break;
            case JpegConstants.Dri:
                ParseRestartInterval(fields, data);
                break;
            case JpegConstants.Com:
                ParseComment(context, fields, data);
                break;
            default:
                fields.Note("This marker payload is not interpreted in this version.", payloadRange);
                data.SkipRemaining();
                break;
        }
    }

    private static void ParseFrameHeader(ParseContext context, FieldList fields, BigEndianReader data)
    {
        if (!TryByte(fields, data, "Precision", out var precision)
            || !TryUInt16(fields, data, "Height", out _)
            || !TryUInt16(fields, data, "Width", out _)
            || !TryByte(fields, data, "Number of components", out var componentCount))
        {
            return;
        }

        if (precision != 8 && precision != 12)
        {
            fields.Warning(DiagnosticCodes.InvalidEncoding, $"Sample precision {precision} is not defined by JPEG.", data.LastRange);
        }

        if (componentCount == 0)
        {
            fields.Warning(DiagnosticCodes.InvalidLength, "The frame header declares zero components.", data.LastRange);
        }

        for (var index = 0; index < componentCount; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!TryByte(fields, data, ComponentKey(index, "Component identifier"), out _)
                || !TryByte(fields, data, ComponentKey(index, "Sampling factors"), out var sampling)
                || !TryByte(fields, data, ComponentKey(index, "Quantization table destination"), out var table))
            {
                return;
            }

            var horizontal = sampling >> 4;
            var vertical = sampling & 0x0F;
            fields.Derived($"<{ComponentKey(index, "Horizontal sampling factor")}>", horizontal);
            fields.Derived($"<{ComponentKey(index, "Vertical sampling factor")}>", vertical);
            if (horizontal is 0 or > 4 || vertical is 0 or > 4)
            {
                fields.Warning(
                    DiagnosticCodes.SchemaMismatch,
                    $"Component {index} sampling factors {horizontal}x{vertical} are outside 1..4.",
                    data.LastRange);
            }

            if (table > 3)
            {
                fields.Warning(
                    DiagnosticCodes.SchemaMismatch,
                    $"Component {index} quantization table {table} is outside 0..3.",
                    data.LastRange);
            }
        }

        RequireEmpty(fields, data, "frame header");
    }

    private static void ParseScanHeader(ParseContext context, FieldList fields, BigEndianReader data)
    {
        if (!TryByte(fields, data, "Number of components", out var componentCount))
        {
            return;
        }

        if (componentCount == 0)
        {
            fields.Warning(DiagnosticCodes.InvalidLength, "The scan header declares zero components.", data.LastRange);
        }

        for (var index = 0; index < componentCount; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!TryByte(fields, data, ComponentKey(index, "Component selector"), out _)
                || !TryByte(fields, data, ComponentKey(index, "Td/Ta"), out var selectors))
            {
                return;
            }

            var dc = selectors >> 4;
            var ac = selectors & 0x0F;
            fields.Derived($"<{ComponentKey(index, "DC entropy coding table selector")}>", dc);
            fields.Derived($"<{ComponentKey(index, "AC entropy coding table selector")}>", ac);
            if (dc > 3 || ac > 3)
            {
                fields.Warning(
                    DiagnosticCodes.SchemaMismatch,
                    $"Component {index} entropy table selectors DC={dc}, AC={ac} are outside 0..3.",
                    data.LastRange);
            }
        }

        if (!TryByte(fields, data, "Spectral selection start", out _)
            || !TryByte(fields, data, "Spectral selection end", out _)
            || !TryByte(fields, data, "Successive approximation", out var approximation))
        {
            return;
        }

        fields.Derived("<SuccessiveApproximationHigh>", approximation >> 4);
        fields.Derived("<SuccessiveApproximationLow>", approximation & 0x0F);
        RequireEmpty(fields, data, "scan header");
    }

    private static void ParseQuantizationTables(ParseContext context, FieldList fields, BigEndianReader data)
    {
        var tableIndex = 0;
        while (data.Remaining > 0)
        {
            context.ThrowIfCancellationRequested();
            if (!TryByte(fields, data, TableKey(tableIndex, "Pq/Tq"), out var precisionAndDestination))
            {
                return;
            }

            var precision = precisionAndDestination >> 4;
            var destination = precisionAndDestination & 0x0F;
            fields.Derived($"<{TableKey(tableIndex, "Precision")}>", precision);
            fields.Derived($"<{TableKey(tableIndex, "Destination")}>", destination);
            if (precision > 1)
            {
                fields.Warning(
                    DiagnosticCodes.InvalidEncoding,
                    $"Quantization table {tableIndex} precision {precision} must be 0 or 1.",
                    data.LastRange);
                data.SkipRemaining();
                return;
            }

            if (destination > 3)
            {
                fields.Warning(
                    DiagnosticCodes.SchemaMismatch,
                    $"Quantization table destination {destination} is outside 0..3.",
                    data.LastRange);
            }

            for (var valueIndex = 0; valueIndex < 64; valueIndex++)
            {
                var key = TableKey(tableIndex, $"Value[{valueIndex}]");
                var read = precision == 0
                    ? TryByte(fields, data, key, out _)
                    : TryUInt16(fields, data, key, out _);
                if (!read)
                {
                    return;
                }
            }

            tableIndex++;
        }
    }

    private static void ParseHuffmanTables(ParseContext context, FieldList fields, BigEndianReader data)
    {
        var tableIndex = 0;
        while (data.Remaining > 0)
        {
            context.ThrowIfCancellationRequested();
            if (!TryByte(fields, data, TableKey(tableIndex, "Tc/Th"), out var classAndDestination))
            {
                return;
            }

            var tableClass = classAndDestination >> 4;
            var destination = classAndDestination & 0x0F;
            fields.Derived($"<{TableKey(tableIndex, "Class")}>", JpegFormatting.FormatTableClass(tableClass));
            fields.Derived($"<{TableKey(tableIndex, "Destination")}>", destination);
            if (tableClass > 1)
            {
                fields.Warning(
                    DiagnosticCodes.InvalidEncoding,
                    $"Huffman table {tableIndex} class {tableClass} must be 0 or 1.",
                    data.LastRange);
            }

            if (destination > 3)
            {
                fields.Warning(
                    DiagnosticCodes.SchemaMismatch,
                    $"Huffman table destination {destination} is outside 0..3.",
                    data.LastRange);
            }

            var symbolCount = 0;
            for (var countIndex = 0; countIndex < 16; countIndex++)
            {
                if (!TryByte(fields, data, TableKey(tableIndex, $"Count[{countIndex}]"), out var count))
                {
                    return;
                }

                symbolCount += count;
            }

            fields.Derived($"<{TableKey(tableIndex, "SymbolCount")}>", symbolCount);
            var shown = Math.Min(symbolCount, context.Limits.MaxDataLines);
            for (var symbolIndex = 0; symbolIndex < symbolCount; symbolIndex++)
            {
                var key = TableKey(tableIndex, $"Symbol[{symbolIndex}]");
                if (!data.TryByte(out var symbol))
                {
                    Truncated(fields, data, key);
                    return;
                }

                if (symbolIndex < shown)
                {
                    fields.Raw(key, symbol, data.LastRange);
                }
            }

            if (symbolCount > shown)
            {
                fields.Warning(
                    DiagnosticCodes.LimitRecords,
                    $"Huffman table {tableIndex} has {symbolCount} symbols; only the first {shown} are shown.",
                    data.CurrentRange);
            }

            tableIndex++;
        }
    }

    private static void ParseRestartInterval(FieldList fields, BigEndianReader data)
    {
        if (!TryUInt16(fields, data, "Restart interval", out _))
        {
            return;
        }

        RequireEmpty(fields, data, "DRI");
    }

    private static void ParseComment(ParseContext context, FieldList fields, BigEndianReader data)
    {
        var text = data.ReadRemainingPrefix(context.Limits.MaxStringBytes, out var length, out var range);
        fields.Raw("Comment", JpegFormatting.FormatText(text, Encoding.Latin1), range);
        fields.Derived("<TextByteLength>", length, range);
        if (length > text.Length)
        {
            fields.Warning(
                DiagnosticCodes.LimitString,
                $"The comment is {length} byte(s); only the first {text.Length} are shown.",
                range);
        }
    }

    private static void RequireEmpty(FieldList fields, BigEndianReader data, string label)
    {
        if (data.Remaining > 0)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                string.Create(CultureInfo.InvariantCulture, $"The {label} has {data.Remaining} unexpected trailing byte(s)."),
                data.CurrentRange);
            data.SkipRemaining();
        }
    }

    private static bool TryByte(FieldList fields, BigEndianReader data, string key, out byte value)
    {
        if (data.TryByte(out value))
        {
            fields.Raw(key, value, data.LastRange);
            return true;
        }

        return Truncated(fields, data, key);
    }

    private static bool TryUInt16(FieldList fields, BigEndianReader data, string key, out ushort value)
    {
        if (data.TryUInt16BE(out value))
        {
            fields.Raw(key, value, data.LastRange);
            return true;
        }

        return Truncated(fields, data, key);
    }

    private static bool Truncated(FieldList fields, BigEndianReader data, string key)
    {
        fields.Warning(
            DiagnosticCodes.TruncatedPayload,
            $"Field '{key}' is truncated.",
            data.CurrentRange);
        return false;
    }

    private static string ComponentKey(int index, string field) =>
        string.Create(CultureInfo.InvariantCulture, $"Component[{index}].{field}");

    private static string TableKey(int index, string field) =>
        string.Create(CultureInfo.InvariantCulture, $"Table[{index}].{field}");

    private static bool TryGetMarkerOffset(string? stableId, out long offset)
    {
        offset = 0;
        if (stableId is null || !stableId.StartsWith("jpeg:marker:", StringComparison.Ordinal))
        {
            return false;
        }

        return long.TryParse(
            stableId.AsSpan("jpeg:marker:".Length),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out offset);
    }

    private static NodeParseResult Placeholder(FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<Length>", node.Range.Length, node.Range);
        fields.Note("This synthetic range is not a parsed JPEG marker.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult Result(FormatNode node, FieldList fields, string? title = null) =>
        new(
            title ?? node.Name,
            node.Range,
            node.Range,
            fields.ToImmutable(),
            ImmutableArray.CreateRange(fields.Diagnostics));
}
