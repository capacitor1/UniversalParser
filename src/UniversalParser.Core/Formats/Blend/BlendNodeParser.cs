using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Blend;

/// <summary>
/// Re-reads one blend block header and the known metadata blocks (DNA1, GLOB, TEST, ENDB).
/// Runtime memory blocks stay unparsed, but their block header and SDNA structure name are shown.
/// </summary>
internal static class BlendNodeParser
{
    public static NodeParseResult Parse(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        if (node.StableId == "blend-root")
        {
            return ParseRoot(context, node);
        }

        if (!BlendHeaderReader.TryRead(context.Source, out var header, out var headerError))
        {
            var fields = context.CreateFieldList();
            fields.Error(
                DiagnosticCodes.InvalidSignature,
                $"The Blender file header could not be read ({headerError}).",
                node.Range);
            return Result(node, fields);
        }

        if (!BlendBlockReader.TryRead(context.Source, node.Range.Offset, node.Range.End, header, out var block, out var blockError))
        {
            var fields = context.CreateFieldList();
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"The blend block header could not be read ({blockError}).",
                node.Range);
            return Result(node, fields);
        }

        return ParseBlock(context, node, header, block);
    }

    private static NodeParseResult ParseRoot(ParseContext context, FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        if (!BlendHeaderReader.TryRead(context.Source, out var header, out var error))
        {
            fields.Error(
                DiagnosticCodes.InvalidSignature,
                $"The Blender file header could not be read ({error}).",
                node.Range);
            return Result(node, fields);
        }

        fields.Derived("<HeaderSize>", header.HeaderSize, new SourceRange(0, 12));
        fields.Derived("<FileFormatVersion>", header.FileFormatVersion, new SourceRange(0, 12));
        if (header.FileFormatVersion == 0)
        {
            fields.Raw("pointer_size", header.PointerSize.ToString(CultureInfo.InvariantCulture), new SourceRange(7, 1));
        }
        else
        {
            fields.Derived("<PointerSize>", header.PointerSize, new SourceRange(0, BlendHeaderReader.CurrentHeaderLength));
        }

        fields.Raw(
            "endian",
            header.Endian == BlendEndian.Little ? "v (little endian)" : "V (big endian)",
            header.HeaderSize == BlendHeaderReader.CurrentHeaderLength
                ? new SourceRange(12, 1)
                : new SourceRange(8, 1));
        fields.Raw(
            "file_version",
            header.VersionText,
            header.HeaderSize == BlendHeaderReader.CurrentHeaderLength
                ? new SourceRange(13, 4)
                : new SourceRange(9, 3));
        fields.Derived("<FileSize>", node.Range.Length, node.Range);
        fields.Derived(
            "<BlockCount>",
            node.Children.Count(static child => child.StableId?.StartsWith("blend:", StringComparison.Ordinal) == true),
            node.Range);
        fields.Note("Synthetic document root; select a block to inspect its header and payload.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult ParseBlock(
        ParseContext context,
        FormatNode node,
        BlendHeader header,
        BlendBlockHeader block)
    {
        var fields = context.CreateFieldList();
        if (block.IsLegacyEnd)
        {
            fields.Note("Legacy 8-byte ENDB terminator.", node.Range);
            return Result(node, fields);
        }

        var ranges = GetBlockFieldRanges(block, header);
        fields.Raw("len", block.Length, ranges.Length);
        var oldText = "0x" + block.OldAddress.ToString(
            header.PointerSize == 4 ? "X8" : "X16",
            CultureInfo.InvariantCulture);
        fields.Raw("old", oldText, ranges.Old);
        fields.Raw("SDNAnr", block.SdnaIndex, ranges.Sdna);
        fields.Raw("nr", block.Count, ranges.Count);

        if (block.Truncated)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The blend block data extends beyond the end of the file.",
                SourceRange.FromBounds(block.Start, block.End));
        }

        switch (block.Code)
        {
            case "DNA1":
                ParseDna(context, fields, block, header);
                break;
            case "GLOB":
                ParseGlobal(context, fields, node, block, header);
                break;
            case "TEST":
                ParseThumbnail(context, fields, block, header);
                break;
            case "ENDB":
                fields.Note("End of the blend file block sequence.", node.Range);
                break;
            default:
                var structName = GetStableStructName(node.StableId);
                if (!string.IsNullOrEmpty(structName))
                {
                    fields.Derived("<SDNA_STRUCT>", structName, node.Range);
                }

                fields.Note("Block data is a runtime memory dump and is not interpreted.", SourceRange.FromBounds(block.DataStart, block.End));
                break;
        }

        return Result(node, fields);
    }

    private static void ParseDna(
        ParseContext context,
        FieldList fields,
        BlendBlockHeader block,
        BlendHeader header)
    {
        var dna = BlendDna.TryParse(
            context.Source,
            block.DataStart,
            block.DataLength,
            header.Endian,
            context.CancellationToken,
            out var error);
        if (dna is null)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                $"The DNA1 block could not be parsed: {error}",
                SourceRange.FromBounds(block.DataStart, block.End));
            return;
        }

        var range = SourceRange.FromBounds(block.DataStart, block.End);
        fields.Raw("NAME_COUNT", dna.Names.Length, range);
        fields.Raw("TYPE_COUNT", dna.Types.Length, range);
        fields.Raw("TLEN_COUNT", dna.TypeLengths.Length, range);
        fields.Raw("STRUCT_COUNT", dna.Structs.Length, range);

        for (var index = 0; index < dna.Names.Length; index++)
        {
            if ((index & 0xFF) == 0)
            {
                context.ThrowIfCancellationRequested();
            }

            fields.Raw($"NAME[{index}]", dna.Names[index], range);
        }

        for (var index = 0; index < dna.Types.Length; index++)
        {
            if ((index & 0xFF) == 0)
            {
                context.ThrowIfCancellationRequested();
            }

            fields.Raw($"TYPE[{index}]", dna.Types[index], range);
        }

        for (var index = 0; index < dna.TypeLengths.Length; index++)
        {
            fields.Raw($"TLEN[{index}]", dna.TypeLengths[index], range);
        }

        for (var index = 0; index < dna.Structs.Length; index++)
        {
            if ((index & 0xFF) == 0)
            {
                context.ThrowIfCancellationRequested();
            }

            var definition = dna.Structs[index];
            fields.Raw(
                $"STRUCT[{index}]",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{dna.Types[definition.TypeIndex]} ({definition.Fields.Length} field(s))"),
                range);
        }

        fields.Note("DNA structure names are used to label blend blocks.", range);
    }

    private static void ParseGlobal(
        ParseContext context,
        FieldList fields,
        FormatNode node,
        BlendBlockHeader block,
        BlendHeader header)
    {
        if (!TryParseDnaLocation(node.StableId, out var dnaStart, out var dnaLength))
        {
            fields.Warning(DiagnosticCodes.SchemaMismatch, "The DNA1 block location is missing for this GLOB block.", node.Range);
            return;
        }

        var dna = BlendDna.TryParse(
            context.Source,
            dnaStart,
            dnaLength,
            header.Endian,
            context.CancellationToken,
            out var error);
        if (dna is null)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                $"The DNA1 block could not be parsed: {error}",
                node.Range);
            return;
        }

        if (!dna.TryBuildLayout("FileGlobal", header.PointerSize, out var layout, out var structSize))
        {
            fields.Warning(DiagnosticCodes.SchemaMismatch, "The DNA FileGlobal structure could not be laid out.", node.Range);
            return;
        }

        fields.Derived("<SDNA_STRUCT>", "FileGlobal", node.Range);
        if (block.Count != 1)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                string.Create(CultureInfo.InvariantCulture, $"FileGlobal block count is {block.Count}, expected 1."),
                node.Range);
        }

        if (block.DataLength < structSize)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The GLOB block is {block.DataLength} byte(s), but FileGlobal is {structSize} byte(s)."),
                SourceRange.FromBounds(block.DataStart, block.End));
        }

        var little = header.Endian == BlendEndian.Little;
        var paddingFields = 0;
        foreach (var field in layout)
        {
            if (field.Name.StartsWith("_pad", StringComparison.Ordinal))
            {
                paddingFields++;
                continue;
            }

            var offset = block.DataStart + field.Offset;
            if (field.Offset < 0 || field.Offset + field.Size > block.DataLength)
            {
                fields.Derived($"<{field.Name}>", "outside the block data", node.Range);
                continue;
            }

            var range = new SourceRange(offset, field.Size);
            if (field.PointerDepth > 0)
            {
                if (TryReadUnsigned(context.Source, offset, header.PointerSize, little, out var address))
                {
                    var addressText = "0x" + address.ToString(
                        header.PointerSize == 4 ? "X8" : "X16",
                        CultureInfo.InvariantCulture);
                    fields.Raw(field.Name, addressText + " (file address)", range);
                }
            }
            else if (field.IsChar)
            {
                if (TryReadText(context.Source, offset, field.Size, context.Limits.MaxStringBytes, out var text))
                {
                    fields.Raw(field.Name, text, range);
                }
            }
            else if (field.IsStruct)
            {
                fields.Derived($"<{field.Name}>", $"{field.TypeName} (not interpreted)", range);
            }
            else if (field.Dimensions.Length > 0)
            {
                var elementSize = field.Size / Math.Max(1, field.Dimensions.Aggregate(1, static (a, b) => a * b));
                var count = field.Size / Math.Max(1, elementSize);
                if (count <= 16)
                {
                    for (var index = 0; index < count; index++)
                    {
                        var itemOffset = offset + index * elementSize;
                        if (TryFormatScalar(context.Source, itemOffset, field.TypeName, elementSize, little, out var value))
                        {
                            fields.Raw($"{field.Name}[{index}]", value, new SourceRange(itemOffset, elementSize));
                        }
                    }
                }
                else
                {
                    fields.Derived($"<{field.Name}>", $"{count} element(s) (not expanded)", range);
                }
            }
            else if (TryFormatScalar(context.Source, offset, field.TypeName, field.Size, little, out var scalar))
            {
                fields.Raw(field.Name, scalar, range);
            }
        }

        if (paddingFields > 0)
        {
            fields.Derived("<PaddingFields>", paddingFields, node.Range);
        }

        fields.Note("Pointer values are addresses from the original Blender process; they are not resolved.", node.Range);
    }

    private static void ParseThumbnail(
        ParseContext context,
        FieldList fields,
        BlendBlockHeader block,
        BlendHeader header)
    {
        if (block.DataLength < 8)
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The TEST thumbnail block is shorter than its header.", SourceRange.FromBounds(block.DataStart, block.End));
            return;
        }

        var little = header.Endian == BlendEndian.Little;
        _ = TryReadSigned(context.Source, block.DataStart, 4, little, out var width);
        _ = TryReadSigned(context.Source, block.DataStart + 4, 4, little, out var height);
        fields.Raw("width", width, new SourceRange(block.DataStart, 4));
        fields.Raw("height", height, new SourceRange(block.DataStart + 4, 4));

        var pixels = (long)width * height;
        if (width is > 0 and <= 65535 && height is > 0 and <= 65535)
        {
            var dataLength = pixels * 4;
            fields.Derived("<ThumbnailDataLength>", dataLength, new SourceRange(block.DataStart + 8, Math.Max(0, Math.Min(dataLength, block.DataLength - 8))));
            if (dataLength > block.DataLength - 8)
            {
                fields.Warning(DiagnosticCodes.InvalidLength, "The thumbnail pixel data is shorter than its declared size.", SourceRange.FromBounds(block.DataStart, block.End));
            }

            fields.Note("Thumbnail pixels are not decoded.", SourceRange.FromBounds(block.DataStart, block.End));
        }
        else
        {
            fields.Warning(DiagnosticCodes.InvalidLength, "The thumbnail dimensions are invalid.", new SourceRange(block.DataStart, 8));
        }
    }

    private static bool TryFormatScalar(
        IRandomAccessSource source,
        long offset,
        string typeName,
        int size,
        bool little,
        out string value)
    {
        value = string.Empty;
        if (string.Equals(typeName, "float", StringComparison.Ordinal) && size == 4)
        {
            if (!TryReadUInt32(source, offset, little, out var raw))
            {
                return false;
            }

            value = BitConverter.Int32BitsToSingle(unchecked((int)raw)).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (string.Equals(typeName, "double", StringComparison.Ordinal) && size == 8)
        {
            if (!TryReadUInt64(source, offset, little, out var raw))
            {
                return false;
            }

            value = BitConverter.Int64BitsToDouble(unchecked((long)raw)).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (size is not (1 or 2 or 4 or 8))
        {
            return false;
        }

        if (!TryReadUnsigned(source, offset, size, little, out var unsigned))
        {
            return false;
        }

        var isUnsigned = typeName.StartsWith('u')
            || string.Equals(typeName, "unsigned char", StringComparison.Ordinal);
        if (isUnsigned)
        {
            value = unsigned.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        var signed = size switch
        {
            1 => unchecked((sbyte)unsigned),
            2 => unchecked((short)unsigned),
            4 => unchecked((int)unsigned),
            _ => unchecked((long)unsigned),
        };
        value = signed.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryReadText(
        IRandomAccessSource source,
        long offset,
        int length,
        int maxBytes,
        out string value)
    {
        var count = Math.Min(length, maxBytes);
        var buffer = new byte[count];
        if (SourceRead.ReadAtMost(source, offset, buffer) != count)
        {
            value = string.Empty;
            return false;
        }

        var zero = Array.IndexOf(buffer, (byte)0, 0, count);
        if (zero >= 0)
        {
            count = zero;
        }

        value = TextEscaping.Escape(Encoding.UTF8.GetString(buffer, 0, count));
        return true;
    }

    private static bool TryReadUnsigned(
        IRandomAccessSource source,
        long offset,
        int size,
        bool little,
        out ulong value)
    {
        value = 0;
        Span<byte> bytes = stackalloc byte[8];
        if (SourceRead.ReadAtMost(source, offset, bytes[..size]) != size)
        {
            return false;
        }

        value = size switch
        {
            1 => bytes[0],
            2 => little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes),
            4 => little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes),
            8 => little ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : BinaryPrimitives.ReadUInt64BigEndian(bytes),
            _ => 0,
        };
        return size is 1 or 2 or 4 or 8;
    }

    private static bool TryReadUInt32(IRandomAccessSource source, long offset, bool little, out uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (SourceRead.ReadAtMost(source, offset, bytes) != bytes.Length)
        {
            value = 0;
            return false;
        }

        value = little
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt32BigEndian(bytes);
        return true;
    }

    private static bool TryReadUInt64(IRandomAccessSource source, long offset, bool little, out ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        if (SourceRead.ReadAtMost(source, offset, bytes) != bytes.Length)
        {
            value = 0;
            return false;
        }

        value = little
            ? BinaryPrimitives.ReadUInt64LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt64BigEndian(bytes);
        return true;
    }

    private static bool TryReadSigned(IRandomAccessSource source, long offset, int size, bool little, out long value)
    {
        if (!TryReadUnsigned(source, offset, size, little, out var raw))
        {
            value = 0;
            return false;
        }

        value = size switch
        {
            1 => unchecked((sbyte)raw),
            2 => unchecked((short)raw),
            4 => unchecked((int)raw),
            _ => unchecked((long)raw),
        };
        return true;
    }

    private static (SourceRange Length, SourceRange Old, SourceRange Sdna, SourceRange Count) GetBlockFieldRanges(
        BlendBlockHeader block,
        BlendHeader header)
    {
        if (header.FileFormatVersion == 1)
        {
            return (
                new SourceRange(block.Start + 16, 8),
                new SourceRange(block.Start + 8, 8),
                new SourceRange(block.Start + 4, 4),
                new SourceRange(block.Start + 24, 8));
        }

        if (header.PointerSize == 8)
        {
            return (
                new SourceRange(block.Start + 4, 4),
                new SourceRange(block.Start + 8, 8),
                new SourceRange(block.Start + 16, 4),
                new SourceRange(block.Start + 20, 4));
        }

        return (
            new SourceRange(block.Start + 4, 4),
            new SourceRange(block.Start + 8, 4),
            new SourceRange(block.Start + 12, 4),
            new SourceRange(block.Start + 16, 4));
    }

    private static string GetStableStructName(string? stableId)
    {
        if (stableId is null || !stableId.StartsWith("blend:block:", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var parts = stableId.Split(':');
        return parts.Length >= 5 ? parts[4] : string.Empty;
    }

    private static bool TryParseDnaLocation(string? stableId, out long dnaStart, out long dnaLength)
    {
        dnaStart = 0;
        dnaLength = 0;
        if (stableId is null || !stableId.StartsWith("blend:glob:", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = stableId.Split(':');
        return parts.Length >= 5
            && long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out dnaStart)
            && long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out dnaLength);
    }

    private static NodeParseResult Result(FormatNode node, FieldList fields, string? title = null) =>
        new(
            title ?? node.Name,
            node.Range,
            node.Range,
            fields.ToImmutable(),
            [.. fields.Diagnostics]);
}
