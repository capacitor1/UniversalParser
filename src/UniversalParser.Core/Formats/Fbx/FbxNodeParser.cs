using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Fbx;

/// <summary>
/// Re-reads one FBX node record. The tree stores only ranges, so selecting a node never depends on
/// bytes copied during document parsing. Property lists are read lazily; deflate-compressed array
/// data is described by its declared lengths and is never decompressed.
/// </summary>
internal static class FbxNodeParser
{
    private const int HexPreviewBytes = 64;
    private const int ArrayPreviewElements = 16;

    public static NodeParseResult Parse(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        if (node.StableId == "fbx-root")
        {
            return ParseRoot(context, node);
        }

        if (node.StableId == "fbx:header")
        {
            return ParseHeader(context, node);
        }

        if (node.StableId == "fbx:footer")
        {
            return ParseFooter(context, node);
        }

        if (node.StableId?.StartsWith("fbx:footer:", StringComparison.Ordinal) == true)
        {
            return ParseFooterPart(context, node);
        }

        if (TryGetNodeOffset(node.StableId, out var offset))
        {
            return ParseNode(context, node, offset);
        }

        return Placeholder(node);
    }

    private static NodeParseResult ParseRoot(ParseContext context, FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<FileSize>", node.Range.Length, node.Range);
        var version = FbxConstants.ReadVersion(context.Source);
        fields.Derived("<Version>", version, node.Range);
        fields.Derived("<Version Format>", FbxFormatting.FormatVersionFormat(version), node.Range);
        fields.Derived("<ChildCount>", node.Children.Length, node.Range);
        var topLevelCount = node.Children.Count(static child =>
            child.StableId?.StartsWith("fbx:node:", StringComparison.Ordinal) == true);
        fields.Derived("<TopLevelNodeCount>", topLevelCount, node.Range);
        fields.Note("Synthetic document root; select a node record to inspect its properties.");
        return Result(node, fields);
    }

