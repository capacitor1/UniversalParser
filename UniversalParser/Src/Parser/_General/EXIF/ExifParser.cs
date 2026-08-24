#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UniversalParser.Src.Parser.EXIF;

/// <summary>
/// Raw TIFF/Exif IFD walker.
/// Design rules:
///   * never throw on malformed input - collect warnings instead;
///   * never trust count/offset fields - all bounds math is done in long;
///   * every offset in the stream is relative to the TIFF header (TiffBase).
/// </summary>
public static class ExifParser
{
    private const int MaxIfdDepth = 6;        // ExifIFD -> InteropIFD is depth 2
    private const int MaxIfdCount = 32;       // total IFD budget (loop/fan-out guard)
    private const int MaxEntriesPerIfd = 4096;
    private const int MaxIfdChain = 8;        // IFD0 -> IFD1 -> ...

    public static Dictionary<string, string> Parse(byte[] data, int startOffset = 0)
    {
        var ctx = new ExifContext(data ?? Array.Empty<byte>());
        var result = ctx.Result;

        if (ctx.Data.Length == 0 || startOffset < 0 || ctx.Data.Length - startOffset < 8)
        {
            result["Error"] = "Invalid EXIF data (need at least 8 bytes of TIFF header)";
            return result;
        }

        int p = startOffset;

        // -----------------------------
        // 0. Container tolerance: JPEG APP1 payload starts with "Exif\0\0"
        // -----------------------------
        if (ctx.Data.Length - p >= 6 &&
            ctx.Data[p + 0] == 'E' && ctx.Data[p + 1] == 'x' &&
            ctx.Data[p + 2] == 'i' && ctx.Data[p + 3] == 'f' &&
            ctx.Data[p + 4] == 0x00 && ctx.Data[p + 5] == 0x00)
        {
            result["Container"] = @"JPEG APP1 (""Exif\0\0"" identifier skipped)";
            p += 6;
        }

        if (ctx.Data.Length - p < 8)
        {
            result["Error"] = "Truncated TIFF header";
            return result;
        }

        // -----------------------------
        // 1. Byte order
        // -----------------------------
        string byteOrder = Encoding.ASCII.GetString(ctx.Data, p, 2);
        if (byteOrder == "II") ctx.LittleEndian = true;
        else if (byteOrder == "MM") ctx.LittleEndian = false;
        else
        {
            result["Error"] = $"Invalid TIFF byte order: {Convert.ToHexString(ctx.Data, p, 2)}";
            return result;
        }

        ctx.TiffBase = p;
        result["TIFFHeaderOffset"] = p.ToString();
        result["ByteOrder"] = ctx.LittleEndian ? "II (LE)" : "MM (BE)";

        // -----------------------------
        // 2. TIFF magic
        // -----------------------------
        ushort magic = ctx.ReadU16(p + 2);
        result["TIFFMagic"] = $"0x{magic:X4}";
        if (magic != 0x002A)
        {
            result["Error"] = magic == 0x002B
                ? "BigTIFF (magic 0x002B) is not supported by this parser"
                : $"Invalid TIFF magic 0x{magic:X4}";
            return result;
        }

        // -----------------------------
        // 3. IFD0 offset (relative to TiffBase!)
        // -----------------------------
        uint ifd0 = ctx.ReadU32(p + 4);
        result["IFDOffset"] = ifd0.ToString();

        if (ifd0 == 0)
            ctx.Warn("IFD0 offset is 0: no IFD present");
        else
            ParseTiffChain(ctx, ifd0);

        // -----------------------------
        // 4. Derived values + diagnostics
        // -----------------------------
        EmitGpsSummary(ctx);

        result["Summary"] = $"{ctx.TagsDecoded} tags in {ctx.IfdsParsed} IFD(s), " +
                            $"{ctx.Warnings.Count} warning(s)";

        for (int i = 0; i < ctx.Warnings.Count; i++)
            result[$"Warning[{i}]"] = ctx.Warnings[i];

        return result;
    }

    // =====================================================
    // IFD0 -> IFD1 -> ... chain
    // =====================================================
    private static void ParseTiffChain(ExifContext ctx, uint firstOffset)
    {
        uint rel = firstOffset;
        int index = 0;

        while (rel != 0 && index < MaxIfdChain)
        {
            rel = ParseIfd(ctx, "IFD" + index, ExifIfdKind.Tiff, rel, 0);
            index++;
        }

        if (rel != 0)
            ctx.Warn($"IFD chain longer than {MaxIfdChain}, stopped at offset {rel}");
    }

