using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// One Photoshop image resource block header: '8BIM' + u16 ID + padded Pascal name +
/// u32 data size. The same block format is used by PSD/PSB image resources, JPEG APP13
/// "Photoshop 3.0" and TIFF tag 34377.
/// </summary>
internal readonly record struct PhotoshopResourceBlock(
    long Start,
    long End,
    ushort Id,
    long DataOffset,
    long DataLength,
    string Name,
    SourceRange SignatureRange,
    SourceRange IdRange,
    SourceRange NameRange,
    SourceRange SizeRange,
    bool Complete,
    string? Error);

/// <summary>
/// Adobe Photoshop image resources. Only documented, small metadata layouts are interpreted;
/// private resources, path points and image bytes keep their ranges without being decoded.
/// </summary>
internal sealed class PhotoshopImageResources
{
    public const int SignatureLength = 4;
    public const int FixedHeaderLength = 6;

    private readonly ParseContext _context;
    private readonly FieldList _fields;

    private PhotoshopImageResources(ParseContext context, FieldList fields)
    {
        _context = context;
        _fields = fields;
    }

    /// <summary>
    /// Reads one block header inside <paramref name="limit"/> without crossing it. The returned
    /// <see cref="PhotoshopResourceBlock.End"/> covers the data padding, so a scanner can move on
    /// even when the declared size is wrong.
    /// </summary>
    public static bool TryReadBlock(
        IRandomAccessSource source,
        long position,
        long limit,
        out PhotoshopResourceBlock block,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(source);
        block = default;
        error = string.Empty;

        if (limit < position || limit > source.Length)
        {
            error = "The image resource limit is outside the source.";
            return false;
        }

        if (limit - position < FixedHeaderLength)
        {
            error = "The image resource block header is truncated.";
            return false;
        }

        Span<byte> signature = stackalloc byte[SignatureLength];
        if (SourceRead.ReadAtMost(source, position, signature) != SignatureLength)
        {
            error = "The image resource signature could not be read.";
            return false;
        }

        if (!signature.SequenceEqual("8BIM"u8))
        {
            error = $"Expected '8BIM' but found {RawIdentifier.FormatBytes(signature)}.";
            return false;
        }

        Span<byte> idBytes = stackalloc byte[2];
        _ = SourceRead.ReadAtMost(source, position + SignatureLength, idBytes);
        var id = BinaryPrimitives.ReadUInt16BigEndian(idBytes);

        var nameLengthOffset = position + FixedHeaderLength;
        if (nameLengthOffset >= limit)
        {
            error = "The image resource name length is truncated.";
            return false;
        }

        Span<byte> nameLengthByte = stackalloc byte[1];
        _ = SourceRead.ReadAtMost(source, nameLengthOffset, nameLengthByte);
        var nameLength = nameLengthByte[0];
        var nameOffset = nameLengthOffset + 1;
        var nameBytes = Math.Min((long)nameLength, limit - nameOffset);
        var name = nameBytes > 0 ? ReadLatin1(source, nameOffset, (int)nameBytes) : string.Empty;
        var padding = (1 + nameLength) & 1;
        var sizeOffset = nameOffset + nameLength + padding;
        if (limit - sizeOffset < 4)
        {
            error = "The image resource size field is truncated.";
            return false;
        }

        Span<byte> sizeBytes = stackalloc byte[4];
        _ = SourceRead.ReadAtMost(source, sizeOffset, sizeBytes);
        var size = BinaryPrimitives.ReadUInt32BigEndian(sizeBytes);
        var dataOffset = sizeOffset + 4;
        var available = limit - dataOffset;
        var complete = size <= available;
        var dataLength = complete ? size : available;
        var blockEnd = dataOffset + dataLength + (dataLength & 1);
        if (blockEnd > limit)
        {
            blockEnd = limit;
        }

        if (!complete)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Resource {id} declares {size} data byte(s), but only {available} are available.");
        }

