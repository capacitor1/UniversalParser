using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// Raw TIFF/Exif IFD walker used by JPEG APP1 and PNG eXIf. It never throws on malformed input;
/// offsets are checked in 64-bit arithmetic, circular IFDs are rejected, and every value is read
/// only when its declared range lies inside the supplied block.
/// </summary>
internal static class ExifParser
{
    private const int MaxIfdDepth = 6;
    private const int MaxIfdCount = 32;
    private const int MaxEntriesPerIfd = 4096;
    private const int MaxIfdChain = 8;

    public static void Parse(
        IRandomAccessSource source,
        long offset,
        long length,
        ParseLimits limits,
        FieldList fields,
        string keyPrefix,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(limits);

        if (length <= 0)
        {
            fields.Note("The Exif payload is empty.", new SourceRange(offset, 0));
            return;
        }

        var available = Math.Min(length, Math.Max(0, source.Length - offset));
        var cap = Math.Min(available, Math.Min(limits.MaxStringBytes, int.MaxValue));
        var data = new byte[(int)cap];
        cancellationToken.ThrowIfCancellationRequested();
        var read = SourceRead.ReadAtMost(source, offset, data);
        if (read < data.Length)
        {
            Array.Resize(ref data, read);
        }

        var context = new ExifContext(data);
        ParseCore(context, keyPrefix);
        foreach (var (key, value) in context.Lines)
        {
            fields.Raw(key, value);
        }

        foreach (var warning in context.Warnings)
        {
            fields.Warning(DiagnosticCodes.InvalidLength, warning, new SourceRange(offset, length));
        }

        if (length > cap)
        {
            fields.Note(
                $"Exif block is {length:N0} byte(s); only the first {cap:N0} byte(s) were decoded because of the configured blob limit.",
                new SourceRange(offset, length));
        }
    }

    private static void ParseCore(ExifContext context, string keyPrefix)
    {
        context.KeyPrefix = keyPrefix;
        var data = context.Data;
        if (data.Length < 8)
        {
            context.Warn("Invalid EXIF data (need at least 8 bytes of TIFF header)");
            return;
        }

        var position = 0;

        // JPEG APP1 keeps the "Exif\0\0" identifier before the TIFF header; tolerate it here too.
        if (data.Length - position >= 6
            && data[position] == 'E'
            && data[position + 1] == 'x'
            && data[position + 2] == 'i'
            && data[position + 3] == 'f'
            && data[position + 4] == 0x00
            && data[position + 5] == 0x00)
        {
            context.Add("Container", "JPEG APP1 (\"Exif\\0\\0\" identifier skipped)");
            position += 6;
        }

        if (data.Length - position < 8)
        {
            context.Warn("Truncated TIFF header");
            return;
        }

        var byteOrder = Encoding.ASCII.GetString(data, position, 2);
        if (byteOrder == "II")
        {
            context.LittleEndian = true;
        }
        else if (byteOrder == "MM")
        {
            context.LittleEndian = false;
        }
        else
        {
            context.Warn($"Invalid TIFF byte order: {Convert.ToHexString(data, position, 2)}");
            return;
        }

        context.TiffBase = position;
        context.Add("TIFFHeaderOffset", position.ToString(CultureInfo.InvariantCulture));
        context.Add("ByteOrder", context.LittleEndian ? "II (LE)" : "MM (BE)");

        var magic = context.ReadU16(position + 2);
        context.Add("TIFFMagic", $"0x{magic:X4}");
        if (magic != 0x002A)
        {
            context.Warn(magic == 0x002B
                ? "BigTIFF (magic 0x002B) is not supported by this parser"
                : $"Invalid TIFF magic 0x{magic:X4}");
            return;
        }

        var ifd0 = context.ReadU32(position + 4);
        context.Add("IFDOffset", ifd0.ToString(CultureInfo.InvariantCulture));
        if (ifd0 == 0)
        {
            context.Warn("IFD0 offset is 0: no IFD present");
        }
        else
        {
            ParseTiffChain(context, ifd0);
        }

        EmitGpsSummary(context);
        context.Add(
            "Summary",
            $"{context.TagsDecoded} tags in {context.IfdsParsed} IFD(s), {context.Warnings.Count} warning(s)");
    }

    private static void ParseTiffChain(ExifContext context, uint firstOffset)
    {
        var relative = firstOffset;
        var index = 0;
        while (relative != 0 && index < MaxIfdChain)
        {
            relative = ParseIfd(context, "IFD" + index, ExifIfdKind.Tiff, relative, 0);
            index++;
        }

        if (relative != 0)
        {
            context.Warn($"IFD chain longer than {MaxIfdChain}, stopped at offset {relative}");
        }
    }