    /// <summary>Parses one IFD and returns its next-IFD offset (0 = end of chain).</summary>
    private static uint ParseIfd(ExifContext ctx, string ifdName, ExifIfdKind kind, uint relOffset, int depth)
    {
        if (depth > MaxIfdDepth)
        {
            ctx.Warn($"{ifdName}: maximum IFD nesting depth reached, skipped");
            return 0;
        }
        if (ctx.IfdsParsed >= MaxIfdCount)
        {
            ctx.Warn($"{ifdName}: IFD budget ({MaxIfdCount}) exhausted, skipped");
            return 0;
        }

        long abs = (long)ctx.TiffBase + relOffset;
        if (!ctx.Has(abs, 2))
        {
            ctx.Warn($"{ifdName}: IFD offset {relOffset} points outside the data, skipped");
            return 0;
        }
        if (!ctx.VisitedIfd.Add(abs))
        {
            ctx.Warn($"{ifdName}: IFD at offset {relOffset} already parsed (circular reference), skipped");
            return 0;
        }
        ctx.IfdsParsed++;

        int p = (int)abs;
        ushort entryCount = ctx.ReadU16(p);
        p += 2;

        ctx.Add($"{ifdName}.Offset", relOffset.ToString());
        ctx.Add($"{ifdName}.EntryCount", entryCount.ToString());

        // how many 12-byte entries actually fit?
        long fits = (ctx.Data.Length - (long)p) / 12;
        int usable = entryCount;
        if (usable > fits)
        {
            ctx.Warn($"{ifdName}: declares {entryCount} entries but only {Math.Max(0, fits)} fit in the buffer (truncated data)");
            usable = (int)Math.Max(0, fits);
        }
        if (usable > MaxEntriesPerIfd)
        {
            ctx.Warn($"{ifdName}: entry count {entryCount} exceeds sanity limit, clamped to {MaxEntriesPerIfd}");
            usable = MaxEntriesPerIfd;
        }

        uint exifPtr = 0, gpsPtr = 0, interopPtr = 0;
        uint thumbOffset = 0, thumbLength = 0;

        // -----------------------------
        // entries
        // -----------------------------
        for (int i = 0; i < usable; i++)
        {
            int entry = p + i * 12;

            ushort tag = ctx.ReadU16(entry);
            ushort type = ctx.ReadU16(entry + 2);
            uint count = ctx.ReadU32(entry + 4);
            uint valueOrOffset = ctx.ReadU32(entry + 8);

            byte[] raw = ExtractValue(ctx, ifdName, tag, type, count, valueOrOffset, entry, out string? error);

            string key = ExifTagResolver.ResolveK(kind, ifdName, tag, type, count);
            string value = error != null
                ? $"<{error}> valueField={Convert.ToHexString(ctx.Data, entry + 8, 4)}"
                : ExifTagResolver.ResolveV(kind, tag, type, raw, ctx.LittleEndian);

            ctx.Add(key, value);
            ctx.TagsDecoded++;

            // ---- SubIFD pointers: the whole point of a real EXIF parser ----
            if (kind == ExifIfdKind.Tiff)
            {
                switch ((ExifTag)tag)
                {
                    case ExifTag.ExifIFDPointer: exifPtr = valueOrOffset; break;
                    case ExifTag.GPSInfoIFDPointer: gpsPtr = valueOrOffset; break;
                    case ExifTag.JPEGInterchangeFormat: thumbOffset = valueOrOffset; break;
                    case ExifTag.JPEGInterchangeFormatLength: thumbLength = valueOrOffset; break;
                }
            }
            else if (kind == ExifIfdKind.Exif && tag == (ushort)ExifTag.InteroperabilityIFDPointer)
            {
                interopPtr = valueOrOffset;
            }

            if (kind == ExifIfdKind.Gps && error == null)
                CaptureGps(ctx, tag, raw);
        }

        // -----------------------------
        // next IFD pointer (was missing entirely)
        // -----------------------------
        uint next = 0;
        long nextPos = (long)p + (long)entryCount * 12;
        if (ctx.Has(nextPos, 4))
            next = ctx.ReadU32((int)nextPos);
        else
            ctx.Warn($"{ifdName}: next-IFD pointer lies outside the data");

        ctx.Add($"{ifdName}.NextIFDOffset", next.ToString());

        if (kind != ExifIfdKind.Tiff && next != 0)
        {
            ctx.Warn($"{ifdName}: sub-IFD has a non-zero next-IFD pointer ({next}), ignored");
            next = 0;
        }

        // -----------------------------
        // thumbnail bookkeeping (usually IFD1)
        // -----------------------------
        if (thumbOffset != 0 || thumbLength != 0)
        {
            bool ok = thumbLength != 0 &&
                      ctx.Has((long)ctx.TiffBase + thumbOffset, thumbLength);
            ctx.Add($"{ifdName}.ThumbnailData",
                    $"offset={thumbOffset} length={thumbLength} {(ok ? "(in range)" : "(OUT OF RANGE)")}");
            if (!ok) ctx.Warn($"{ifdName}: thumbnail range is invalid");
        }

        // -----------------------------
        // recurse (depth/loop guarded above)
        // -----------------------------
        if (exifPtr != 0) ParseIfd(ctx, "Exif", ExifIfdKind.Exif, exifPtr, depth + 1);
        if (interopPtr != 0) ParseIfd(ctx, "Interop", ExifIfdKind.Interop, interopPtr, depth + 1);
        if (gpsPtr != 0) ParseIfd(ctx, "GPS", ExifIfdKind.Gps, gpsPtr, depth + 1);

        return next;
    }