    private static NodeParseResult ParseHeader(ParseContext context, FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        if (node.Range.Length < FbxConstants.HeaderLength)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"The FBX header needs {FbxConstants.HeaderLength} bytes but only {node.Range.Length} are available.",
                node.Range);
            return Result(node, fields);
        }

        Span<byte> bytes = stackalloc byte[FbxConstants.HeaderLength];
        if (SourceRead.ReadAtMost(context.Source, node.Range.Offset, bytes) < bytes.Length)
        {
            fields.Warning(DiagnosticCodes.TruncatedHeader, "The FBX header could not be read.", node.Range);
            return Result(node, fields);
        }

        fields.Raw(
            "File Magic",
            FbxFormatting.FormatText(bytes[..FbxConstants.BinaryMagicLength]),
            new SourceRange(node.Range.Offset, FbxConstants.BinaryMagicLength));
        fields.Raw(
            "Magic Terminator",
            $"0x{bytes[20]:X2}",
            new SourceRange(node.Range.Offset + 20, 1));
        fields.Raw("Header Byte 21", $"0x{bytes[21]:X2}", new SourceRange(node.Range.Offset + 21, 1));
        fields.Raw("Header Byte 22", $"0x{bytes[22]:X2}", new SourceRange(node.Range.Offset + 22, 1));

        var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes[23..27]);
        fields.Raw("Version", version, new SourceRange(node.Range.Offset + 23, 4));
        fields.Derived("<Version Format>", FbxFormatting.FormatVersionFormat(version));
        fields.Derived("<RecordHeaderLength>", FbxConstants.RecordHeaderLength(version));
        fields.Derived("<NullRecordLength>", FbxConstants.NullRecordLength(version));

        if (!bytes[..FbxConstants.BinaryMagicLength].SequenceEqual(FbxConstants.BinaryMagic))
        {
            fields.Warning(
                DiagnosticCodes.InvalidSignature,
                "The file magic is not 'Kaydara FBX Binary'.",
                new SourceRange(node.Range.Offset, FbxConstants.BinaryMagicLength));
        }

        if (version < FbxConstants.MinimumDocumentedVersion)
        {
            fields.Warning(
                DiagnosticCodes.UnsupportedVersion,
                $"Version {version} predates the documented FBX binary layout; the record layout may differ.",
                new SourceRange(node.Range.Offset + 23, 4));
        }

        return Result(node, fields);
    }

    private static NodeParseResult ParseFooter(ParseContext context, FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        var version = FbxConstants.ReadVersion(context.Source);
        if (node.Kind != NodeKind.Container
            || !FbxFooterLayout.TryRead(
                context.Source,
                node.Range.Offset,
                node.Range.End,
                FbxConstants.NullRecordLength(version),
                out _))
        {
            fields.Derived("<UnparsedLength>", node.Range.Length, node.Range);
            fields.Note(
                "Footer contents are not interpreted because the observed FBX footer structure could not be verified.",
                node.Range);
            return Result(node, fields);
        }

        fields.Derived("<FooterLength>", node.Range.Length, node.Range);
        fields.Note(
            "Footer structure follows the observed FBX layout (footer id, version echo, zero fill, trailer); "
            + "the footer id derivation is not documented and is shown as opaque bytes.",
            node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult ParseFooterPart(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList(int.MaxValue);
        var suffix = node.StableId is { } stableId ? stableId["fbx:footer:".Length..] : string.Empty;
        switch (suffix)
        {
            case "null":
                fields.Derived("<Length>", node.Range.Length, node.Range);
                fields.Note("Null record terminates the top-level FBX node list.", node.Range);
                break;
            case "id":
                if (node.Range.Length >= 16)
                {
                    Span<byte> bytes = stackalloc byte[16];
                    if (SourceRead.ReadAtMost(context.Source, node.Range.Offset, bytes) != bytes.Length)
                    {
                        fields.Warning(DiagnosticCodes.TruncatedPayload, "The footer id needs 16 bytes.", node.Range);
                        break;
                    }

                    fields.Raw("Footer ID", HexFormatting.Format(bytes, 16), node.Range);
                    fields.Derived("<FooterIdLength>", node.Range.Length, node.Range);
                    fields.Note(
                        "The 16-byte footer code is opaque; no universal, publicly specified decoding is available.",
                        node.Range);
                }
                else
                {
                    fields.Warning(DiagnosticCodes.TruncatedPayload, "The footer id needs 16 bytes.", node.Range);
                }

                break;
            case "padding":
            case "zeros":
                fields.Derived("<Length>", node.Range.Length, node.Range);
                break;
            case "version":
                if (node.Range.Length >= 4)
                {
                    Span<byte> bytes = stackalloc byte[4];
                    if (SourceRead.ReadAtMost(context.Source, node.Range.Offset, bytes) == bytes.Length)
                    {
                        fields.Raw(
                            "Version",
                            BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                            node.Range);
                        break;
                    }
                }

                fields.Warning(DiagnosticCodes.TruncatedPayload, "The footer version echo needs 4 bytes.", node.Range);
                break;
            case "trailer":
                if (node.Range.Length >= 16)
                {
                    Span<byte> bytes = stackalloc byte[16];
                    if (SourceRead.ReadAtMost(context.Source, node.Range.Offset, bytes) == bytes.Length)
                    {
                        fields.Raw("Trailer", HexFormatting.Format(bytes, 16), node.Range);
                        break;
                    }
                }

                fields.Warning(DiagnosticCodes.TruncatedPayload, "The footer trailer needs 16 bytes.", node.Range);
                break;
            default:
                fields.Derived("<Length>", node.Range.Length, node.Range);
                break;
        }

        return Result(node, fields);
    }

    private static NodeParseResult ParseNode(ParseContext context, FormatNode node, long expectedOffset)
    {
        var fields = context.CreateFieldList();
        var version = FbxConstants.ReadVersion(context.Source);
        var wide = FbxConstants.IsWide(version);
        var headerSize = FbxConstants.RecordHeaderLength(version);

        if (node.Range.Length < headerSize)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"The node record is shorter than its {headerSize}-byte header.",
                node.Range);
            return Result(node, fields);
        }

        var reader = new FbxFieldReader(
            new SourceReader(context.Source, node.Range.Offset, node.Range.Length),
            fields);

        if (!TryHeaderValue(reader, fields, wide, "EndOffset", out var endOffset)
            || !TryHeaderValue(reader, fields, wide, "NumProperties", out var numProperties)
            || !TryHeaderValue(reader, fields, wide, "PropertyListLen", out var propertyListLength)
            || !reader.TryByte(out var nameLength))
        {
            return Result(node, fields);
        }

        fields.Raw("NameLen", nameLength, reader.LastRange);
        if (!reader.TryBytes(nameLength, out var nameBytes))
        {
            return Result(node, fields);
        }

        fields.Derived("<NodeLength>", node.Range.Length, node.Range);
        fields.Derived("<ChildCount>", node.Children.Length, node.Range);

        if (endOffset > (ulong)node.Range.End)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"EndOffset is {endOffset}, but the parsed node ends at {node.Range.End}.",
                new SourceRange(node.Range.Offset, headerSize));
        }

        if (expectedOffset != node.Range.Offset)
        {
            fields.Warning(
                DiagnosticCodes.InvalidSignature,
                $"The node identity points at {expectedOffset}, but the node starts at {node.Range.Offset}.",
                node.Range);
        }

        var propertyOffset = node.Range.Offset + headerSize + nameLength;
        var available = node.Range.End - propertyOffset;
        if (available < 0)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                "The node name extends beyond the node range.",
                node.Range);
            return Result(node, fields);
        }

        var declared = propertyListLength > long.MaxValue ? long.MaxValue : (long)propertyListLength;
        if (declared > available)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"PropertyListLen is {propertyListLength}, but only {available} byte(s) remain in the node.",
                new SourceRange(node.Range.Offset, headerSize));
        }

        ParseProperties(
            context,
            fields,
            propertyOffset,
            Math.Min(declared, available),
            numProperties);

        return Result(node, fields);
    }

    private static void ParseProperties(
        ParseContext context,
        FieldList fields,
        long propertyOffset,
        long propertyLength,
        ulong numProperties)
    {
        var count = numProperties > long.MaxValue ? long.MaxValue : (long)numProperties;
        if (count == 0)
        {
            if (propertyLength > 0)
            {
                fields.Warning(
                    DiagnosticCodes.SchemaMismatch,
                    $"NumProperties is 0, but PropertyListLen is {propertyLength}.",
                    new SourceRange(propertyOffset, propertyLength));
            }

            return;
        }

        if (propertyLength <= 0)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"NumProperties is {numProperties}, but the property list is empty.",
                new SourceRange(propertyOffset, 0));
            return;
        }

        var limit = Math.Min(count, context.Limits.MaxDataLines);
        if (count > limit)
        {
            fields.Warning(
                DiagnosticCodes.LimitRecords,
                $"NumProperties is {count}; only the first {limit} properties are shown.",
                new SourceRange(propertyOffset, propertyLength));
        }

        var reader = new FbxFieldReader(
            new SourceReader(context.Source, propertyOffset, propertyLength),
            fields);

        var index = 0L;
        var stopped = false;
        while (index < limit)
        {
            if (reader.Remaining <= 0)
            {
                fields.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    $"The property list ends after {index} of {count} propert{(count == 1 ? "y" : "ies")}.",
                    new SourceRange(propertyOffset, propertyLength));
                stopped = true;
                break;
            }

            if (!reader.TryByte(out var typeCode))
            {
                stopped = true;
                break;
            }

            var prefix = string.Create(CultureInfo.InvariantCulture, $"Property[{index}]");
            fields.Raw($"{prefix}.TypeCode", FbxFormatting.FormatTypeCode((char)typeCode), reader.LastRange);
            if (!TryParseProperty(context, fields, reader, prefix, (char)typeCode))
            {
                stopped = true;
                break;
            }

            index++;
        }

        var consumed = reader.Position - propertyOffset;
        if (!stopped && index == count && consumed != propertyLength)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                $"The {count} propert{(count == 1 ? "y" : "ies")} consume {consumed} byte(s), but PropertyListLen is {propertyLength}.",
                new SourceRange(propertyOffset, propertyLength));
        }
    }

    private static bool TryParseProperty(
        ParseContext context,
        FieldList fields,
        FbxFieldReader reader,
        string prefix,
        char typeCode)
    {
        switch (typeCode)
        {
            case 'Y':
                if (!reader.TryInt16(out var int16))
                {
                    return false;
                }

                fields.Raw(prefix, int16, reader.LastRange);
                return true;

            case 'C':
                if (!reader.TryByte(out var boolean))
                {
                    return false;
                }

                fields.Raw(prefix, $"{boolean} ({(boolean == 0 ? "false" : "true")})", reader.LastRange);
                return true;

            case 'I':
                if (!reader.TryInt32(out var int32))
                {
                    return false;
                }

                fields.Raw(prefix, int32, reader.LastRange);
                return true;

            case 'F':
                if (!reader.TrySingle(out var single))
                {
                    return false;
                }

                fields.Raw(prefix, FbxFormatting.FormatSingle(single), reader.LastRange);
                return true;

            case 'D':
                if (!reader.TryDouble(out var number))
                {
                    return false;
                }

                fields.Raw(prefix, FbxFormatting.FormatDouble(number), reader.LastRange);
                return true;

            case 'L':
                if (!reader.TryInt64(out var int64))
                {
                    return false;
                }

                fields.Raw(prefix, int64, reader.LastRange);
                return true;

            case 'S':
                return ParseString(context, fields, reader, prefix);

            case 'R':
                return ParseRaw(fields, reader, prefix);

            case 'f':
            case 'd':
            case 'l':
            case 'i':
            case 'b':
                return ParseArray(context, fields, reader, prefix, typeCode);

            default:
                fields.Warning(
                    DiagnosticCodes.SchemaMismatch,
                    $"Unknown property type code '{typeCode}' (0x{(int)typeCode:X2}).",
                    reader.LastRange);
                return false;
        }
    }

    private static bool ParseString(
        ParseContext context,
        FieldList fields,
        FbxFieldReader reader,
        string prefix)
    {
        if (!reader.TryUInt32(out var length))
        {
            return false;
        }

        fields.Raw($"{prefix}.Length", length, reader.LastRange);
        var bytes = reader.ReadPrefix(length, context.Limits.MaxStringBytes, out var range);
        if (bytes.Length == range.Length && !FbxFormatting.IsValidUtf8(bytes))
        {
            fields.Warning(
                DiagnosticCodes.InvalidEncoding,
                $"{prefix}.Data is not valid UTF-8; invalid bytes are shown as replacement characters.",
                range);
        }

        fields.Raw($"{prefix}.Data", FbxFormatting.FormatText(bytes), range);
        if (length <= range.Length && range.Length > bytes.Length)
        {
            fields.Warning(
                DiagnosticCodes.LimitString,
                $"{prefix}.Data is {range.Length} byte(s); only the first {bytes.Length} are shown.",
                range);
        }

        return true;
    }

    private static bool ParseRaw(FieldList fields, FbxFieldReader reader, string prefix)
    {
        if (!reader.TryUInt32(out var length))
        {
            return false;
        }

        fields.Raw($"{prefix}.Length", length, reader.LastRange);
        var bytes = reader.ReadPrefix(length, HexPreviewBytes, out var range);
        fields.Raw($"{prefix}.Data", FbxFormatting.FormatHex(bytes, HexPreviewBytes), range);
        if (range.Length > bytes.Length)
        {
            fields.Note(
                $"{prefix}.Data shows the first {bytes.Length} of {range.Length} byte(s).",
                range);
        }

        return true;
    }

    private static bool ParseArray(
        ParseContext context,
        FieldList fields,
        FbxFieldReader reader,
        string prefix,
        char typeCode)
    {
        if (!reader.TryUInt32(out var arrayLength))
        {
            return false;
        }

        fields.Raw($"{prefix}.ArrayLength", arrayLength, reader.LastRange);
        if (!reader.TryUInt32(out var encoding))
        {
            return false;
        }

        fields.Raw($"{prefix}.Encoding", encoding, reader.LastRange);
        var encodingRange = reader.LastRange;
        if (!reader.TryUInt32(out var compressedLength))
        {
            return false;
        }

        fields.Raw($"{prefix}.CompressedLength", compressedLength, reader.LastRange);

        var elementSize = ElementSize(typeCode);
        var uncompressedLength = (ulong)arrayLength * (ulong)elementSize;
        fields.Derived(
            $"<{prefix}.UncompressedDataLength>",
            uncompressedLength.ToString(CultureInfo.InvariantCulture));

        var dataRange = new SourceRange(
            reader.Position,
            Math.Min(compressedLength, reader.Remaining));

        if (encoding == 0)
        {
            if (compressedLength != uncompressedLength)
            {
                fields.Warning(
                    DiagnosticCodes.SchemaMismatch,
                    $"{prefix}.CompressedLength is {compressedLength}, but ArrayLength x element size is {uncompressedLength}.",
                    new SourceRange(reader.Position, 0));
            }

            AppendArrayPreview(context, fields, reader.Position, arrayLength, typeCode, prefix, dataRange.Length);
        }
        else if (encoding == 1)
        {
            fields.Note(
                $"{prefix} is deflate-compressed; the array data is not decoded.",
                dataRange);
        }
        else
        {
            fields.Warning(
                DiagnosticCodes.InvalidEncoding,
                $"{prefix}.Encoding is {encoding}; only 0 (raw) and 1 (deflate) are documented.",
                encodingRange);
        }

        return reader.Skip(compressedLength);
    }

    private static void AppendArrayPreview(
        ParseContext context,
        FieldList fields,
        long dataOffset,
        uint arrayLength,
        char typeCode,
        string prefix,
        long availableLength)
    {
        if (arrayLength == 0 || availableLength <= 0)
        {
            return;
        }

        var elementSize = ElementSize(typeCode);
        var previewCount = (int)Math.Min(arrayLength, ArrayPreviewElements);
        var byteCount = (int)Math.Min((long)previewCount * elementSize, availableLength);
        var bytes = new byte[byteCount];
        var read = SourceRead.ReadAtMost(context.Source, dataOffset, bytes);
        var available = Math.Min(previewCount, read / elementSize);
        if (available == 0)
        {
            return;
        }

        var builder = new StringBuilder();
        for (var index = 0; index < available; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append(FormatArrayElement(typeCode, bytes.AsSpan(index * elementSize, elementSize)));
        }

        if (available < arrayLength)
        {
            builder.Append(", ...");
        }

        fields.Derived(
            $"<{prefix}.Preview>",
            builder.ToString(),
            new SourceRange(dataOffset, (long)available * elementSize));
    }

    private static string FormatArrayElement(char typeCode, ReadOnlySpan<byte> bytes) =>
        typeCode switch
        {
            'f' => FbxFormatting.FormatSingle(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes))),
            'd' => FbxFormatting.FormatDouble(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes))),
            'l' => BinaryPrimitives.ReadInt64LittleEndian(bytes).ToString(CultureInfo.InvariantCulture),
            'i' => BinaryPrimitives.ReadInt32LittleEndian(bytes).ToString(CultureInfo.InvariantCulture),
            _ => bytes[0].ToString(CultureInfo.InvariantCulture),
        };

    private static int ElementSize(char typeCode) => typeCode switch
    {
        'f' => 4,
        'd' => 8,
        'l' => 8,
        'i' => 4,
        _ => 1,
    };

    private static bool TryHeaderValue(
        FbxFieldReader reader,
        FieldList fields,
        bool wide,
        string key,
        out ulong value)
    {
        if (wide)
        {
            if (!reader.TryUInt64(out var wideValue))
            {
                value = 0;
                return false;
            }

            value = wideValue;
            fields.Raw(key, wideValue, reader.LastRange);
            return true;
        }

        if (!reader.TryUInt32(out var narrowValue))
        {
            value = 0;
            return false;
        }

        value = narrowValue;
        fields.Raw(key, narrowValue, reader.LastRange);
        return true;
    }

    private static bool TryGetNodeOffset(string? stableId, out long offset)
    {
        offset = 0;
        if (stableId is null || !stableId.StartsWith("fbx:node:", StringComparison.Ordinal))
        {
            return false;
        }

        return long.TryParse(
            stableId.AsSpan("fbx:node:".Length),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out offset);
    }

    private static NodeParseResult Placeholder(FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<Length>", node.Range.Length, node.Range);
        fields.Note("This synthetic range is not a parsed FBX structure.", node.Range);
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
        if (node.Name.StartsWith('<') || node.Name is "FBX" or "FBXHeader" or "FBXFooter")
        {
            return node.Name;
        }

        return $"Node '{RawIdentifier.FormatText(node.Name)}'";
    }

}