    private static uint ParseIfd(
        ExifContext context,
        string ifdName,
        ExifIfdKind kind,
        uint relativeOffset,
        int depth)
    {
        if (depth > MaxIfdDepth)
        {
            context.Warn($"{ifdName}: maximum IFD nesting depth reached, skipped");
            return 0;
        }

        if (context.IfdsParsed >= MaxIfdCount)
        {
            context.Warn($"{ifdName}: IFD budget ({MaxIfdCount}) exhausted, skipped");
            return 0;
        }

        var absolute = (long)context.TiffBase + relativeOffset;
        if (!context.Has(absolute, 2))
        {
            context.Warn($"{ifdName}: IFD offset {relativeOffset} points outside the data, skipped");
            return 0;
        }

        if (!context.VisitedIfd.Add(absolute))
        {
            context.Warn($"{ifdName}: IFD at offset {relativeOffset} already parsed (circular reference), skipped");
            return 0;
        }

        context.IfdsParsed++;
        var position = (int)absolute;
        var entryCount = context.ReadU16(position);
        position += 2;

        context.Add($"{ifdName}.Offset", relativeOffset.ToString(CultureInfo.InvariantCulture));
        context.Add($"{ifdName}.EntryCount", entryCount.ToString(CultureInfo.InvariantCulture));

        var fits = (context.Data.Length - (long)position) / 12;
        var usable = (int)entryCount;
        if (usable > fits)
        {
            context.Warn(
                $"{ifdName}: declares {entryCount} entries but only {Math.Max(0, fits)} fit in the buffer (truncated data)");
            usable = (int)Math.Max(0, fits);
        }

        if (usable > MaxEntriesPerIfd)
        {
            context.Warn($"{ifdName}: entry count {entryCount} exceeds sanity limit, clamped to {MaxEntriesPerIfd}");
            usable = MaxEntriesPerIfd;
        }

        uint exifPointer = 0;
        uint gpsPointer = 0;
        uint interopPointer = 0;
        uint thumbnailOffset = 0;
        uint thumbnailLength = 0;

        for (var index = 0; index < usable; index++)
        {
            var entry = position + (index * 12);
            var tag = context.ReadU16(entry);
            var type = context.ReadU16(entry + 2);
            var count = context.ReadU32(entry + 4);
            var valueOrOffset = context.ReadU32(entry + 8);

            var raw = ExtractValue(context, ifdName, tag, type, count, valueOrOffset, entry, out var error);
            var tagName = ExifTagResolver.ResolveTagName(kind, tag);
            var key = $"{ifdName}.{tagName}";
            var value = error is not null
                ? $"<{error}> valueField={Convert.ToHexString(context.Data, entry + 8, 4)}"
                : ExifTagResolver.ResolveV(kind, tag, type, raw, context.LittleEndian);

            context.Add(key, value);
            context.TagsDecoded++;

            if (kind == ExifIfdKind.Tiff)
            {
                switch ((ExifTag)tag)
                {
                    case ExifTag.ExifIFDPointer:
                        exifPointer = valueOrOffset;
                        break;
                    case ExifTag.GPSInfoIFDPointer:
                        gpsPointer = valueOrOffset;
                        break;
                    case ExifTag.JPEGInterchangeFormat:
                        thumbnailOffset = valueOrOffset;
                        break;
                    case ExifTag.JPEGInterchangeFormatLength:
                        thumbnailLength = valueOrOffset;
                        break;
                }
            }
            else if (kind == ExifIfdKind.Exif && tag == (ushort)ExifTag.InteroperabilityIFDPointer)
            {
                interopPointer = valueOrOffset;
            }

            if (kind == ExifIfdKind.Gps && error is null)
            {
                CaptureGps(context, tag, raw);
            }
        }

        uint next = 0;
        var nextPosition = (long)position + ((long)entryCount * 12);
        if (context.Has(nextPosition, 4))
        {
            next = context.ReadU32((int)nextPosition);
        }
        else
        {
            context.Warn($"{ifdName}: next-IFD pointer lies outside the data");
        }

        context.Add($"{ifdName}.NextIFDOffset", next.ToString(CultureInfo.InvariantCulture));
        if (kind != ExifIfdKind.Tiff && next != 0)
        {
            context.Warn($"{ifdName}: sub-IFD has a non-zero next-IFD pointer ({next}), ignored");
            next = 0;
        }

        if (thumbnailOffset != 0 || thumbnailLength != 0)
        {
            var ok = thumbnailLength != 0
                && context.Has((long)context.TiffBase + thumbnailOffset, thumbnailLength);
            context.Add(
                $"{ifdName}.ThumbnailData",
                $"offset={thumbnailOffset} length={thumbnailLength} {(ok ? "(in range)" : "(OUT OF RANGE)")}");
            if (!ok)
            {
                context.Warn($"{ifdName}: thumbnail range is invalid");
            }
        }

        if (exifPointer != 0)
        {
            ParseIfd(context, "Exif", ExifIfdKind.Exif, exifPointer, depth + 1);
        }

        if (interopPointer != 0)
        {
            ParseIfd(context, "Interop", ExifIfdKind.Interop, interopPointer, depth + 1);
        }

        if (gpsPointer != 0)
        {
            ParseIfd(context, "GPS", ExifIfdKind.Gps, gpsPointer, depth + 1);
        }

        return next;
    }

