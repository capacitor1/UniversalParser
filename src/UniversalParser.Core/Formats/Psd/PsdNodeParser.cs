using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Embedded;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Psd;

/// <summary>
/// Re-reads one PSD/PSB node on demand. Header fields use the specification's names; derived
/// names, counts and unparsed lengths use angle brackets. Pixel data and opaque tagged blocks are
/// never decoded.
/// </summary>
internal static class PsdNodeParser
{
    public static NodeParseResult Parse(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        var id = node.StableId ?? string.Empty;
        if (id == "psd-root")
        {
            return ParseRoot(context, node);
        }

        if (id == "psd:header")
        {
            return ParseHeader(context, node);
        }

        if (id.StartsWith("psd:cmd:", StringComparison.Ordinal))
        {
            return ParseColorModeData(context, node);
        }

        if (id.StartsWith("psd:ir:", StringComparison.Ordinal))
        {
            return ParseImageResources(context, node);
        }

        if (id.StartsWith("psd:res:", StringComparison.Ordinal))
        {
            return ParseResource(context, node);
        }

        if (id.StartsWith("psd:lmi:", StringComparison.Ordinal))
        {
            return ParseLayerAndMaskInformation(context, node);
        }

        if (id.StartsWith("psd:li:", StringComparison.Ordinal))
        {
            return ParseLayerInfo(context, node);
        }

        if (id.StartsWith("psd:lc:", StringComparison.Ordinal))
        {
            return ParseLayerCount(context, node);
        }

        if (id.StartsWith("psd:lrs:", StringComparison.Ordinal))
        {
            return ParseLayerRecords(context, node);
        }

        if (id.StartsWith("psd:lrh:", StringComparison.Ordinal))
        {
            return ParseLayerRecordHeader(context, node);
        }

        if (id.StartsWith("psd:lr:", StringComparison.Ordinal))
        {
            return ParseLayerRecord(context, node);
        }

        if (id.StartsWith("psd:lm:", StringComparison.Ordinal))
        {
            return ParseLayerMaskData(context, node);
        }

        if (id.StartsWith("psd:lbr:", StringComparison.Ordinal))
        {
            return ParseLayerBlendingRanges(context, node);
        }

        if (id.StartsWith("psd:ln:", StringComparison.Ordinal))
        {
            return ParseLayerName(context, node);
        }

        if (id.StartsWith("psd:ai:", StringComparison.Ordinal))
        {
            return ParseAdditionalInfo(context, node);
        }

        if (id.StartsWith("psd:cds:", StringComparison.Ordinal))
        {
            return ParseChannelImageData(context, node);
        }

        if (id.StartsWith("psd:cd:", StringComparison.Ordinal))
        {
            return ParseChannelData(context, node);
        }

        if (id.StartsWith("psd:glm:", StringComparison.Ordinal))
        {
            return ParseGlobalLayerMaskInfo(context, node);
        }

        if (id.StartsWith("psd:id:", StringComparison.Ordinal))
        {
            return ParseImageData(context, node);
        }

        return Placeholder(node);
    }

    private static NodeParseResult ParseRoot(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        fields.Derived("<FileSize>", node.Range.Length, node.Range);
        fields.Derived("<SectionCount>", node.Children.Length, node.Range);
        if (PsdLayout.TryReadHeader(context.Source, out var header, out _))
        {
            fields.Derived("<Width>", header.Width, node.Range);
            fields.Derived("<Height>", header.Height, node.Range);
            fields.Derived("<ColorMode>", PsdConstants.FormatColorMode(header.ColorMode), node.Range);
        }
        else
        {
            fields.Note("The file header is truncated; dimensions are unavailable.", node.Range);
        }

        fields.Note("Synthetic document root; select a section to inspect its fields.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult ParseHeader(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = Reader(context, node);
        if (reader.Remaining < PsdConstants.HeaderLength)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"The file header needs {PsdConstants.HeaderLength} bytes but only {reader.Remaining} are available.",
                node.Range);
            return Result(node, fields);
        }

        Span<byte> signature = stackalloc byte[PsdConstants.SignatureLength];
        _ = reader.TryReadBytes(signature);
        fields.Raw("Signature", RawIdentifier.FormatBytes(signature), reader.LastRange);
        if (!signature.SequenceEqual(PsdConstants.Signature))
        {
            fields.Warning(DiagnosticCodes.InvalidSignature, "The Photoshop document must start with '8BPS'.", reader.LastRange);
        }