    // =====================================================
    // value extraction (inline / offset) - overflow safe
    // =====================================================
    private static byte[] ExtractValue(
        ExifContext ctx,
        string ifdName,
        ushort tag,
        ushort type,
        uint count,
        uint valueOrOffset,
        int entryAbs,
        out string? error)
    {
        error = null;

        int typeSize = ExifTagResolver.GetTypeSize(type);
        if (typeSize == 0)
        {
            error = $"unsupported field type {type}";
            ctx.Warn($"{ifdName}: tag 0x{tag:X4} has unsupported type {type}, value left undecoded");
            return Array.Empty<byte>();
        }

        if (count == 0)
        {
            error = "count = 0 (empty value)";
            return Array.Empty<byte>();
        }

        long total = (long)typeSize * count;   // long: no truncation, no negative sizes

        // -----------------------------
        // CASE 1: inline value (<= 4 bytes, left justified in the value field)
        // Read straight from the buffer -> independent of host endianness.
        // -----------------------------
        if (total <= 4)
        {
            var inline = new byte[total];
            Array.Copy(ctx.Data, entryAbs + 8, inline, 0, (int)total);
            return inline;
        }

        // -----------------------------
        // CASE 2: offset to data (relative to the TIFF header)
        // -----------------------------
        long abs = (long)ctx.TiffBase + valueOrOffset;
        if (!ctx.Has(abs, total))
        {
            error = $"value offset {valueOrOffset} + {total} bytes is outside the data";
            ctx.Warn($"{ifdName}: tag 0x{tag:X4} {error}");
            return Array.Empty<byte>();   // never fabricate a fake 0x00 value
        }

        var buffer = new byte[total];      // bounded by Has() -> bounded by input size
        Array.Copy(ctx.Data, abs, buffer, 0, total);
        return buffer;
    }

    // =====================================================
    // GPS aggregation
    // =====================================================
    private static void CaptureGps(ExifContext ctx, ushort tag, byte[] raw)
    {
        switch ((GpsTag)tag)
        {
            case GpsTag.GPSLatitudeRef: ctx.GpsLatRef = ExifTagResolver.ReadAscii(raw); break;
            case GpsTag.GPSLongitudeRef: ctx.GpsLonRef = ExifTagResolver.ReadAscii(raw); break;
            case GpsTag.GPSLatitude: ctx.GpsLat = ExifTagResolver.ReadRationals(raw, ctx.LittleEndian); break;
            case GpsTag.GPSLongitude: ctx.GpsLon = ExifTagResolver.ReadRationals(raw, ctx.LittleEndian); break;
        }
    }

    private static void EmitGpsSummary(ExifContext ctx)
    {
        double? lat = ExifTagResolver.ToDegrees(ctx.GpsLat, ctx.GpsLatRef);
        double? lon = ExifTagResolver.ToDegrees(ctx.GpsLon, ctx.GpsLonRef);
        if (lat == null || lon == null) return;

        var inv = CultureInfo.InvariantCulture;
        ctx.Add("Computed.GPSPosition",
                $"{lat.Value.ToString("0.######", inv)}, {lon.Value.ToString("0.######", inv)}");
    }
}

/// <summary>Parser state: buffer + byte order + TIFF base + collected output.</summary>
internal sealed class ExifContext
{
    public readonly byte[] Data;
    public int TiffBase;
    public bool LittleEndian;

    public readonly Dictionary<string, string> Result = new(StringComparer.Ordinal);
    public readonly List<string> Warnings = new();
    public readonly HashSet<long> VisitedIfd = new();

    public int IfdsParsed;
    public int TagsDecoded;

    // GPS accumulators
    public string? GpsLatRef, GpsLonRef;
    public double[]? GpsLat, GpsLon;

    public ExifContext(byte[] data) => Data = data;

    /// <summary>Overflow-proof bounds check (all arithmetic in long).</summary>
    public bool Has(long absOffset, long length)
        => absOffset >= 0 && length >= 0 && absOffset + length <= Data.Length;

    public ushort ReadU16(int abs) => LittleEndian
        ? (ushort)(Data[abs] | (Data[abs + 1] << 8))
        : (ushort)((Data[abs] << 8) | Data[abs + 1]);

    public uint ReadU32(int abs) => LittleEndian
        ? (uint)(Data[abs] | (Data[abs + 1] << 8) | (Data[abs + 2] << 16) | (Data[abs + 3] << 24))
        : (uint)((Data[abs] << 24) | (Data[abs + 1] << 16) | (Data[abs + 2] << 8) | Data[abs + 3]);

    /// <summary>Insert without silently overwriting duplicate keys.</summary>
    public void Add(string key, string value)
    {
        if (Result.TryAdd(key, value)) return;
        for (int i = 2; i < int.MaxValue; i++)
            if (Result.TryAdd($"{key} #{i}", value)) return;
    }

    public void Warn(string message) => Warnings.Add(message);
}