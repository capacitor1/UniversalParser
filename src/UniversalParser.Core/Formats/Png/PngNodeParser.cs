using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Embedded;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Png;

/// <summary>
/// Re-reads one PNG chunk on demand. The tree phase stores ranges only, so selecting a chunk still
/// works after the document parse has finished. Chunk data is read within the chunk range; the CRC
/// is streamed over the type and data bytes, and image/frame payloads are never decompressed.
/// </summary>
internal static class PngNodeParser
{
    private const int MaxKeywordBytes = 79;
    private const int MaxPaletteEntries = 256;

    public static NodeParseResult Parse(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        if (node.StableId == "png-root")
        {
            return ParseRoot(context, node);
        }

        if (node.StableId == "png:signature")
        {
            return ParseSignature(context, node);
        }

        if (TryGetChunkOffset(node.StableId, out var offset))
        {
            return ParseChunk(context, node, offset);
        }

        return Placeholder(node);
    }

    private static NodeParseResult ParseRoot(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        fields.Derived("<FileSize>", node.Range.Length, node.Range);
        fields.Derived(
            "<ChunkCount>",
            node.Children.Count(static child => child.StableId?.StartsWith("png:chunk:", StringComparison.Ordinal) == true),
            node.Range);
        if (PngImageHeader.TryRead(context.Source, out var header))
        {
            fields.Derived("<ImageWidth>", header.Width, node.Range);
            fields.Derived("<ImageHeight>", header.Height, node.Range);
            fields.Derived("<BitDepth>", header.BitDepth, node.Range);
            fields.Derived("<ColorType>", PngFormatting.FormatColorType(header.ColorType), node.Range);
            fields.Derived(
                "<InterlaceMethod>",
                PngFormatting.FormatInterlaceMethod(header.InterlaceMethod),
                node.Range);
        }
        else
        {
            fields.Note("The IHDR chunk is missing or truncated; image dimensions are unavailable.", node.Range);
        }

        fields.Note("Synthetic document root; select a chunk to inspect its fields.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult ParseSignature(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        var count = (int)Math.Min(node.Range.Length, PngConstants.SignatureLength);
        Span<byte> signature = stackalloc byte[PngConstants.SignatureLength];
        var read = SourceRead.ReadAtMost(context.Source, node.Range.Offset, signature[..count]);
        if (read == 0)
        {
            fields.Warning(DiagnosticCodes.TruncatedHeader, "The PNG signature could not be read.", node.Range);
            return Result(node, fields);
        }

        var bytes = signature[..read];
        var title = $"Signature '{RawIdentifier.FormatBytes(bytes)}'";
        if (read < PngConstants.SignatureLength)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"The PNG signature needs {PngConstants.SignatureLength} bytes but only {read} are available.",
                node.Range);
            return Result(node, fields, title);
        }

        var valid = bytes.SequenceEqual(PngConstants.Signature);
        fields.Derived("<Valid>", valid ? "yes" : "no");
        if (!valid)
        {
            fields.Warning(DiagnosticCodes.InvalidSignature, "The PNG signature bytes are wrong.", node.Range);
        }