        block = new PhotoshopResourceBlock(
            position,
            blockEnd,
            id,
            dataOffset,
            dataLength,
            name,
            new SourceRange(position, SignatureLength),
            new SourceRange(position + SignatureLength, 2),
            new SourceRange(nameOffset, nameLength),
            new SourceRange(sizeOffset, 4),
            complete,
            complete ? null : error);
        return true;
    }

    /// <summary>
    /// Parses a consecutive list of image resource blocks inside one JPEG APP13 segment and emits
    /// them as <c>Resource[n].</c> fields.
    /// </summary>
    public static void ParseList(ParseContext context, FieldList fields, BigEndianReader data) =>
        new PhotoshopImageResources(context, fields).ReadList(data);

    /// <summary>
    /// Parses one image resource block that is already an explicit node. The resource ID is part of
    /// the node title, so it is not repeated in the field list (D-013).
    /// </summary>
    public static void ParseBlock(ParseContext context, FieldList fields, PhotoshopResourceBlock block) =>
        new PhotoshopImageResources(context, fields).ReadBlock(block);

    public static string ResourceName(ushort id) => id switch
    {
        1000 => "Obsolete image properties",
        1001 => "Macintosh print manager print info record",
        1002 => "Macintosh page format information",
        1003 => "Obsolete indexed color table",
        1005 => "ResolutionInfo",
        1006 => "Alpha channel names",
        1007 => "DisplayInfo",
        1008 => "Caption",
        1009 => "Border information",
        1010 => "Background color",
        1011 => "Print flags",
        1012 => "Grayscale and multichannel halftoning information",
        1013 => "Color halftoning information",
        1014 => "Duotone halftoning information",
        1015 => "Grayscale and multichannel transfer function",
        1016 => "Color transfer functions",
        1017 => "Duotone transfer functions",
        1018 => "Duotone image information",
        1019 => "Dot range",
        1021 => "EPS options",
        1022 => "Quick Mask information",
        1024 => "Layer state information",
        1025 => "Working path",
        1026 => "Layers group information",
        1028 => "IPTC-NAA record",
        1029 => "Image mode for raw format files",
        1030 => "JPEG quality",
        1032 => "Grid and guides",
        1033 or 1036 => "Thumbnail",
        1034 => "Copyright flag",
        1035 => "URL",
        1037 => "Global Angle",
        1038 => "Color samplers",
        1039 => "ICC Profile",
        1040 => "Watermark",
        1041 => "ICC Untagged Profile",
        1042 => "Effects visible",
        1043 => "Spot Halftone",
        1044 => "Document-specific IDs seed number",
        1045 => "Unicode Alpha Names",
        1046 => "Indexed Color Table Count",
        1047 => "Transparency Index",
        1049 => "Global Altitude",
        1050 => "Slices",
        1051 => "Workflow URL",
        1052 => "Jump To XPEP",
        1053 => "Alpha Identifiers",
        1054 => "URL List",
        1057 => "Version Info",
        1058 => "EXIF data 1",
        1059 => "EXIF data 3",
        1060 => "XMP metadata",
        1061 => "Caption digest",
        1062 => "Print scale",
        1064 => "Pixel Aspect Ratio",
        1065 => "Layer Comps",
        1066 => "Alternate Duotone Colors",
        1067 => "Alternate Spot Colors",
        1069 => "Layer Selection ID(s)",
        1070 => "HDR Toning information",
        1071 => "Print info",
        1072 => "Layer Group(s) Enabled ID",
        1073 => "Color samplers",
        1074 => "Measurement Scale",
        1075 => "Timeline Information",
        1076 => "Sheet Disclosure",
        1077 => "DisplayInfo",
        1078 => "Onion Skins",
        1080 => "Count Information",
        1082 => "Print Information",
        1083 => "Print Style",
        1084 => "Macintosh NSPrintInfo",
        1085 => "Windows DEVMODE",
        1086 => "Auto Save File Path",
        1087 => "Auto Save Format",
        1088 => "Path Selection State",
        >= 2000 and <= 2997 => "Path Information",
        2999 => "Name of clipping path",
        3000 => "Origin Path Info",
        >= 4000 and <= 4999 => "Plug-In resource",
        >= 7000 and <= 7006 => "Image Ready resource",
        8000 => "Lightroom workflow",
        10000 => "Print flags information",
        _ => "Unknown resource",
    };

    public static string FormatResourceId(ushort id) =>
        string.Create(CultureInfo.InvariantCulture, $"{id} ({ResourceName(id)})");

    private void ReadList(BigEndianReader data)
    {
        var index = 0;
        while (data.Remaining > 0)
        {
            _context.ThrowIfCancellationRequested();
            if (index >= _context.Limits.MaxDataLines || _fields.IsFull)
            {
                _fields.Warning(
                    DiagnosticCodes.LimitRecords,
                    "The Photoshop resource listing reached the field/record limit.",
                    data.CurrentRange);
                _fields.Derived("<UnparsedLength>", data.Remaining, data.CurrentRange);
                data.SkipRemaining();
                return;
            }

            var start = data.Position;
            var limit = data.Position + data.Remaining;
            var prefix = string.Create(CultureInfo.InvariantCulture, $"Resource[{index}].");
            if (!TryReadBlock(_context.Source, start, limit, out var block, out var error))
            {
                _fields.Warning(DiagnosticCodes.InvalidSignature, error, new SourceRange(start, limit - start));
                _fields.Derived("<UnparsedLength>", limit - start, new SourceRange(start, limit - start));
                data.SkipRemaining();
                return;
            }

            WriteHeader(block, prefix, includeId: true);
            ReadData(block.Id, prefix, new BigEndianReader(_context.Source, block.DataOffset, block.DataLength));

            data.Skip(block.End - start);
            if (!block.Complete)
            {
                _fields.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    block.Error ?? "The image resource data is truncated.",
                    new SourceRange(block.Start, block.End - block.Start));
                return;
            }

            index++;
        }
    }

    private void ReadBlock(PhotoshopResourceBlock block)
    {
        WriteHeader(block, string.Empty, includeId: false);
        ReadData(block.Id, string.Empty, new BigEndianReader(_context.Source, block.DataOffset, block.DataLength));
        if (!block.Complete && block.Error is not null)
        {
            _fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                block.Error,
                new SourceRange(block.Start, block.End - block.Start));
        }
    }

    private void WriteHeader(PhotoshopResourceBlock block, string prefix, bool includeId)
    {
        _fields.Raw(
            prefix + "Signature",
            RawIdentifier.FormatBytes("8BIM"u8),
            block.SignatureRange);
        if (includeId)
        {
            _fields.Raw(prefix + "ID", FormatResourceId(block.Id), block.IdRange);
        }

        _fields.Raw(
            prefix + "Name",
            block.Name.Length == 0 ? string.Empty : TextEscaping.Escape(block.Name),
            block.NameRange);
        _fields.Raw(prefix + "Size", block.DataLength, block.SizeRange);
    }

    private void ReadData(ushort id, string prefix, BigEndianReader data)
    {
        switch (id)
        {
            case 1000:
                _ = UInt16(data, prefix + "Number of channels", out _)
                    && UInt16(data, prefix + "Rows", out _)
                    && UInt16(data, prefix + "Columns", out _)
                    && UInt16(data, prefix + "Depth", out _)
                    && UInt16(data, prefix + "Mode", out _, FormatColorMode);
                break;
            case 1005:
                _ = Fixed1616(data, prefix + "hRes")
                    && UInt16(data, prefix + "hResUnit", out _, ResolutionUnit)
                    && UInt16(data, prefix + "widthUnit", out _, DimensionUnit)
                    && Fixed1616(data, prefix + "vRes")
                    && UInt16(data, prefix + "vResUnit", out _, ResolutionUnit)
                    && UInt16(data, prefix + "heightUnit", out _, DimensionUnit);
                break;
            case 1006:
                ReadPascalNames(data, prefix, "Alpha name");
                break;
            case 1008:
                _ = PascalString(data, prefix + "Caption");
                break;
            case 1009:
                _ = Fixed1616(data, prefix + "Border width")
                    && UInt16(data, prefix + "Border units", out _, DimensionUnit);
                break;
            case 1010:
                ReadColor(data, prefix);
                break;
            case 1011:
                ReadPrintFlags(data, prefix);
                break;
            case 1019:
                _ = UInt16(data, prefix + "Effective black value", out _)
                    && UInt16(data, prefix + "Effective white value", out _);
                break;
            case 1022:
                _ = UInt16(data, prefix + "Quick Mask channel ID", out _)
                    && Boolean(data, prefix + "Mask initially empty");
                break;
            case 1024:
                _ = UInt16(data, prefix + "Target layer index", out _);
                break;
            case 1026:
                ReadLayerGroupIds(data, prefix);
                break;
            case 1032:
                ReadGuides(data, prefix);
                break;
            case 1033:
            case 1036:
                ReadThumbnail(data, prefix);
                break;
            case 1034:
                _ = Boolean(data, prefix + "Copyright flag");
                break;
            case 1035:
                ReadText(data, prefix + "URL", Encoding.Latin1);
                break;
            case 1037:
                _ = Int32(data, prefix + "Global Angle");
                break;
            case 1039:
                ReadIccProfile(data);
                break;
            case 1040:
                _ = Byte(data, prefix + "Watermark", out _);
                break;
            case 1041:
                _ = Boolean(data, prefix + "ICC Untagged Profile");
                break;
            case 1042:
                _ = Boolean(data, prefix + "Effects visible");
                break;
            case 1043:
                _ = UInt32(data, prefix + "Version", out _)
                    && UInt32(data, prefix + "Length", out _);
                break;
            case 1044:
                _ = UInt32(data, prefix + "Document-specific IDs seed number", out _);
                break;
            case 1045:
                ReadUnicodeNames(data, prefix);
                break;
            case 1046:
                _ = UInt16(data, prefix + "Indexed Color Table Count", out _);
                break;
            case 1047:
                _ = UInt16(data, prefix + "Transparency Index", out _);
                break;
            case 1049:
                _ = Int32(data, prefix + "Global Altitude");
                break;
            case 1050:
                ReadSlices(data, prefix);
                break;
            case 1051:
                _ = UnicodeString(data, prefix + "Workflow URL");
                break;
            case 1052:
                ReadJumpToXpep(data, prefix);
                break;
            case 1053:
                ReadAlphaIdentifiers(data, prefix);
                break;
            case 1054:
                ReadUrlList(data, prefix);
                break;
            case 1057:
                _ = UInt32(data, prefix + "Version", out _)
                    && Boolean(data, prefix + "hasRealMergedData")
                    && UnicodeString(data, prefix + "Writer name")
                    && UnicodeString(data, prefix + "Reader name")
                    && UInt32(data, prefix + "File version", out _);
                break;
            case 1058:
            case 1059:
                ReadExif(data, prefix);
                break;
            case 1060:
                XmpText.Parse(_context, _fields, data, prefix);
                break;
            case 1061:
                ReadDigest(data, prefix);
                break;
            case 1062:
                _ = UInt16(data, prefix + "Style", out _, PrintScaleStyle)
                    && Single(data, prefix + "X location")
                    && Single(data, prefix + "Y location")
                    && Single(data, prefix + "Scale");
                break;
            case 1064:
                _ = UInt32(data, prefix + "Version", out _) && Double(data, prefix + "Pixel Aspect Ratio");
                break;
            case 1065:
            case 1074:
            case 1075:
            case 1076:
            case 1078:
            case 1080:
            case 1082:
            case 1083:
            case 1088:
            case 3000:
                ReadDescriptor(data, prefix);
                break;
            case 1069:
                ReadLayerSelectionIds(data, prefix);
                break;
            case 1072:
                ReadLayerGroupsEnabled(data, prefix);
                break;
            case 1073:
                ReadColorSamplers(data, prefix);
                break;
            case 1086:
                _ = UnicodeString(data, prefix + "Auto Save File Path");
                break;
            case 1087:
                _ = UnicodeString(data, prefix + "Auto Save Format");
                break;
            case >= 2000 and <= 2997:
                _fields.Derived($"<{prefix}PathRecordCount>", data.Remaining / 26, data.CurrentRange);
                _fields.Note("Path point records are not interpreted.", data.CurrentRange);
                break;
            case 2999:
                _ = PascalString(data, prefix + "Name of clipping path");
                break;
            case 10000:
                _ = UInt16(data, prefix + "Version", out _)
                    && Byte(data, prefix + "Center crop marks", out _)
                    && Byte(data, prefix + "Reserved", out _)
                    && UInt32(data, prefix + "Bleed width value", out _)
                    && UInt16(data, prefix + "Bleed width scale", out _);
                break;
            default:
                _fields.Note(
                    $"{prefix.TrimEnd('.')} {ResourceName(id)} payload is not interpreted.",
                    data.CurrentRange);
                break;
        }

        if (data.Remaining > 0)
        {
            _fields.Derived($"<{prefix}UnparsedLength>", data.Remaining, data.CurrentRange);
            data.SkipRemaining();
        }
    }

    private void ReadPrintFlags(BigEndianReader data, string prefix)
    {
        string[] names =
        [
            "Labels",
            "Crop marks",
            "Color bars",
            "Registration marks",
            "Negative",
            "Flip",
            "Interpolate",
            "Caption",
            "Print flags",
        ];
        foreach (var name in names)
        {
            if (!Boolean(data, prefix + name))
            {
                return;
            }
        }
    }

    private void ReadLayerGroupIds(BigEndianReader data, string prefix)
    {
        var index = 0;
        while (data.Remaining >= 2)
        {
            _context.ThrowIfCancellationRequested();
            if (index >= _context.Limits.MaxDataLines || _fields.IsFull)
            {
                _fields.Warning(
                    DiagnosticCodes.LimitRecords,
                    "The layer group listing reached the record limit.",
                    data.CurrentRange);
                return;
            }

            if (!UInt16(data, prefix + $"Layer group ID[{index}]", out _))
            {
                return;
            }

            index++;
        }
    }

    private void ReadGuides(BigEndianReader data, string prefix)
    {
        if (!UInt32(data, prefix + "Version", out _)
            || !UInt32(data, prefix + "Horizontal grid", out _)
            || !UInt32(data, prefix + "Vertical grid", out _)
            || !UInt32(data, prefix + "fGuideCount", out var count))
        {
            return;
        }

        for (var index = 0L; index < count && !_fields.IsFull && index < _context.Limits.MaxDataLines; index++)
        {
            _context.ThrowIfCancellationRequested();
            if (!UInt32(data, prefix + $"Guide[{index}].Location", out _)
                || !Byte(data, prefix + $"Guide[{index}].Direction", out _))
            {
                return;
            }
        }

        if (data.Remaining > 0)
        {
            _fields.Note("Remaining guide records are available in Hex.", data.CurrentRange);
        }
    }

    private void ReadThumbnail(BigEndianReader data, string prefix)
    {
        if (!UInt32(data, prefix + "Format", out _)
            || !UInt32(data, prefix + "Width", out _)
            || !UInt32(data, prefix + "Height", out _)
            || !UInt32(data, prefix + "Widthbytes", out _)
            || !UInt32(data, prefix + "Total size", out _)
            || !UInt32(data, prefix + "Size after compression", out _)
            || !UInt16(data, prefix + "Bits per pixel", out _)
            || !UInt16(data, prefix + "Number of planes", out _))
        {
            return;
        }

        _fields.Derived($"<{prefix}ThumbnailByteLength>", data.Remaining, data.CurrentRange);
        _fields.Note("Thumbnail image bytes are not decoded.", data.CurrentRange);
        data.SkipRemaining();
    }

    private void ReadDigest(BigEndianReader data, string prefix)
    {
        if (!Require(data, 16, prefix + "Caption digest"))
        {
            return;
        }

        Span<byte> bytes = stackalloc byte[16];
        _ = data.TryReadBytes(bytes);
        _fields.Raw(prefix + "Caption digest", HexFormatting.Format(bytes, bytes.Length), data.LastRange);
    }

    private void ReadPascalNames(BigEndianReader data, string prefix, string key)
    {
        var index = 0;
        while (data.Remaining > 0 && index < _context.Limits.MaxDataLines && !_fields.IsFull)
        {
            _context.ThrowIfCancellationRequested();
            if (!PascalString(data, $"{prefix}{key}[{index}]"))
            {
                return;
            }

            index++;
        }
    }

    private void ReadUnicodeNames(BigEndianReader data, string prefix)
    {
        var index = 0;
        while (data.Remaining > 0 && index < _context.Limits.MaxDataLines && !_fields.IsFull)
        {
            _context.ThrowIfCancellationRequested();
            if (!UnicodeString(data, prefix + $"Alpha name[{index}]"))
            {
                return;
            }

            index++;
        }
    }

    private void ReadColor(BigEndianReader data, string prefix)
    {
        if (!UInt16(data, prefix + "Color space", out _, FormatColorSpace))
        {
            return;
        }

        for (var index = 0; index < 4; index++)
        {
            if (!UInt16(data, prefix + $"Color component[{index}]", out _))
            {
                return;
            }
        }
    }

    private void ReadIccProfile(BigEndianReader data)
    {
        var limit = (int)Math.Min(_context.Limits.MaxBlobBytes, int.MaxValue);
        var profile = data.ReadRemainingPrefix(limit, out var totalLength, out var range);
        if (totalLength > profile.Length)
        {
            _fields.Warning(
                DiagnosticCodes.LimitBlob,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The ICC profile is {totalLength} byte(s); only the first {profile.Length} are read."),
                range);
        }

        IccProfileParser.Parse(
            profile,
            _fields,
            _context.Limits,
            _context.CancellationToken,
            profileOffset: range.Offset,
            fallbackRange: range);
    }

    private void ReadSlices(BigEndianReader data, string prefix)
    {
        if (!UInt32(data, prefix + "Version", out var version))
        {
            return;
        }

        if (version is 7 or 8)
        {
            _ = UInt32(data, prefix + "Descriptor version", out _);
            _fields.Derived($"<{prefix}DescriptorLength>", data.Remaining, data.CurrentRange);
            _fields.Note("The slices descriptor is not interpreted.", data.CurrentRange);
            data.SkipRemaining();
            return;
        }

        if (version != 6)
        {
            _fields.Note($"Slices version {version} is not interpreted.", data.CurrentRange);
            return;
        }

        ReadRectangle(data, prefix + "Bounding rectangle");
        _ = UnicodeString(data, prefix + "Group name");
        _ = UInt32(data, prefix + "Number of slices", out _);
        _fields.Derived($"<{prefix}SliceRecordsLength>", data.Remaining, data.CurrentRange);
        _fields.Note("Slice records are not interpreted.", data.CurrentRange);
        data.SkipRemaining();
    }

    private void ReadJumpToXpep(BigEndianReader data, string prefix)
    {
        if (!UInt16(data, prefix + "Major version", out _)
            || !UInt16(data, prefix + "Minor version", out _)
            || !UInt32(data, prefix + "Count", out var count))
        {
            return;
        }

        Span<byte> key = stackalloc byte[4];
        for (var index = 0L; index < count && index < _context.Limits.MaxDataLines && !_fields.IsFull; index++)
        {
            _context.ThrowIfCancellationRequested();
            if (!UInt32(data, prefix + $"Block[{index}].Size", out _))
            {
                return;
            }

            if (!Require(data, 4, prefix + $"Block[{index}].Key") || !data.TryReadBytes(key))
            {
                return;
            }

            _fields.Raw(
                prefix + $"Block[{index}].Key",
                RawIdentifier.FormatText(Encoding.Latin1.GetString(key)),
                data.LastRange);
            if (key.SequenceEqual("jtDd"u8))
            {
                _ = Boolean(data, prefix + $"Block[{index}].Dirty flag");
            }
            else
            {
                _ = UInt32(data, prefix + $"Block[{index}].Modification date", out _);
            }
        }
    }

    private void ReadAlphaIdentifiers(BigEndianReader data, string prefix)
    {
        if (!UInt32(data, prefix + "Length", out var length))
        {
            return;
        }

        var count = Math.Min(length / 4, data.Remaining / 4);
        for (var index = 0L; index < count && index < _context.Limits.MaxDataLines && !_fields.IsFull; index++)
        {
            _context.ThrowIfCancellationRequested();
            if (!UInt32(data, prefix + $"Alpha identifier[{index}]", out _))
            {
                return;
            }
        }
    }

    private void ReadUrlList(BigEndianReader data, string prefix)
    {
        if (!UInt32(data, prefix + "Count", out var count))
        {
            return;
        }

        for (var index = 0L; index < count && index < _context.Limits.MaxDataLines && !_fields.IsFull; index++)
        {
            _context.ThrowIfCancellationRequested();
            if (!UInt32(data, prefix + $"URL[{index}].Long", out _)
                || !UInt32(data, prefix + $"URL[{index}].ID", out _)
                || !UnicodeString(data, prefix + $"URL[{index}].Text"))
            {
                return;
            }
        }
    }

    private void ReadLayerSelectionIds(BigEndianReader data, string prefix)
    {
        if (!UInt16(data, prefix + "Count", out var count))
        {
            return;
        }

        for (var index = 0; index < count && index < _context.Limits.MaxDataLines && !_fields.IsFull; index++)
        {
            _context.ThrowIfCancellationRequested();
            if (!UInt32(data, prefix + $"Layer ID[{index}]", out _))
            {
                return;
            }
        }
    }

    private void ReadLayerGroupsEnabled(BigEndianReader data, string prefix)
    {
        var index = 0;
        while (data.Remaining > 0 && index < _context.Limits.MaxDataLines && !_fields.IsFull)
        {
            _context.ThrowIfCancellationRequested();
            if (!Boolean(data, prefix + $"Layer[{index}] enabled"))
            {
                return;
            }

            index++;
        }
    }

    private void ReadColorSamplers(BigEndianReader data, string prefix)
    {
        if (!UInt32(data, prefix + "Version", out _) || !UInt32(data, prefix + "Count", out _))
        {
            return;
        }

        _fields.Derived($"<{prefix}ColorSamplerRecordsLength>", data.Remaining, data.CurrentRange);
        _fields.Note("Color sampler records are not interpreted.", data.CurrentRange);
        data.SkipRemaining();
    }

    private void ReadDescriptor(BigEndianReader data, string prefix)
    {
        // An action descriptor is a nested structure. The product boundary stops at the raw
        // descriptor version; its opaque body stays visible as a prefix in Hex.
        _ = UInt32(data, prefix + "Descriptor version", out _);
        _fields.Derived($"<{prefix}DescriptorLength>", data.Remaining, data.CurrentRange);
        _fields.Note("The descriptor structure is not interpreted.", data.CurrentRange);
        data.SkipRemaining();
    }

    private void ReadExif(BigEndianReader data, string prefix)
    {
        Span<byte> header = stackalloc byte[4];
        var read = SourceRead.ReadAtMost(_context.Source, data.Position, header);
        var isTiff = read == 4
            && (header.SequenceEqual("II*\0"u8) || header.SequenceEqual("MM\0*"u8));
        if (!isTiff)
        {
            _fields.Note(
                "The EXIF resource does not start with a TIFF header; it is kept unparsed.",
                data.CurrentRange);
            return;
        }

        _fields.Derived($"<{prefix}ExifPayloadLength>", data.Remaining, data.CurrentRange);
        ExifParser.Parse(
            _context.Source,
            data.Position,
            data.Remaining,
            _context.Limits,
            _fields,
            prefix,
            _context.CancellationToken);
        data.SkipRemaining();
    }

    private void ReadRectangle(BigEndianReader data, string key)
    {
        _ = Int32(data, key + ".Top")
            && Int32(data, key + ".Left")
            && Int32(data, key + ".Bottom")
            && Int32(data, key + ".Right");
    }

    private bool PascalString(BigEndianReader data, string key, bool padded = false)
    {
        if (!Byte(data, key + " length", out var length))
        {
            return false;
        }

        if (!Require(data, length, key))
        {
            return false;
        }

        var bytes = data.ReadPrefix(length, _context.Limits.MaxStringBytes, out var range);
        _fields.Raw(key, TextDecoding.Display(bytes, TextEncodings.Latin1), range);
        if (bytes.Length < length)
        {
            _fields.Warning(DiagnosticCodes.LimitString, $"'{key}' is shown as a prefix.", range);
        }

        if (padded && ((length + 1) & 1) != 0)
        {
            if (!Require(data, 1, key + " padding"))
            {
                return false;
            }

            _ = data.TryByte(out _);
        }

        return true;
    }

    private bool UnicodeString(BigEndianReader data, string key)
    {
        if (!UInt32(data, key + " length", out var units))
        {
            return false;
        }

        var byteLength = units * 2L;
        if (!Require(data, byteLength, key))
        {
            return false;
        }

        var limit = _context.Limits.MaxStringBytes & ~1;
        var bytes = data.ReadPrefix(byteLength, limit, out var range);
        _fields.Raw(key, TextDecoding.Display(bytes, Encoding.BigEndianUnicode), range);
        if (bytes.Length < byteLength)
        {
            _fields.Warning(DiagnosticCodes.LimitString, $"'{key}' is shown as a prefix.", range);
        }

        // Adobe's general "Unicode string" definition mentions a two-byte null, but real PSD and
        // APP13 resources store only the counted UTF-16 units. Consuming a guessed null would eat
        // the next field (for example the file version in resource 1057).
        return true;
    }

    private void ReadText(BigEndianReader data, string key, Encoding encoding)
    {
        var bytes = data.ReadRemainingPrefix(_context.Limits.MaxStringBytes, out var length, out var range);
        _fields.Raw(key, TextDecoding.Display(bytes, encoding), range);
        if (bytes.Length < length)
        {
            _fields.Warning(DiagnosticCodes.LimitString, $"'{key}' is shown as a prefix.", range);
        }
    }

    private bool Require(BigEndianReader data, long length, string key)
    {
        if (data.Remaining >= length)
        {
            return true;
        }

        _fields.Warning(DiagnosticCodes.TruncatedPayload, $"Field '{key}' is truncated.", data.CurrentRange);
        return false;
    }

    private bool Byte(BigEndianReader data, string key, out byte value)
    {
        value = 0;
        if (!Require(data, 1, key) || !data.TryByte(out value))
        {
            return false;
        }

        _fields.Raw(key, value, data.LastRange);
        return true;
    }

    private bool Boolean(BigEndianReader data, string key)
    {
        if (!Require(data, 1, key) || !data.TryByte(out var value))
        {
            return false;
        }

        _fields.Raw(
            key,
            string.Create(CultureInfo.InvariantCulture, $"{value} ({(value == 0 ? "false" : "true")})"),
            data.LastRange);
        return true;
    }

    private bool UInt16(BigEndianReader data, string key, out ushort value, Func<ushort, string>? format = null)
    {
        value = 0;
        if (!Require(data, 2, key) || !data.TryUInt16BE(out value))
        {
            return false;
        }

        _fields.Raw(key, format?.Invoke(value) ?? value.ToString(CultureInfo.InvariantCulture), data.LastRange);
        return true;
    }

    private bool UInt32(BigEndianReader data, string key, out uint value)
    {
        value = 0;
        if (!Require(data, 4, key) || !data.TryUInt32BE(out value))
        {
            return false;
        }

        _fields.Raw(key, value, data.LastRange);
        return true;
    }

    private bool Int32(BigEndianReader data, string key)
    {
        if (!Require(data, 4, key) || !data.TryUInt32BE(out var value))
        {
            return false;
        }

        _fields.Raw(key, unchecked((int)value), data.LastRange);
        return true;
    }

    private bool Fixed1616(BigEndianReader data, string key)
    {
        if (!Require(data, 4, key) || !data.TryUInt32BE(out var value))
        {
            return false;
        }

        _fields.Raw(
            key,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{value} ({(value / 65536m).ToString("0.################", CultureInfo.InvariantCulture)})"),
            data.LastRange);
        return true;
    }

    private bool Single(BigEndianReader data, string key)
    {
        if (!Require(data, 4, key) || !data.TrySingleBE(out var value))
        {
            return false;
        }

        _fields.Raw(key, value.ToString("R", CultureInfo.InvariantCulture), data.LastRange);
        return true;
    }

    private bool Double(BigEndianReader data, string key)
    {
        if (!Require(data, 8, key) || !data.TryDoubleBE(out var value))
        {
            return false;
        }

        _fields.Raw(key, value.ToString("R", CultureInfo.InvariantCulture), data.LastRange);
        return true;
    }

    private static string ReadLatin1(IRandomAccessSource source, long offset, int count)
    {
        var bytes = new byte[count];
        var read = SourceRead.ReadAtMost(source, offset, bytes);
        return Encoding.Latin1.GetString(bytes, 0, read);
    }

    private static string FormatColorMode(ushort mode) => mode switch
    {
        0 => "0 (Bitmap)",
        1 => "1 (Grayscale)",
        2 => "2 (Indexed)",
        3 => "3 (RGB)",
        4 => "4 (CMYK)",
        7 => "7 (Multichannel)",
        8 => "8 (Duotone)",
        9 => "9 (Lab)",
        _ => mode.ToString(CultureInfo.InvariantCulture),
    };

    private static string FormatColorSpace(ushort space) => space switch
    {
        0 => "0 (RGB)",
        1 => "1 (HSB)",
        2 => "2 (CMYK)",
        3 => "3 (Pantone)",
        4 => "4 (Focoltone)",
        5 => "5 (Trumatch)",
        6 => "6 (Toyo)",
        7 => "7 (Lab)",
        8 => "8 (Gray)",
        9 => "9 (Wide CMYK)",
        10 => "10 (HKS)",
        11 => "11 (DIC)",
        12 => "12 (Total Ink)",
        13 => "13 (Monitor RGB)",
        14 => "14 (Duotone)",
        15 => "15 (Opacity)",
        16 => "16 (Web)",
        17 => "17 (Gray Float)",
        18 => "18 (RGB Float)",
        19 => "19 (Opacity Float)",
        _ => space.ToString(CultureInfo.InvariantCulture),
    };

    private static string PrintScaleStyle(ushort style) => style switch
    {
        0 => "0 (Centered)",
        1 => "1 (Size to fit)",
        2 => "2 (User defined)",
        _ => style.ToString(CultureInfo.InvariantCulture),
    };

    private static string ResolutionUnit(ushort unit) => unit switch
    {
        1 => "1 (pixels per inch)",
        2 => "2 (pixels per centimetre)",
        _ => unit.ToString(CultureInfo.InvariantCulture),
    };

    private static string DimensionUnit(ushort unit) => unit switch
    {
        1 => "1 (inches)",
        2 => "2 (centimetres)",
        3 => "3 (points)",
        4 => "4 (picas)",
        5 => "5 (columns)",
        _ => unit.ToString(CultureInfo.InvariantCulture),
    };
}