    private static byte[] ExtractValue(
        ExifContext context,
        string ifdName,
        ushort tag,
        ushort type,
        uint count,
        uint valueOrOffset,
        int entryAbsolute,
        out string? error)
    {
        error = null;
        var typeSize = ExifTagResolver.GetTypeSize(type);
        if (typeSize == 0)
        {
            error = $"unsupported field type {type}";
            context.Warn($"{ifdName}: tag 0x{tag:X4} has unsupported type {type}, value left undecoded");
            return [];
        }

        if (count == 0)
        {
            error = "count = 0 (empty value)";
            return [];
        }

        var total = (long)typeSize * count;
        if (total <= 4)
        {
            var inline = new byte[(int)total];
            Array.Copy(context.Data, entryAbsolute + 8, inline, 0, (int)total);
            return inline;
        }

        var absolute = (long)context.TiffBase + valueOrOffset;
        if (!context.Has(absolute, total))
        {
            error = $"value offset {valueOrOffset} + {total} bytes is outside the data";
            context.Warn($"{ifdName}: tag 0x{tag:X4} {error}");
            return [];
        }

        var buffer = new byte[(int)total];
        Array.Copy(context.Data, absolute, buffer, 0, (int)total);
        return buffer;
    }

    private static void CaptureGps(ExifContext context, ushort tag, byte[] raw)
    {
        switch ((GpsTag)tag)
        {
            case GpsTag.GPSLatitudeRef:
                context.GpsLatitudeReference = ExifTagResolver.ReadAscii(raw);
                break;
            case GpsTag.GPSLongitudeRef:
                context.GpsLongitudeReference = ExifTagResolver.ReadAscii(raw);
                break;
            case GpsTag.GPSLatitude:
                context.GpsLatitude = ExifTagResolver.ReadRationals(raw, context.LittleEndian);
                break;
            case GpsTag.GPSLongitude:
                context.GpsLongitude = ExifTagResolver.ReadRationals(raw, context.LittleEndian);
                break;
        }
    }

    private static void EmitGpsSummary(ExifContext context)
    {
        var latitude = ExifTagResolver.ToDegrees(context.GpsLatitude, context.GpsLatitudeReference);
        var longitude = ExifTagResolver.ToDegrees(context.GpsLongitude, context.GpsLongitudeReference);
        if (latitude is null || longitude is null)
        {
            return;
        }

        context.Add(
            "Computed.GPSPosition",
            $"{latitude.Value.ToString("0.######", CultureInfo.InvariantCulture)}, "
            + longitude.Value.ToString("0.######", CultureInfo.InvariantCulture));
    }

    private sealed class ExifContext
    {
        public ExifContext(byte[] data) => Data = data;

        public byte[] Data { get; }
        public int TiffBase { get; set; }
        public bool LittleEndian { get; set; }
        public string KeyPrefix { get; set; } = string.Empty;

        public List<(string Key, string Value)> Lines { get; } = [];
        public List<string> Warnings { get; } = [];
        public HashSet<long> VisitedIfd { get; } = [];

        public int IfdsParsed { get; set; }
        public int TagsDecoded { get; set; }

        public string? GpsLatitudeReference { get; set; }
        public string? GpsLongitudeReference { get; set; }
        public double[]? GpsLatitude { get; set; }
        public double[]? GpsLongitude { get; set; }

        public bool Has(long absoluteOffset, long length) =>
            absoluteOffset >= 0 && length >= 0 && absoluteOffset + length <= Data.Length;

        public ushort ReadU16(int absolute) => LittleEndian
            ? (ushort)(Data[absolute] | (Data[absolute + 1] << 8))
            : (ushort)((Data[absolute] << 8) | Data[absolute + 1]);

        public uint ReadU32(int absolute) => LittleEndian
            ? (uint)(Data[absolute]
                | (Data[absolute + 1] << 8)
                | (Data[absolute + 2] << 16)
                | (Data[absolute + 3] << 24))
            : (uint)((Data[absolute] << 24)
                | (Data[absolute + 1] << 16)
                | (Data[absolute + 2] << 8)
                | Data[absolute + 3]);

        public void Add(string key, string value)
        {
            var fullKey = string.IsNullOrEmpty(KeyPrefix) ? key : KeyPrefix + "." + key;
            Lines.Add((fullKey, value));
        }

        public void Warn(string message) => Warnings.Add(message);
    }
}