        _ = UInt16(reader, fields, "Version", out var version, PsdConstants.FormatVersion);
        Span<byte> reserved = stackalloc byte[PsdConstants.ReservedLength];
        if (reader.TryReadBytes(reserved))
        {
            fields.Raw("Reserved", HexFormatting.Format(reserved, reserved.Length), reader.LastRange);
        }

        _ = UInt16(reader, fields, "Channels", out _)
            && UInt32(reader, fields, "Height", out _)
            && UInt32(reader, fields, "Width", out _)
            && UInt16(reader, fields, "Depth", out _)
            && UInt16(reader, fields, "Color Mode", out _, PsdConstants.FormatColorMode);

        if (version is not (PsdConstants.VersionPsd or PsdConstants.VersionPsb))
        {
            fields.Warning(
                DiagnosticCodes.UnsupportedVersion,
                $"Version {version} is neither PSD (1) nor PSB (2).",
                node.Range);
        }

        return Result(node, fields);
    }

    private static NodeParseResult ParseColorModeData(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = Reader(context, node);
        if (!UInt32(reader, fields, "Length", out var length))
        {
            return Result(node, fields);
        }

        fields.Derived("<DataLength>", Math.Min(length, reader.Remaining), reader.CurrentRange);
        if (length > 0)
        {
            fields.Note("Color mode data bytes are not interpreted; they remain available in Hex.", reader.CurrentRange);
        }

        SkipRemaining(reader, fields);
        return Result(node, fields);
    }

    private static NodeParseResult ParseImageResources(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = Reader(context, node);
        _ = UInt32(reader, fields, "Length", out _);
        fields.Derived("<ResourceCount>", node.Children.Length, node.Range);
        fields.Note("Each image resource is listed as a child node.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult ParseResource(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        if (!PhotoshopImageResources.TryReadBlock(
                context.Source,
                node.Range.Offset,
                node.Range.Offset + node.Range.Length,
                out var block,
                out var error))
        {
            fields.Warning(DiagnosticCodes.InvalidSignature, error, node.Range);
            return Result(node, fields);
        }

        PhotoshopImageResources.ParseBlock(context, fields, block);
        return Result(node, fields);
    }

    private static NodeParseResult ParseLayerAndMaskInformation(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = Reader(context, node);
        var psb = IsPsb(context);
        if (!ReadSectionLength(reader, fields, psb, "Length", out _))
        {
            return Result(node, fields);
        }

        fields.Derived("<PayloadLength>", node.PayloadRange?.Length ?? 0, node.PayloadRange);
        fields.Note("Layer info, global layer mask info and additional layer information are child nodes.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult ParseLayerInfo(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = Reader(context, node);
        var psb = IsPsb(context);
        if (!ReadSectionLength(reader, fields, psb, "Length", out _))
        {
            return Result(node, fields);
        }

        fields.Derived("<PayloadLength>", node.PayloadRange?.Length ?? 0, node.PayloadRange);
        fields.Note("Layer records and channel image data are child nodes.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult ParseLayerCount(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = Reader(context, node);
        if (reader.TryInt16BE(out var count))
        {
            fields.Raw("Layer count", count, reader.LastRange);
            fields.Derived("<LayerCount>", Math.Abs((int)count), reader.LastRange);
            fields.Derived(
                "<FirstAlphaIsTransparency>",
                count < 0 ? "yes" : "no",
                reader.LastRange);
        }
        else
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The layer count is truncated.", node.Range);
        }

        return Result(node, fields);
    }

    private static NodeParseResult ParseLayerRecords(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        fields.Derived("<RecordCount>", node.Children.Length, node.Range);
        fields.Note("Each layer record is a child node in file order.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult ParseLayerRecord(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        fields.Derived("<ChildCount>", node.Children.Length, node.Range);
        fields.Note("The fixed layer fields and the extra data parts are child nodes.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult ParseLayerRecordHeader(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = Reader(context, node);
        var psb = IsPsb(context);

        if (!Int32(reader, fields, "Top", out _)
            || !Int32(reader, fields, "Left", out _)
            || !Int32(reader, fields, "Bottom", out _)
            || !Int32(reader, fields, "Right", out _)
            || !UInt16(reader, fields, "Number of channels", out var channelCount))
        {
            return Result(node, fields);
        }

        for (var index = 0; index < channelCount; index++)
        {
            if (!Int16(reader, fields, $"Channel ID[{index}]", out _, PsdConstants.FormatChannelId))
            {
                return Result(node, fields);
            }

            _ = psb
                ? UInt64(reader, fields, $"Channel data length[{index}]", out _)
                : UInt32(reader, fields, $"Channel data length[{index}]", out _);
        }

        Span<byte> signature = stackalloc byte[4];
        if (!Require(reader, fields, 4, "Blend mode signature") || !reader.TryReadBytes(signature))
        {
            return Result(node, fields);
        }

        fields.Raw("Blend mode signature", RawIdentifier.FormatBytes(signature), reader.LastRange);
        if (!signature.SequenceEqual(PsdConstants.AdditionalSignature))
        {
            fields.Warning(
                DiagnosticCodes.InvalidSignature,
                "The blend mode signature must be '8BIM'.",
                reader.LastRange);
        }

        Span<byte> blendKey = stackalloc byte[4];
        if (!Require(reader, fields, 4, "Blend mode key") || !reader.TryReadBytes(blendKey))
        {
            return Result(node, fields);
        }

        fields.Raw("Blend mode key", PsdConstants.FormatBlendMode(blendKey), reader.LastRange);
        byte flags = 0;
        var flagsRange = node.Range;
        _ = Byte(reader, fields, "Opacity", out _);
        _ = Byte(reader, fields, "Clipping", out _);
        if (Byte(reader, fields, "Flags", out flags))
        {
            flagsRange = reader.LastRange;
        }

        _ = Byte(reader, fields, "Filler", out _);
        fields.Derived("<Flags>", FormatFlags(flags), flagsRange);
        _ = UInt32(reader, fields, "Extra data length", out _);
        SkipRemaining(reader, fields);
        return Result(node, fields);
    }

    private static NodeParseResult ParseLayerMaskData(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = Reader(context, node);
        if (!UInt32(reader, fields, "Size of the data", out var size))
        {
            return Result(node, fields);
        }

        fields.Derived("<DataLength>", Math.Min(size, reader.Remaining), reader.CurrentRange);
        if (size >= 16)
        {
            _ = Int32(reader, fields, "Top", out _)
                && Int32(reader, fields, "Left", out _)
                && Int32(reader, fields, "Bottom", out _)
                && Int32(reader, fields, "Right", out _);
        }

        if (size >= 18)
        {
            _ = Byte(reader, fields, "Default color", out _)
                && Byte(reader, fields, "Flags", out _);
        }

        if (size >= 20)
        {
            fields.Note("Mask parameters and real-mask fields are not interpreted.", reader.CurrentRange);
        }

        SkipRemaining(reader, fields);
        return Result(node, fields);
    }

    private static NodeParseResult ParseLayerBlendingRanges(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = Reader(context, node);
        if (!UInt32(reader, fields, "Length of layer blending ranges data", out var length))
        {
            return Result(node, fields);
        }

        fields.Derived("<DataLength>", Math.Min(length, reader.Remaining), reader.CurrentRange);
        if (length > 0)
        {
            fields.Note("Blending range pairs are not interpreted.", reader.CurrentRange);
        }

        SkipRemaining(reader, fields);
        return Result(node, fields);
    }

    private static NodeParseResult ParseLayerName(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = Reader(context, node);
        if (!PascalName(context, reader, fields, "Layer name"))
        {
            return Result(node, fields);
        }

        SkipRemaining(reader, fields);
        return Result(node, fields);
    }

    private static NodeParseResult ParseAdditionalInfo(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = Reader(context, node);
        var psb = IsPsb(context);

        Span<byte> signature = stackalloc byte[4];
        Span<byte> key = stackalloc byte[4];
        if (!Require(reader, fields, 8, "Signature") || !reader.TryReadBytes(signature))
        {
            return Result(node, fields);
        }

        fields.Raw("Signature", RawIdentifier.FormatBytes(signature), reader.LastRange);
        if (!signature.SequenceEqual(PsdConstants.AdditionalSignature)
            && !signature.SequenceEqual(PsdConstants.AdditionalLargeSignature))
        {
            fields.Warning(
                DiagnosticCodes.InvalidSignature,
                "The additional layer information signature must be '8BIM' or '8B64'.",
                reader.LastRange);
        }

        if (!reader.TryReadBytes(key))
        {
            return Result(node, fields);
        }

        fields.Raw("Key", PsdConstants.FormatAdditionalInfoKey(key), reader.LastRange);

        var longLength = psb && PsdConstants.UsesLongAdditionalLength(key);
        ulong length;
        if (longLength)
        {
            if (!UInt64(reader, fields, "Length", out length))
            {
                return Result(node, fields);
            }
        }
        else
        {
            if (!UInt32(reader, fields, "Length", out var length32))
            {
                return Result(node, fields);
            }

            length = length32;
        }

        fields.Derived("<DataLength>", (long)Math.Min(length, (ulong)reader.Remaining), reader.CurrentRange);
        ParseAdditionalInfoData(context, fields, reader, key);
        SkipRemaining(reader, fields);
        return Result(node, fields);
    }

    private static void ParseAdditionalInfoData(
        ParseContext context,
        FieldList fields,
        BigEndianReader reader,
        ReadOnlySpan<byte> key)
    {
        if (key.SequenceEqual("luni"u8))
        {
            _ = UnicodeString(context, reader, fields, "Unicode layer name");
        }
        else if (key.SequenceEqual("lyid"u8))
        {
            _ = UInt32(reader, fields, "Layer ID", out _);
        }
        else if (key.SequenceEqual("lspf"u8))
        {
            if (UInt32(reader, fields, "Protected setting", out var flags))
            {
                fields.Derived("<ProtectedFlags>", FormatProtectedFlags(flags), reader.LastRange);
            }
        }
        else if (key.SequenceEqual("lsct"u8) || key.SequenceEqual("lsdk"u8))
        {
            if (!UInt32(reader, fields, "Type", out var type))
            {
                return;
            }

            fields.Derived("<SectionType>", PsdConstants.FormatSectionType(type), reader.LastRange);
            if (reader.Remaining >= 8)
            {
                Span<byte> signature = stackalloc byte[4];
                Span<byte> blendKey = stackalloc byte[4];
                if (reader.TryReadBytes(signature))
                {
                    fields.Raw("Blend mode signature", RawIdentifier.FormatBytes(signature), reader.LastRange);
                }

                if (reader.TryReadBytes(blendKey))
                {
                    fields.Raw("Blend mode key", PsdConstants.FormatBlendMode(blendKey), reader.LastRange);
                }
            }
        }
        else if (key.SequenceEqual("clbl"u8) || key.SequenceEqual("infx"u8) || key.SequenceEqual("knko"u8))
        {
            _ = Boolean(reader, fields, "Value");
            SkipPadding(reader, fields, 3);
        }
        else if (key.SequenceEqual("iOpa"u8))
        {
            _ = Byte(reader, fields, "Fill opacity", out _);
        }
        else if (key.SequenceEqual("lyvr"u8))
        {
            _ = UInt32(reader, fields, "Version", out _);
        }
        else if (key.SequenceEqual("fxrp"u8))
        {
            _ = Double(reader, fields, "X");
            _ = Double(reader, fields, "Y");
        }
        else
        {
            fields.Note(
                $"Additional layer information '{PsdConstants.FormatAdditionalInfoKey(key)}' is not interpreted.",
                reader.CurrentRange);
        }
    }

    private static NodeParseResult ParseChannelImageData(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        fields.Derived("<ChannelCount>", node.Children.Length, node.Range);
        fields.Note("Each channel data record is a child node; pixel bytes are not decoded.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult ParseChannelData(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = Reader(context, node);
        if (UInt16(reader, fields, "Compression", out var compression, PsdConstants.FormatCompression))
        {
            fields.Derived("<DataLength>", reader.Remaining, reader.CurrentRange);
            if (compression == 1)
            {
                fields.Note("RLE row byte counts and PackBits data are not interpreted.", reader.CurrentRange);
            }
            else if (compression is 2 or 3)
            {
                fields.Note("ZIP-compressed channel data is not interpreted.", reader.CurrentRange);
            }
            else
            {
                fields.Note("Raw channel pixel data is not interpreted.", reader.CurrentRange);
            }
        }

        SkipRemaining(reader, fields);
        return Result(node, fields);
    }

    private static NodeParseResult ParseGlobalLayerMaskInfo(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = Reader(context, node);
        if (!UInt32(reader, fields, "Length of global layer mask info section", out var length))
        {
            return Result(node, fields);
        }

        fields.Derived("<DataLength>", Math.Min(length, reader.Remaining), reader.CurrentRange);
        if (length >= 2)
        {
            _ = UInt16(reader, fields, "Overlay color space", out _);
        }

        if (length >= 10)
        {
            for (var index = 0; index < 4; index++)
            {
                if (!UInt16(reader, fields, $"Color component[{index}]", out _))
                {
                    break;
                }
            }
        }

        if (length >= 12)
        {
            _ = UInt16(reader, fields, "Opacity", out _);
        }

        if (length >= 13)
        {
            _ = Byte(reader, fields, "Kind", out _);
        }

        SkipRemaining(reader, fields);
        return Result(node, fields);
    }

    private static NodeParseResult ParseImageData(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var reader = Reader(context, node);
        if (UInt16(reader, fields, "Compression method", out var compression, PsdConstants.FormatCompression))
        {
            fields.Derived("<DataLength>", reader.Remaining, reader.CurrentRange);
            if (compression == 1)
            {
                fields.Note("RLE row byte counts and PackBits data are not interpreted.", reader.CurrentRange);
            }
            else if (compression is 2 or 3)
            {
                fields.Note("ZIP-compressed image data is not interpreted.", reader.CurrentRange);
            }
            else
            {
                fields.Note("Raw planar image data is not interpreted.", reader.CurrentRange);
            }
        }

        SkipRemaining(reader, fields);
        return Result(node, fields);
    }

    private static NodeParseResult Placeholder(FormatNode node)
    {
        var fields = new FieldList(1);
        fields.Note("This node has no dedicated parser.", node.Range);
        return new NodeParseResult(node.Name, node.Range, node.Range, fields.ToImmutable());
    }

    private static BigEndianReader Reader(ParseContext context, FormatNode node) =>
        new(context.Source, node.Range.Offset, node.Range.Length);

    private static bool IsPsb(ParseContext context)
    {
        Span<byte> version = stackalloc byte[2];
        return SourceRead.ReadAtMost(context.Source, 4, version) == 2
            && System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(version) == PsdConstants.VersionPsb;
    }

    private static bool ReadSectionLength(
        BigEndianReader reader,
        FieldList fields,
        bool psb,
        string key,
        out long length)
    {
        if (psb)
        {
            if (!UInt64(reader, fields, key, out var value))
            {
                length = 0;
                return false;
            }

            length = value > long.MaxValue ? long.MaxValue : (long)value;
            return true;
        }

        if (!UInt32(reader, fields, key, out var small))
        {
            length = 0;
            return false;
        }

        length = small;
        return true;
    }

    private static bool PascalName(
        ParseContext context,
        BigEndianReader reader,
        FieldList fields,
        string key)
    {
        if (!Byte(reader, fields, key + " length", out var length))
        {
            return false;
        }

        if (!Require(reader, fields, length, key))
        {
            return false;
        }

        var bytes = reader.ReadPrefix(length, context.Limits.MaxStringBytes, out var range);
        var text = TextDecoding.Utf8(bytes, bytes.Length == length, out var invalid);
        if (invalid)
        {
            fields.Warning(
                DiagnosticCodes.InvalidEncoding,
                $"'{key}' is not valid UTF-8; invalid bytes are shown as replacement characters.",
                range);
        }

        fields.Raw(key, TextEscaping.Escape(text), range);
        if (bytes.Length < length)
        {
            fields.Warning(DiagnosticCodes.LimitString, $"'{key}' is shown as a prefix.", range);
        }

        var total = 1 + length;
        var padding = (int)((4 - (total & 3)) & 3);
        return SkipPadding(reader, fields, padding);
    }

    private static bool UnicodeString(
        ParseContext context,
        BigEndianReader reader,
        FieldList fields,
        string key)
    {
        if (!UInt32(reader, fields, key + " length", out var units))
        {
            return false;
        }

        var byteLength = units * 2L;
        if (!Require(reader, fields, byteLength, key))
        {
            return false;
        }

        var limit = context.Limits.MaxStringBytes & ~1;
        var bytes = reader.ReadPrefix(byteLength, limit, out var range);
        fields.Raw(key, TextDecoding.Display(bytes, Encoding.BigEndianUnicode), range);
        if (bytes.Length < byteLength)
        {
            fields.Warning(DiagnosticCodes.LimitString, $"'{key}' is shown as a prefix.", range);
        }

        // Real PSD files store only the counted UTF-16 units; do not consume a guessed null.
        return true;
    }

    private static bool SkipPadding(BigEndianReader reader, FieldList fields, int count)
    {
        if (count <= 0)
        {
            return true;
        }

        if (!Require(reader, fields, count, "Padding"))
        {
            return false;
        }

        reader.Skip(count);
        return true;
    }

    private static void SkipRemaining(BigEndianReader reader, FieldList fields)
    {
        if (reader.Remaining > 0)
        {
            fields.Derived("<UnparsedLength>", reader.Remaining, reader.CurrentRange);
            reader.SkipRemaining();
        }
    }

    private static bool Require(BigEndianReader reader, FieldList fields, long length, string key)
    {
        if (reader.Remaining >= length)
        {
            return true;
        }

        fields.Warning(DiagnosticCodes.TruncatedPayload, $"Field '{key}' is truncated.", reader.CurrentRange);
        return false;
    }

    private static bool Byte(BigEndianReader reader, FieldList fields, string key, out byte value)
    {
        value = 0;
        if (!Require(reader, fields, 1, key) || !reader.TryByte(out value))
        {
            return false;
        }

        fields.Raw(key, value, reader.LastRange);
        return true;
    }

    private static bool Boolean(BigEndianReader reader, FieldList fields, string key)
    {
        if (!Require(reader, fields, 1, key) || !reader.TryByte(out var value))
        {
            return false;
        }

        fields.Raw(
            key,
            string.Create(CultureInfo.InvariantCulture, $"{value} ({(value == 0 ? "false" : "true")})"),
            reader.LastRange);
        return true;
    }

    private static bool UInt16(
        BigEndianReader reader,
        FieldList fields,
        string key,
        out ushort value,
        Func<ushort, string>? format = null)
    {
        value = 0;
        if (!Require(reader, fields, 2, key) || !reader.TryUInt16BE(out value))
        {
            return false;
        }

        fields.Raw(key, format?.Invoke(value) ?? value.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        return true;
    }

    private static bool Int16(
        BigEndianReader reader,
        FieldList fields,
        string key,
        out short value,
        Func<short, string>? format = null)
    {
        value = 0;
        if (!Require(reader, fields, 2, key) || !reader.TryInt16BE(out value))
        {
            return false;
        }

        fields.Raw(key, format?.Invoke(value) ?? value.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        return true;
    }

    private static bool UInt32(BigEndianReader reader, FieldList fields, string key, out uint value)
    {
        value = 0;
        if (!Require(reader, fields, 4, key) || !reader.TryUInt32BE(out value))
        {
            return false;
        }

        fields.Raw(key, value, reader.LastRange);
        return true;
    }

    private static bool UInt64(BigEndianReader reader, FieldList fields, string key, out ulong value)
    {
        value = 0;
        if (!Require(reader, fields, 8, key) || !reader.TryUInt64BE(out value))
        {
            return false;
        }

        fields.Raw(key, value, reader.LastRange);
        return true;
    }

    private static bool Int32(BigEndianReader reader, FieldList fields, string key, out int value)
    {
        value = 0;
        if (!Require(reader, fields, 4, key) || !reader.TryUInt32BE(out var raw))
        {
            return false;
        }

        value = unchecked((int)raw);
        fields.Raw(key, value, reader.LastRange);
        return true;
    }

    private static bool Double(BigEndianReader reader, FieldList fields, string key)
    {
        if (!Require(reader, fields, 8, key) || !reader.TryDoubleBE(out var value))
        {
            return false;
        }

        fields.Raw(key, value.ToString("R", CultureInfo.InvariantCulture), reader.LastRange);
        return true;
    }

    private static string FormatFlags(byte flags)
    {
        var names = new List<string>(5);
        if ((flags & 0x01) != 0)
        {
            names.Add("transparency protected");
        }

        if ((flags & 0x02) != 0)
        {
            names.Add("visible");
        }

        if ((flags & 0x04) != 0)
        {
            names.Add("obsolete");
        }

        if ((flags & 0x08) != 0)
        {
            names.Add("bit 4 is useful");
        }

        if ((flags & 0x10) != 0)
        {
            names.Add("pixel data irrelevant to appearance");
        }

        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    private static string FormatProtectedFlags(uint flags)
    {
        var names = new List<string>(5);
        if ((flags & 0x01) != 0)
        {
            names.Add("transparency protected");
        }

        if ((flags & 0x02) != 0)
        {
            names.Add("composite protected");
        }

        if ((flags & 0x04) != 0)
        {
            names.Add("position protected");
        }

        if ((flags & 0x08) != 0)
        {
            names.Add("artboards protected");
        }

        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    private static NodeParseResult Result(FormatNode node, FieldList fields, string? title = null) =>
        new(
            title ?? node.Name,
            node.Range,
            node.Range,
            fields.ToImmutable(),
            ImmutableArray.CreateRange(fields.Diagnostics));
}