        return Result(node, fields, title);
    }

    private static NodeParseResult ParseChunk(ParseContext context, FormatNode node, long offset)
    {
        var fields = context.CreateFieldList();
        if (node.Range.Length < PngConstants.ChunkHeaderLength
            || !PngChunkHeader.TryRead(context.Source, offset, node.Range.End, out var header))
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"The chunk header needs {PngConstants.ChunkHeaderLength} bytes but only {node.Range.Length} are available.",
                node.Range);
            return Result(node, fields);
        }

        var title = PngConstants.GetTitle(header.Type);
        fields.Raw("Length", header.Length, new SourceRange(offset, 4));
        var dataCapacity = Math.Max(0, node.Range.Length - PngConstants.ChunkHeaderLength);
        var dataLength = Math.Min(header.Length, dataCapacity);
        var dataRange = new SourceRange(header.DataOffset, dataLength);
        fields.Derived("<PayloadLength>", dataLength, dataRange);

        if (!header.IsLengthValid)
        {
            fields.Warning(
                DiagnosticCodes.InvalidLength,
                $"Chunk length {header.Length} exceeds the PNG maximum of {PngConstants.MaxChunkLength}.",
                new SourceRange(offset, PngConstants.ChunkHeaderLength));
            return Result(node, fields, title);
        }

        if (dataLength < header.Length)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The chunk declares {header.Length} data byte(s), but only {dataLength} are available."),
                new SourceRange(header.DataOffset, dataLength));
        }

        var data = new PngChunkDataReader(context.Source, header.DataOffset, dataLength);
        if (PngConstants.IsKnown(header.Type))
        {
            ParseChunkData(context, fields, data, node, header.Type);
        }
        else
        {
            fields.Note("Unknown chunk type; the payload is not interpreted.", dataRange);
            data.SkipRemaining();
        }

        AppendCrc(context, fields, header, node, dataLength);
        return Result(node, fields, title);
    }

    private static void ParseChunkData(
        ParseContext context,
        FieldList fields,
        PngChunkDataReader data,
        FormatNode node,
        string type)
    {
        switch (type)
        {
            case "IHDR":
                ParseImageHeader(context, fields, data);
                break;
            case "PLTE":
                ParsePalette(context, fields, data);
                break;
            case "IDAT":
                data.SkipRemaining();
                fields.Note("Compressed image data is not decoded.", node.Range);
                break;
            case "IEND":
                RequireEmpty(fields, data, "IEND");
                break;
            case "tRNS":
                ParseTransparency(context, fields, data);
                break;
            case "cHRM":
                ParseChromaticities(fields, data);
                break;
            case "gAMA":
                ParseGamma(fields, data);
                break;
            case "iCCP":
                ParseEmbeddedProfile(context, fields, data);
                break;
            case "sBIT":
                ParseSignificantBits(context, fields, data);
                break;
            case "sRGB":
                ParseSrgb(fields, data);
                break;
            case "cICP":
                ParseCodingIndependentCodePoints(fields, data);
                break;
            case "mDCV":
                ParseMasteringDisplayColorVolume(fields, data);
                break;
            case "cLLI":
                ParseContentLightLevelInformation(fields, data);
                break;
            case "bKGD":
                ParseBackground(context, fields, data);
                break;
            case "hIST":
                ParseHistogram(context, fields, data);
                break;
            case "pHYs":
                ParsePhysicalDimensions(fields, data);
                break;
            case "sPLT":
                ParseSuggestedPalette(context, fields, data);
                break;
            case "eXIf":
                ParseExif(context, fields, data);
                break;
            case "tIME":
                ParseTime(fields, data);
                break;
            case "tEXt":
                ParseText(context, fields, data);
                break;
            case "zTXt":
                ParseCompressedText(context, fields, data);
                break;
            case "iTXt":
                ParseInternationalText(context, fields, data);
                break;
            case "acTL":
                ParseAnimationControl(fields, data);
                break;
            case "fcTL":
                ParseFrameControl(fields, data);
                break;
            case "fdAT":
                ParseFrameData(fields, data);
                break;
            case "oFFs":
                ParseImageOffset(fields, data);
                break;
            default:
                fields.Note("This known chunk type is not interpreted in this version.", node.Range);
                data.SkipRemaining();
                break;
        }
    }

    private static void ParseImageHeader(ParseContext context, FieldList fields, PngChunkDataReader data)
    {
        if (!TryUInt32(fields, data, "Width", out var width)
            || !TryUInt32(fields, data, "Height", out var height)
            || !TryByte(fields, data, "Bit depth", out var bitDepth)
            || !TryByteMeaning(fields, data, "Color type", out var colorType, PngFormatting.FormatColorType)
            || !TryByteMeaning(fields, data, "Compression method", out var compression, PngFormatting.FormatCompressionMethod)
            || !TryByteMeaning(fields, data, "Filter method", out var filter, PngFormatting.FormatFilterMethod)
            || !TryByteMeaning(fields, data, "Interlace method", out var interlace, PngFormatting.FormatInterlaceMethod))
        {
            return;
        }

        if (width == 0 || width > PngConstants.MaxChunkLength)
        {
            fields.Warning(DiagnosticCodes.InvalidLength, $"Width {width} is outside the PNG range 1..{PngConstants.MaxChunkLength}.", data.LastRange);
        }

        if (height == 0 || height > PngConstants.MaxChunkLength)
        {
            fields.Warning(DiagnosticCodes.InvalidLength, $"Height {height} is outside the PNG range 1..{PngConstants.MaxChunkLength}.", data.LastRange);
        }

        if (!PngFormatting.IsValidColorType(colorType))
        {
            fields.Warning(DiagnosticCodes.SchemaMismatch, $"Color type {colorType} is not defined by PNG.", data.LastRange);
        }
        else if (!PngFormatting.IsValidBitDepth(colorType, bitDepth))
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                $"Bit depth {bitDepth} is not allowed for color type {colorType} ({PngFormatting.FormatColorType(colorType)}).",
                data.LastRange);
        }

        if (compression != 0)
        {
            fields.Warning(DiagnosticCodes.InvalidEncoding, $"Compression method {compression} is not defined.", data.LastRange);
        }

        if (filter != 0)
        {
            fields.Warning(DiagnosticCodes.InvalidEncoding, $"Filter method {filter} is not defined.", data.LastRange);
        }

        if (interlace > 1)
        {
            fields.Warning(DiagnosticCodes.InvalidEncoding, $"Interlace method {interlace} is not defined.", data.LastRange);
        }

        RequireExactLength(fields, data, "IHDR", 13, context.Limits);
    }

    private static void ParsePalette(ParseContext context, FieldList fields, PngChunkDataReader data)
    {
        var byteLength = data.Remaining;
        if (byteLength % 3 != 0)
        {
            fields.Warning(
                DiagnosticCodes.InvalidLength,
                $"PLTE length {byteLength} is not divisible by 3.",
                data.CurrentRange);
        }

        var entries = byteLength / 3;
        if (entries > MaxPaletteEntries)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                $"PLTE contains {entries} entries; the PNG maximum is {MaxPaletteEntries}.",
                data.CurrentRange);
        }

        var shown = Math.Min(entries, context.Limits.MaxDataLines / 4);
        for (var index = 0L; index < shown; index++)
        {
            context.ThrowIfCancellationRequested();
            var entryStart = data.Position;
            if (!TryByte(fields, data, EntryKey(index, "Red"), out var red)
                || !TryByte(fields, data, EntryKey(index, "Green"), out var green)
                || !TryByte(fields, data, EntryKey(index, "Blue"), out var blue))
            {
                return;
            }

            // PLTE samples are always 8-bit RGB regardless of the image bit depth.
            fields.Derived(
                $"<{EntryKey(index, "Color")}>",
                $"#{red:X2}{green:X2}{blue:X2}",
                new SourceRange(entryStart, 3));
        }

        fields.Derived("<PaletteEntryCount>", entries, data.CurrentRange);
        if (entries > shown)
        {
            fields.Warning(
                DiagnosticCodes.LimitRecords,
                $"PLTE has {entries} entries; only the first {shown} are shown.",
                data.CurrentRange);
            data.SkipRemaining();
        }
        else if (byteLength % 3 != 0)
        {
            data.SkipRemaining();
        }
    }

    private static void ParseTransparency(ParseContext context, FieldList fields, PngChunkDataReader data)
    {
        if (!PngImageHeader.TryRead(context.Source, out var image))
        {
            fields.Note("tRNS interpretation requires an IHDR chunk.", data.CurrentRange);
            data.SkipRemaining();
            return;
        }

        switch (image.ColorType)
        {
            case 0:
                if (TryUInt16(fields, data, "Grey sample value", out var grey))
                {
                    AppendSampleMeaning(fields, image.BitDepth, "<TransparentSample>", grey, data.LastRange);
                }

                break;
            case 2:
                var sampleStart = data.Position;
                if (TryUInt16(fields, data, "Red sample value", out var red)
                    && TryUInt16(fields, data, "Green sample value", out var green)
                    && TryUInt16(fields, data, "Blue sample value", out var blue))
                {
                    if (TrySampleMask(image.BitDepth, out var mask))
                    {
                        fields.Derived(
                            "<TransparentSample>",
                            string.Create(CultureInfo.InvariantCulture,
                                $"R={red & mask}, G={green & mask}, B={blue & mask} ({image.BitDepth}-bit samples)"),
                            new SourceRange(sampleStart, 6));
                    }
                }

                break;
            case 3:
                var alphaCount = data.Remaining;
                if (alphaCount > MaxPaletteEntries)
                {
                    fields.Warning(
                        DiagnosticCodes.SchemaMismatch,
                        $"tRNS has {alphaCount} alpha entries; the PNG maximum is {MaxPaletteEntries}.",
                        data.CurrentRange);
                }

                var limit = Math.Min(alphaCount, context.Limits.MaxDataLines);
                for (var index = 0L; index < limit; index++)
                {
                    context.ThrowIfCancellationRequested();
                    if (!TryByte(fields, data, string.Create(CultureInfo.InvariantCulture, $"Alpha for palette index {index}"), out _))
                    {
                        return;
                    }
                }

                fields.Derived("<PaletteAlphaEntryCount>", alphaCount, data.CurrentRange);
                if (alphaCount > 0)
                {
                    fields.Note(
                        "Palette entries without an alpha value in tRNS are fully opaque (alpha 255).",
                        data.CurrentRange);
                }

                if (alphaCount > limit)
                {
                    fields.Warning(DiagnosticCodes.LimitRecords,
                        $"tRNS has {alphaCount} alpha entries; only the first {limit} are shown.", data.CurrentRange);
                }

                data.SkipRemaining();
                return;
            default:
                fields.Derived("<UnparsedLength>", data.Remaining, data.CurrentRange);
                fields.Note("The IHDR color type does not select a tRNS layout; the bytes remain available in Hex.", data.CurrentRange);
                data.SkipRemaining();
                return;
        }

        if (data.Remaining > 0)
        {
            fields.Warning(DiagnosticCodes.SchemaMismatch, $"tRNS has {data.Remaining} unexpected trailing byte(s).", data.CurrentRange);
            data.SkipRemaining();
        }
    }

    private static void ParseChromaticities(FieldList fields, PngChunkDataReader data)
    {
        if (!TryFixed(fields, data, "White point x", out _)
            || !TryFixed(fields, data, "White point y", out _)
            || !TryFixed(fields, data, "Red x", out _)
            || !TryFixed(fields, data, "Red y", out _)
            || !TryFixed(fields, data, "Green x", out _)
            || !TryFixed(fields, data, "Green y", out _)
            || !TryFixed(fields, data, "Blue x", out _)
            || !TryFixed(fields, data, "Blue y", out _))
        {
            return;
        }

        RequireEmpty(fields, data, "cHRM");
    }

    private static void ParseGamma(FieldList fields, PngChunkDataReader data)
    {
        if (!TryFixed(fields, data, "Image gamma", out _))
        {
            return;
        }

        RequireEmpty(fields, data, "gAMA");
    }

    private static void ParseEmbeddedProfile(ParseContext context, FieldList fields, PngChunkDataReader data)
    {
        if (!TryReadNullString(
                fields,
                data,
                "Profile name",
                MaxKeywordBytes,
                Encoding.Latin1,
                validateUtf8: false,
                allowEmpty: false,
                out _))
        {
            return;
        }

        if (!TryByteMeaning(fields, data, "Compression method", out var method, PngFormatting.FormatCompressionMethod))
        {
            return;
        }

        if (method != 0)
        {
            fields.Warning(DiagnosticCodes.InvalidEncoding, $"iCCP compression method {method} is not defined.", data.LastRange);
        }

        var profile = DecompressRemaining(
            context,
            fields,
            data,
            "<CompressedProfileLength>",
            (int)Math.Min(context.Limits.MaxBlobBytes, int.MaxValue),
            DiagnosticCodes.LimitBlob,
            "The embedded ICC profile",
            out _,
            out var range);
        if (profile is null)
        {
            return;
        }

        fields.Derived("<DecompressedProfileLength>", profile.Length, range);
        IccProfileParser.Parse(
            profile,
            fields,
            context.Limits,
            context.CancellationToken,
            profileOffset: null,
            fallbackRange: range);
    }

    private static void ParseSignificantBits(ParseContext context, FieldList fields, PngChunkDataReader data)
    {
        if (!PngImageHeader.TryRead(context.Source, out var image))
        {
            fields.Note("sBIT interpretation requires an IHDR chunk.", data.CurrentRange);
            data.SkipRemaining();
            return;
        }

        var sampleDepth = image.ColorType == 3 ? 8 : image.BitDepth;
        switch (image.ColorType)
        {
            case 0:
                ReadSignificantBits(fields, data, ["significant greyscale bits"], sampleDepth);
                break;
            case 2:
            case 3:
                ReadSignificantBits(
                    fields,
                    data,
                    ["significant red bits", "significant green bits", "significant blue bits"],
                    sampleDepth);
                break;
            case 4:
                ReadSignificantBits(fields, data, ["significant greyscale bits", "significant alpha bits"], sampleDepth);
                break;
            case 6:
                ReadSignificantBits(
                    fields,
                    data,
                    ["significant red bits", "significant green bits", "significant blue bits", "significant alpha bits"],
                    sampleDepth);
                break;
            default:
                fields.Warning(
                    DiagnosticCodes.SchemaMismatch,
                    $"sBIT is not defined for color type {image.ColorType}.",
                    data.CurrentRange);
                data.SkipRemaining();
                return;
        }

        RequireEmpty(fields, data, "sBIT");
    }

    private static void ReadSignificantBits(
        FieldList fields,
        PngChunkDataReader data,
        string[] names,
        int sampleDepth)
    {
        foreach (var name in names)
        {
            if (!TryByte(fields, data, name, out var value))
            {
                return;
            }

            if (value == 0 || value > sampleDepth)
            {
                fields.Warning(
                    DiagnosticCodes.SchemaMismatch,
                    $"{name} is {value}, outside 1..{sampleDepth}.",
                    data.LastRange);
            }
        }
    }

    private static void ParseSrgb(FieldList fields, PngChunkDataReader data)
    {
        if (!TryByteMeaning(fields, data, "Rendering intent", out var intent, PngFormatting.FormatRenderingIntent))
        {
            return;
        }

        if (intent > 3)
        {
            fields.Warning(DiagnosticCodes.InvalidEncoding, $"Rendering intent {intent} is not defined.", data.LastRange);
        }

        RequireEmpty(fields, data, "sRGB");
    }

    private static void ParseCodingIndependentCodePoints(FieldList fields, PngChunkDataReader data)
    {
        // PNG Third Edition §11.3.2.6: four one-byte ITU-T H.273 code points, in this order.
        if (!TryByteMeaning(fields, data, "Color primaries", out _, PngFormatting.FormatColorPrimaries)
            || !TryByteMeaning(fields, data, "Transfer function", out _, PngFormatting.FormatTransferFunction)
            || !TryByteMeaning(fields, data, "Matrix coefficients", out _, PngFormatting.FormatMatrixCoefficients)
            || !TryByteMeaning(fields, data, "Video full range flag", out var fullRange, PngFormatting.FormatVideoFullRangeFlag))
        {
            return;
        }

        if (fullRange > 1)
        {
            fields.Warning(
                DiagnosticCodes.InvalidEncoding,
                $"Video full range flag {fullRange} is not defined.",
                data.LastRange);
        }

        RequireEmpty(fields, data, "cICP");
    }

    private static void ParseMasteringDisplayColorVolume(FieldList fields, PngChunkDataReader data)
    {
        // PNG Third Edition §11.3.2.7: chromaticities use a divisor of 0.00002,
        // luminances use 0.0001 cd/m2.
        if (!TryChromaticity(fields, data, "Red x", out _)
            || !TryChromaticity(fields, data, "Red y", out _)
            || !TryChromaticity(fields, data, "Green x", out _)
            || !TryChromaticity(fields, data, "Green y", out _)
            || !TryChromaticity(fields, data, "Blue x", out _)
            || !TryChromaticity(fields, data, "Blue y", out _)
            || !TryChromaticity(fields, data, "White point x", out _)
            || !TryChromaticity(fields, data, "White point y", out _)
            || !TryLuminance(fields, data, "Maximum luminance", out _)
            || !TryLuminance(fields, data, "Minimum luminance", out _))
        {
            return;
        }

        RequireEmpty(fields, data, "mDCV");
    }

    private static void ParseContentLightLevelInformation(FieldList fields, PngChunkDataReader data)
    {
        // PNG Third Edition §11.3.2.8: MaxCLL and MaxFALL are uint32 / 0.0001 cd/m2.
        if (!TryLuminance(fields, data, "Maximum Content Light Level (MaxCLL)", out _)
            || !TryLuminance(fields, data, "Maximum Frame-Average Light Level (MaxFALL)", out _))
        {
            return;
        }

        RequireEmpty(fields, data, "cLLI");
    }

    private static bool TryChromaticity(
        FieldList fields,
        PngChunkDataReader data,
        string key,
        out ushort value)
    {
        if (!data.TryUInt16BE(out value))
        {
            return Truncated(fields, data, key);
        }

        fields.Raw(
            key,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{value} ({(value / 50000m).ToString("0.#####", CultureInfo.InvariantCulture)})"),
            data.LastRange);
        return true;
    }

    private static bool TryLuminance(
        FieldList fields,
        PngChunkDataReader data,
        string key,
        out uint value)
    {
        if (!data.TryUInt32BE(out value))
        {
            return Truncated(fields, data, key);
        }

        fields.Raw(
            key,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{value} ({(value / 10000m).ToString("0.####", CultureInfo.InvariantCulture)} cd/m2)"),
            data.LastRange);
        return true;
    }

    private static bool TrySampleMask(byte bitDepth, out uint mask)
    {
        mask = bitDepth is 1 or 2 or 4 or 8 or 16 ? (1u << bitDepth) - 1 : 0;
        return mask != 0;
    }

    private static void AppendSampleMeaning(FieldList fields, byte bitDepth, string key, uint sample, SourceRange range)
    {
        if (TrySampleMask(bitDepth, out var mask))
        {
            fields.Derived(key, string.Create(CultureInfo.InvariantCulture, $"{sample & mask} ({bitDepth}-bit sample)"), range);
        }
    }

    private static byte ScaleTo8(uint value, uint mask)
    {
        if (mask == 0)
        {
            return 0;
        }

        return checked((byte)((value * 255u + mask / 2) / mask));
    }

    private static string FormatColor(byte red, byte green, byte blue) => $"#{red:X2}{green:X2}{blue:X2}";

    private static bool TryReadPaletteEntry(
        ParseContext context,
        byte index,
        out byte red,
        out byte green,
        out byte blue)
    {
        red = 0;
        green = 0;
        blue = 0;
        var position = (long)PngConstants.SignatureLength;
        while (context.Source.Length - position >= PngConstants.ChunkOverhead)
        {
            context.ThrowIfCancellationRequested();
            if (!PngChunkHeader.TryRead(context.Source, position, context.Source.Length, out var header))
            {
                return false;
            }

            if (!header.IsComplete || !header.IsLengthValid)
            {
                return false;
            }

            if (header.Type == "PLTE")
            {
                var entryOffset = index * 3L;
                if (entryOffset + 3 > header.Length)
                {
                    return false;
                }

                Span<byte> entry = stackalloc byte[3];
                if (SourceRead.ReadAtMost(context.Source, header.DataOffset + entryOffset, entry) != entry.Length)
                {
                    return false;
                }

                red = entry[0];
                green = entry[1];
                blue = entry[2];
                return true;
            }

            if (header.Type is "IDAT" or "IEND")
            {
                return false;
            }

            position = header.End;
        }

        return false;
    }

    private static void ParseBackground(ParseContext context, FieldList fields, PngChunkDataReader data)
    {
        if (!PngImageHeader.TryRead(context.Source, out var image))
        {
            fields.Note("bKGD interpretation requires an IHDR chunk.", data.CurrentRange);
            data.SkipRemaining();
            return;
        }

        switch (image.ColorType)
        {
            case 3:
                if (!TryByte(fields, data, "Palette index", out var paletteIndex))
                {
                    return;
                }

                if (TryReadPaletteEntry(context, paletteIndex, out var paletteRed, out var paletteGreen, out var paletteBlue))
                {
                    fields.Derived("<BackgroundColor>", FormatColor(paletteRed, paletteGreen, paletteBlue), data.LastRange);
                }
                else
                {
                    fields.Note("The bKGD palette index has no matching PLTE entry.", data.LastRange);
                }

                break;
            case 0:
            case 4:
                if (TryUInt16(fields, data, "Greyscale", out var grey))
                {
                    if (!TrySampleMask(image.BitDepth, out var mask))
                    {
                        break;
                    }
                    var sample = grey & mask;
                    fields.Derived(
                        "<BackgroundSample>",
                        string.Create(CultureInfo.InvariantCulture, $"{sample} ({image.BitDepth}-bit sample)"),
                        data.LastRange);
                    var scaled = ScaleTo8(sample, mask);
                    fields.Derived(
                        "<BackgroundColor>",
                        FormatColor(scaled, scaled, scaled) + (image.BitDepth > 8 ? " (8-bit view)" : string.Empty),
                        data.LastRange);
                }

                break;
            case 2:
            case 6:
                var sampleStart = data.Position;
                if (TryUInt16(fields, data, "Red", out var redSample)
                    && TryUInt16(fields, data, "Green", out var greenSample)
                    && TryUInt16(fields, data, "Blue", out var blueSample))
                {
                    if (!TrySampleMask(image.BitDepth, out var mask))
                    {
                        break;
                    }
                    var red = redSample & mask;
                    var green = greenSample & mask;
                    var blue = blueSample & mask;
                    fields.Derived(
                        "<BackgroundSample>",
                        string.Create(CultureInfo.InvariantCulture, $"R={red}, G={green}, B={blue} ({image.BitDepth}-bit samples)"),
                        new SourceRange(sampleStart, 6));
                    fields.Derived(
                        "<BackgroundColor>",
                        FormatColor(ScaleTo8(red, mask), ScaleTo8(green, mask), ScaleTo8(blue, mask))
                            + (image.BitDepth > 8 ? " (8-bit view)" : string.Empty),
                        new SourceRange(sampleStart, 6));
                }

                break;
            default:
                fields.Derived("<UnparsedLength>", data.Remaining, data.CurrentRange);
                fields.Note("The IHDR color type does not select a bKGD layout; the bytes remain available in Hex.", data.CurrentRange);
                data.SkipRemaining();
                return;
        }

        if (data.Remaining > 0)
        {
            fields.Warning(DiagnosticCodes.SchemaMismatch, $"bKGD has {data.Remaining} unexpected trailing byte(s).", data.CurrentRange);
            data.SkipRemaining();
        }
    }

    private static void ParseHistogram(ParseContext context, FieldList fields, PngChunkDataReader data)
    {
        var count = data.Remaining / 2;
        if (data.Remaining % 2 != 0)
        {
            fields.Warning(DiagnosticCodes.InvalidLength, "hIST length is not divisible by 2.", data.CurrentRange);
        }

        var limit = Math.Min(count, context.Limits.MaxDataLines);
        for (var index = 0L; index < limit; index++)
        {
            if (!TryUInt16(fields, data, string.Create(CultureInfo.InvariantCulture, $"Frequency[{index}]"), out _))
            {
                return;
            }
        }

        if (count > limit)
        {
            fields.Warning(
                DiagnosticCodes.LimitRecords,
                $"hIST has {count} entries; only the first {limit} are shown.",
                data.CurrentRange);
        }

        data.SkipRemaining();
    }

    private static void ParsePhysicalDimensions(FieldList fields, PngChunkDataReader data)
    {
        if (!TryUInt32(fields, data, "Pixels per unit, X axis", out _)
            || !TryUInt32(fields, data, "Pixels per unit, Y axis", out _)
            || !TryByteMeaning(fields, data, "Unit specifier", out var unit, PngFormatting.FormatPhysicalUnit))
        {
            return;
        }

        if (unit > 1)
        {
            fields.Warning(DiagnosticCodes.InvalidEncoding, $"pHYs unit specifier {unit} is not defined.", data.LastRange);
        }

        RequireEmpty(fields, data, "pHYs");
    }

    private static void ParseSuggestedPalette(ParseContext context, FieldList fields, PngChunkDataReader data)
    {
        if (!TryReadNullString(
                fields,
                data,
                "Palette name",
                MaxKeywordBytes,
                Encoding.Latin1,
                validateUtf8: false,
                allowEmpty: false,
                out _))
        {
            return;
        }

        if (!TryByte(fields, data, "Sample depth", out var depth))
        {
            return;
        }

        if (depth is not (8 or 16))
        {
            fields.Warning(DiagnosticCodes.InvalidEncoding, $"sPLT sample depth {depth} must be 8 or 16.", data.LastRange);
            data.SkipRemaining();
            return;
        }

        var entrySize = depth == 8 ? 6 : 10;
        var entries = data.Remaining / entrySize;
        var limit = Math.Min(entries, context.Limits.MaxDataLines);
        for (var index = 0L; index < limit; index++)
        {
            var readChannels = depth == 16
                ? TryUInt16(fields, data, EntryKey(index, "red"), out _)
                    && TryUInt16(fields, data, EntryKey(index, "green"), out _)
                    && TryUInt16(fields, data, EntryKey(index, "blue"), out _)
                    && TryUInt16(fields, data, EntryKey(index, "alpha"), out _)
                : TryByte(fields, data, EntryKey(index, "red"), out _)
                    && TryByte(fields, data, EntryKey(index, "green"), out _)
                    && TryByte(fields, data, EntryKey(index, "blue"), out _)
                    && TryByte(fields, data, EntryKey(index, "alpha"), out _);
            if (!readChannels || !TryUInt16(fields, data, EntryKey(index, "frequency"), out _))
            {
                return;
            }
        }

        if (data.Remaining > 0)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                $"sPLT has {data.Remaining} unexpected trailing byte(s).",
                data.CurrentRange);
            data.SkipRemaining();
        }
    }

    private static void ParseExif(ParseContext context, FieldList fields, PngChunkDataReader data)
    {
        if (data.Remaining <= 0)
        {
            fields.Note("The Exif profile is empty.", data.CurrentRange);
            return;
        }

        ExifParser.Parse(
            context.Source,
            data.Position,
            data.Remaining,
            context.Limits,
            fields,
            string.Empty,
            context.CancellationToken);
        data.SkipRemaining();
    }

    private static void ParseTime(FieldList fields, PngChunkDataReader data)
    {
        if (!TryUInt16(fields, data, "Year", out var year)
            || !TryByte(fields, data, "Month", out var month)
            || !TryByte(fields, data, "Day", out var day)
            || !TryByte(fields, data, "Hour", out var hour)
            || !TryByte(fields, data, "Minute", out var minute)
            || !TryByte(fields, data, "Second", out var second))
        {
            return;
        }

        var timestamp = PngFormatting.FormatTimestamp(year, month, day, hour, minute, second);
        fields.Derived("<Timestamp>", timestamp);
        if (timestamp == "invalid")
        {
            fields.Warning(DiagnosticCodes.SchemaMismatch, "tIME contains an invalid date or time.", data.LastRange);
        }

        RequireEmpty(fields, data, "tIME");
    }

    private static void ParseText(ParseContext context, FieldList fields, PngChunkDataReader data)
    {
        if (!TryReadNullString(
                fields,
                data,
                "Keyword",
                MaxKeywordBytes,
                Encoding.Latin1,
                validateUtf8: false,
                allowEmpty: false,
                out _))
        {
            return;
        }

        var bytes = data.ReadRemainingPrefix(context.Limits.MaxStringBytes, out var length, out var range);
        fields.Raw("Text", PngFormatting.FormatText(bytes, Encoding.Latin1), range);
        fields.Derived("<TextByteLength>", length, range);
        if (length > bytes.Length)
        {
            fields.Warning(
                DiagnosticCodes.LimitString,
                $"Text is {length} byte(s); only the first {bytes.Length} are shown.",
                range);
        }

        if (bytes.AsSpan().IndexOfAnyExceptInRange((byte)0, (byte)0x7F) >= 0 && PngFormatting.IsValidUtf8(bytes))
        {
            fields.Derived("<TextUTF8>", PngFormatting.FormatText(bytes, Encoding.UTF8), range);
            fields.Note("The text bytes are valid UTF-8 although tEXt declares ISO-8859-1; both forms are shown.", range);
        }
    }

    private static void ParseCompressedText(ParseContext context, FieldList fields, PngChunkDataReader data)
    {
        if (!TryReadNullString(
                fields,
                data,
                "Keyword",
                MaxKeywordBytes,
                Encoding.Latin1,
                validateUtf8: false,
                allowEmpty: false,
                out _))
        {
            return;
        }

        if (!TryByteMeaning(fields, data, "Compression method", out var method, PngFormatting.FormatCompressionMethod))
        {
            return;
        }

        if (method != 0)
        {
            fields.Warning(DiagnosticCodes.InvalidEncoding, $"zTXt compression method {method} is not defined.", data.LastRange);
        }

        var text = DecompressRemaining(
            context,
            fields,
            data,
            "<CompressedTextLength>",
            context.Limits.MaxStringBytes,
            DiagnosticCodes.LimitString,
            "The compressed text",
            out _,
            out var range);
        if (text is null)
        {
            return;
        }

        fields.Derived("<Text>", PngFormatting.FormatText(text, Encoding.Latin1), range);
        fields.Derived("<TextByteLength>", text.Length, range);
    }

    private static void ParseInternationalText(ParseContext context, FieldList fields, PngChunkDataReader data)
    {
        if (!TryReadNullString(
                fields,
                data,
                "Keyword",
                MaxKeywordBytes,
                Encoding.Latin1,
                validateUtf8: false,
                allowEmpty: false,
                out _))
        {
            return;
        }

        if (!TryByteMeaning(fields, data, "Compression flag", out var compressionFlag, FormatCompressionFlag))
        {
            return;
        }

        if (!TryByteMeaning(fields, data, "Compression method", out var method, PngFormatting.FormatCompressionMethod))
        {
            return;
        }

        if (method != 0)
        {
            fields.Warning(DiagnosticCodes.InvalidEncoding, $"iTXt compression method {method} is not defined.", data.LastRange);
        }

        if (!TryReadNullString(
                fields,
                data,
                "Language tag",
                context.Limits.MaxStringBytes,
                Encoding.Latin1,
                validateUtf8: false,
                allowEmpty: true,
                out _))
        {
            return;
        }

        if (!TryReadNullString(
                fields,
                data,
                "Translated keyword",
                context.Limits.MaxStringBytes,
                Encoding.UTF8,
                validateUtf8: true,
                allowEmpty: true,
                out _))
        {
            return;
        }

        if (compressionFlag == 1)
        {
            var text = DecompressRemaining(
                context,
                fields,
                data,
                "<CompressedTextLength>",
                context.Limits.MaxStringBytes,
                DiagnosticCodes.LimitString,
                "The compressed international text",
                out _,
                out var range);
            if (text is null)
            {
                return;
            }

            if (!PngFormatting.IsValidUtf8(text))
            {
                fields.Warning(
                    DiagnosticCodes.InvalidEncoding,
                    "The decompressed iTXt text is not valid UTF-8; invalid bytes are shown as replacement characters.",
                    range);
            }

            fields.Derived("<Text>", PngFormatting.FormatText(text, Encoding.UTF8), range);
            fields.Derived("<TextByteLength>", text.Length, range);
            return;
        }

        if (compressionFlag != 0)
        {
            fields.Note("The text payload is not decoded because the compression flag is not defined.", data.CurrentRange);
            data.SkipRemaining();
            return;
        }

        var bytes = data.ReadRemainingPrefix(context.Limits.MaxStringBytes, out var textLength, out var textRange);
        if (!PngFormatting.IsValidUtf8(bytes))
        {
            fields.Warning(
                DiagnosticCodes.InvalidEncoding,
                "The iTXt text is not valid UTF-8; invalid bytes are shown as replacement characters.",
                textRange);
        }

        fields.Raw("Text", PngFormatting.FormatText(bytes, Encoding.UTF8), textRange);
        fields.Derived("<TextByteLength>", textLength, textRange);
        if (textLength > bytes.Length)
        {
            fields.Warning(
                DiagnosticCodes.LimitString,
                $"Text is {textLength} byte(s); only the first {bytes.Length} are shown.",
                textRange);
        }
    }

    private static void ParseAnimationControl(FieldList fields, PngChunkDataReader data)
    {
        if (!TryUInt32(fields, data, "num_frames", out var frames)
            || !TryUInt32(fields, data, "num_plays", out var plays))
        {
            return;
        }

        fields.Derived("<FrameCount>", frames);
        if (plays == 0)
        {
            fields.Note("num_plays = 0 means the animation repeats indefinitely.", data.LastRange);
        }

        RequireEmpty(fields, data, "acTL");
    }

    private static void ParseFrameControl(FieldList fields, PngChunkDataReader data)
    {
        if (!TryUInt32(fields, data, "sequence_number", out _)
            || !TryUInt32(fields, data, "width", out _)
            || !TryUInt32(fields, data, "height", out _)
            || !TryUInt32(fields, data, "x_offset", out _)
            || !TryUInt32(fields, data, "y_offset", out _)
            || !TryUInt16(fields, data, "delay_num", out var delayNumerator)
            || !TryUInt16(fields, data, "delay_den", out var delayDenominator)
            || !TryByteMeaning(fields, data, "dispose_op", out var dispose, PngFormatting.FormatDisposeOperation)
            || !TryByteMeaning(fields, data, "blend_op", out var blend, PngFormatting.FormatBlendOperation))
        {
            return;
        }

        fields.Derived("<FrameDelaySeconds>", PngFormatting.FormatDelay(delayNumerator, delayDenominator));
        if (dispose > 2)
        {
            fields.Warning(DiagnosticCodes.InvalidEncoding, $"fcTL dispose_op {dispose} is not defined.", data.LastRange);
        }

        if (blend > 1)
        {
            fields.Warning(DiagnosticCodes.InvalidEncoding, $"fcTL blend_op {blend} is not defined.", data.LastRange);
        }

        RequireEmpty(fields, data, "fcTL");
    }

    private static void ParseFrameData(FieldList fields, PngChunkDataReader data)
    {
        if (!TryUInt32(fields, data, "sequence_number", out _))
        {
            return;
        }

        var payload = data.ReadRemainingPrefix(0, out var length, out var range);
        _ = payload;
        fields.Derived("<FrameDataLength>", length, range);
        fields.Note("Compressed frame data is not decoded.", range);
    }

    private static void ParseImageOffset(FieldList fields, PngChunkDataReader data)
    {
        if (!TryInt32(fields, data, "X position", out _)
            || !TryInt32(fields, data, "Y position", out _)
            || !TryByteMeaning(fields, data, "Unit specifier", out var unit, PngFormatting.FormatOffsetUnit))
        {
            return;
        }

        if (unit > 1)
        {
            fields.Warning(DiagnosticCodes.InvalidEncoding, $"oFFs unit specifier {unit} is not defined.", data.LastRange);
        }

        RequireEmpty(fields, data, "oFFs");
    }

    private static byte[]? DecompressRemaining(
        ParseContext context,
        FieldList fields,
        PngChunkDataReader data,
        string compressedLengthKey,
        int maxOutputBytes,
        string outputLimitCode,
        string label,
        out long compressedLength,
        out SourceRange range)
    {
        var inputLimit = (int)Math.Min(context.Limits.MaxBlobBytes, int.MaxValue);
        var compressed = data.ReadRemainingPrefix(inputLimit, out compressedLength, out range);
        fields.Derived(compressedLengthKey, compressedLength, range);
        if (compressedLength > compressed.Length)
        {
            fields.Warning(
                DiagnosticCodes.LimitBlob,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label} is {compressedLength} byte(s); only the first {compressed.Length} are read."),
                range);
            return null;
        }

        if (!PngZlib.TryDecompress(
                compressed,
                maxOutputBytes,
                context.CancellationToken,
                out var output,
                out var exceeded))
        {
            fields.Warning(DiagnosticCodes.InvalidEncoding, $"{label} is not valid zlib data.", range);
            return null;
        }

        if (exceeded)
        {
            fields.Warning(
                outputLimitCode,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label} exceeds the {maxOutputBytes}-byte output limit; only a prefix is shown."),
                range);
        }

        return output;
    }

    private static void AppendCrc(
        ParseContext context,
        FieldList fields,
        PngChunkHeader header,
        FormatNode node,
        long dataLength)
    {
        if (!header.IsComplete || !header.IsLengthValid)
        {
            return;
        }

        Span<byte> storedBytes = stackalloc byte[4];
        if (SourceRead.ReadAtMost(context.Source, header.CrcOffset, storedBytes) < storedBytes.Length)
        {
            return;
        }

        var stored = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(storedBytes);
        var crcLength = 4 + dataLength;
        Action<long, long>? progress = crcLength > ParseThresholds.LargeOperationBytes
            ? (processed, total) => context.ReportProgress("PNG CRC", processed, total)
            : null;
        var computed = PngCrc32.Compute(
            context.Source,
            header.Offset + 4,
            crcLength,
            context.CancellationToken,
            progress);

        var value = stored == computed
            ? string.Create(CultureInfo.InvariantCulture, $"0x{stored:X8}")
            : string.Create(CultureInfo.InvariantCulture, $"0x{stored:X8} (0x{computed:X8})");
        fields.Raw("CRC", value, new SourceRange(header.CrcOffset, 4));

        if (stored != computed)
        {
            fields.Warning(
                DiagnosticCodes.CrcMismatch,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Stored CRC 0x{stored:X8} does not match the computed CRC 0x{computed:X8}."),
                new SourceRange(header.CrcOffset, 4));
        }
    }

    private static void RequireEmpty(FieldList fields, PngChunkDataReader data, string type)
    {
        if (data.Remaining > 0)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                string.Create(CultureInfo.InvariantCulture, $"{type} has {data.Remaining} unexpected trailing byte(s)."),
                data.CurrentRange);
            data.SkipRemaining();
        }
    }

    private static void RequireExactLength(
        FieldList fields,
        PngChunkDataReader data,
        string type,
        long expected,
        ParseLimits limits)
    {
        _ = limits;
        if (data.Remaining > 0)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{type} declares more than the expected {expected} data byte(s)."),
                data.CurrentRange);
            data.SkipRemaining();
        }
    }

    private static bool TryReadNullString(
        FieldList fields,
        PngChunkDataReader data,
        string key,
        int maxBytes,
        Encoding encoding,
        bool validateUtf8,
        bool allowEmpty,
        out string value)
    {
        value = string.Empty;
        var start = data.Position;
        if (!data.TryReadNullTerminated(maxBytes, out var bytes, out var range, out var exceeded))
        {
            if (bytes.Length > 0)
            {
                fields.Raw(key, PngFormatting.FormatText(bytes, encoding), range);
            }

            fields.Warning(
                exceeded ? DiagnosticCodes.LimitString : DiagnosticCodes.InvalidLength,
                exceeded
                    ? $"'{key}' exceeds {maxBytes} bytes before its null separator."
                    : $"'{key}' is not terminated by a null separator.",
                exceeded ? range : SourceRange.FromBounds(start, data.Position));
            return false;
        }

        if (!allowEmpty && bytes.Length == 0)
        {
            fields.Warning(DiagnosticCodes.InvalidLength, $"'{key}' must not be empty.", range);
            return false;
        }

        if (validateUtf8 && !PngFormatting.IsValidUtf8(bytes))
        {
            fields.Warning(
                DiagnosticCodes.InvalidEncoding,
                $"'{key}' is not valid UTF-8; raw bytes are shown as hex.",
                range);
        }

        value = PngFormatting.FormatText(bytes, encoding) + "\\0";
        fields.Raw(key, value, range);
        return true;
    }

    private static bool TryByte(FieldList fields, PngChunkDataReader data, string key, out byte value)
    {
        if (data.TryByte(out value))
        {
            fields.Raw(key, value, data.LastRange);
            return true;
        }

        return Truncated(fields, data, key);
    }

    private static bool TryByteMeaning(
        FieldList fields,
        PngChunkDataReader data,
        string key,
        out int value,
        Func<int, string> format)
    {
        value = 0;
        if (!data.TryByte(out var raw))
        {
            return Truncated(fields, data, key);
        }

        value = raw;
        fields.Raw(key, format(raw), data.LastRange);
        return true;
    }

    private static bool TryUInt16(FieldList fields, PngChunkDataReader data, string key, out ushort value)
    {
        if (data.TryUInt16BE(out value))
        {
            fields.Raw(key, value, data.LastRange);
            return true;
        }

        return Truncated(fields, data, key);
    }

    private static bool TryUInt32(FieldList fields, PngChunkDataReader data, string key, out uint value)
    {
        if (data.TryUInt32BE(out value))
        {
            fields.Raw(key, value, data.LastRange);
            return true;
        }

        return Truncated(fields, data, key);
    }

    private static bool TryInt32(FieldList fields, PngChunkDataReader data, string key, out int value)
    {
        if (data.TryInt32BE(out value))
        {
            fields.Raw(key, value, data.LastRange);
            return true;
        }

        return Truncated(fields, data, key);
    }

    private static bool TryFixed(FieldList fields, PngChunkDataReader data, string key, out uint value)
    {
        if (data.TryUInt32BE(out value))
        {
            fields.Raw(key, PngFormatting.FormatFixed100000(value), data.LastRange);
            return true;
        }

        return Truncated(fields, data, key);
    }

    private static bool Truncated(FieldList fields, PngChunkDataReader data, string key)
    {
        fields.Warning(
            DiagnosticCodes.TruncatedPayload,
            $"Field '{key}' is truncated.",
            data.CurrentRange);
        return false;
    }

    private static bool TryReadMarker(PngChunkDataReader data, Span<byte> destination)
    {
        for (var index = 0; index < destination.Length; index++)
        {
            if (!data.TryByte(out var value))
            {
                return false;
            }

            destination[index] = value;
        }

        return true;
    }

    private static string EntryKey(long index, string channel) =>
        string.Create(CultureInfo.InvariantCulture, $"Entry[{index}].{channel}");

    private static string FormatCompressionFlag(int value) => value switch
    {
        0 => "0 (Uncompressed)",
        1 => "1 (Compressed)",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    private static bool TryGetChunkOffset(string? stableId, out long offset)
    {
        offset = 0;
        if (stableId is null || !stableId.StartsWith("png:chunk:", StringComparison.Ordinal))
        {
            return false;
        }

        return long.TryParse(
            stableId.AsSpan("png:chunk:".Length),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out offset);
    }

    private static NodeParseResult Placeholder(FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<Length>", node.Range.Length, node.Range);
        fields.Note("This synthetic range is not a parsed PNG chunk.", node.Range);
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
