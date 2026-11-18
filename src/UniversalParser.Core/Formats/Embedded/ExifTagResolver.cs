using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using UniversalParser.Core.Formatting;

namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// Resolves Exif tag names, type names, and human-readable values. Every offset is validated by
/// the caller; this class only formats the bytes it is given and never throws on malformed values.
/// </summary>
internal static class ExifTagResolver
{
    private const int MaxHexBytes = 32;
    private const int MaxTextChars = 256;
    private const int MaxListItems = 16;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string ResolveTagName(ExifIfdKind kind, ushort tag) => kind switch
    {
        ExifIfdKind.Gps => Enum.IsDefined(typeof(GpsTag), tag) ? ((GpsTag)tag).ToString() : Unknown(tag),
        ExifIfdKind.Interop => Enum.IsDefined(typeof(InteropTag), tag) ? ((InteropTag)tag).ToString() : Unknown(tag),
        _ => Enum.IsDefined(typeof(ExifTag), tag) ? ((ExifTag)tag).ToString() : Unknown(tag),
    };

    private static string Unknown(ushort tag) => $"UnknownTag_0x{tag:X4}";

    public static string ResolveTypeName(ushort type) =>
        Enum.IsDefined(typeof(ExifType), type) ? ((ExifType)type).ToString() : $"Type{type}";

    /// <summary>Component size in bytes; 0 means the caller must not guess.</summary>
    public static int GetTypeSize(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 or 13 => 4,
        5 or 10 or 12 or 16 or 17 or 18 => 8,
        _ => 0,
    };

    public static string ResolveV(ExifIfdKind kind, ushort tag, ushort type, byte[] raw, bool littleEndian)
    {
        if (raw.Length == 0)
        {
            return "<empty>";
        }

        var special = DecodeSpecialTag(kind, tag, raw, littleEndian);
        if (special is not null)
        {
            return special;
        }

        var exifType = (ExifType)type;
        var values = ReadValues(exifType, raw, littleEndian, out var totalCount);

        switch (exifType)
        {
            case ExifType.ASCII:
                return Quote(DecodeAsciiMulti(raw));
            case ExifType.Byte:
            case ExifType.SByte:
            case ExifType.Undefined:
                var hex = Hex(raw);
                var note = Describe(kind, tag, values);
                return note is null ? hex : $"{hex}  ({note})";
            default:
                if (values.Length == 0)
                {
                    return Hex(raw);
                }

                var body = FormatValues(values, totalCount);
                var annotation = Describe(kind, tag, values);
                return annotation is null ? body : $"{body}  ({annotation})";
        }
    }

    private readonly struct Num
    {
        public Num(double value)
        {
            Value = value;
            Numerator = 0;
            Denominator = 0;
            IsRational = false;
        }

        public Num(long numerator, long denominator)
        {
            Numerator = numerator;
            Denominator = denominator;
            IsRational = true;
            Value = denominator == 0 ? double.NaN : (double)numerator / denominator;
        }

        public double Value { get; }
        public long Numerator { get; }
        public long Denominator { get; }
        public bool IsRational { get; }

        public override string ToString()
        {
            if (!IsRational)
            {
                return Value.ToString("0.######", Inv);
            }

            if (Denominator == 0)
            {
                return $"{Numerator}/0 (undefined)";
            }

            if (Denominator == 1)
            {
                return Numerator.ToString(Inv);
            }

            return $"{Numerator}/{Denominator} ({Value.ToString("0.####", Inv)})";
        }
    }

    private static Num[] ReadValues(ExifType type, byte[] raw, bool littleEndian, out int totalCount)
    {
        var size = GetTypeSize((ushort)type);
        totalCount = size == 0 ? 0 : raw.Length / size;
        if (totalCount == 0)
        {
            return [];
        }

        var take = Math.Min(totalCount, MaxListItems);
        var list = new List<Num>(take);
        for (var index = 0; index < take; index++)
        {
            var offset = index * size;
            switch (type)
            {
                case ExifType.Byte:
                case ExifType.Undefined:
                    list.Add(new Num(raw[offset]));
                    break;
                case ExifType.SByte:
                    list.Add(new Num((sbyte)raw[offset]));
                    break;
                case ExifType.Short:
                    list.Add(new Num(U16(raw, offset, littleEndian)));
                    break;
                case ExifType.SShort:
                    list.Add(new Num((short)U16(raw, offset, littleEndian)));
                    break;
                case ExifType.Long:
                case ExifType.IFD:
                    list.Add(new Num(U32(raw, offset, littleEndian)));
                    break;
                case ExifType.SLong:
                    list.Add(new Num((int)U32(raw, offset, littleEndian)));
                    break;
                case ExifType.Rational:
                    list.Add(new Num(U32(raw, offset, littleEndian), U32(raw, offset + 4, littleEndian)));
                    break;
                case ExifType.SRational:
                    list.Add(new Num((int)U32(raw, offset, littleEndian), (int)U32(raw, offset + 4, littleEndian)));
                    break;
                case ExifType.Float:
                    list.Add(new Num(BitConverter.Int32BitsToSingle((int)U32(raw, offset, littleEndian))));
                    break;
                case ExifType.Double:
                    list.Add(new Num(BitConverter.Int64BitsToDouble((long)U64(raw, offset, littleEndian))));
                    break;
                case ExifType.Long8:
                case ExifType.IFD8:
                    list.Add(new Num(U64(raw, offset, littleEndian)));
                    break;
                case ExifType.SLong8:
                    list.Add(new Num((long)U64(raw, offset, littleEndian)));
                    break;
            }
        }

        return list.ToArray();
    }

    private static string FormatValues(Num[] values, int totalCount)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append(values[index].ToString());
        }

        if (totalCount > values.Length)
        {
            builder.Append(CultureInfo.InvariantCulture, $", … (+{totalCount - values.Length} more)");
        }

        return builder.ToString();
    }

    private static string? DecodeSpecialTag(ExifIfdKind kind, ushort tag, byte[] raw, bool littleEndian)
    {
        if (kind == ExifIfdKind.Gps)
        {
            return (GpsTag)tag switch
            {
                GpsTag.GPSProcessingMethod or GpsTag.GPSAreaInformation
                    => Quote(DecodeCharsetPrefixed(raw, littleEndian)),
                _ => null,
            };
        }

        if (kind == ExifIfdKind.Interop)
        {
            return tag == (ushort)InteropTag.InteroperabilityVersion ? DecodeVersion(raw) : null;
        }

        switch ((ExifTag)tag)
        {
            case ExifTag.ExifVersion:
            case ExifTag.FlashpixVersion:
                return DecodeVersion(raw);
            case ExifTag.ComponentsConfiguration:
                return DecodeComponentsConfiguration(raw);
            case ExifTag.UserComment:
                return Quote(DecodeCharsetPrefixed(raw, littleEndian));
            case ExifTag.XPTitle:
            case ExifTag.XPComment:
            case ExifTag.XPAuthor:
            case ExifTag.XPKeywords:
            case ExifTag.XPSubject:
                return Quote(DecodeUtf16Le(raw));
            case ExifTag.ApplicationNotes:
                return Quote(DecodeText(raw, 0, raw.Length));
            case ExifTag.MakerNote:
                return Blob(raw, GuessMakerNote(raw));
            case ExifTag.InterColorProfile:
                return Blob(raw, "ICC profile");
            case ExifTag.IPTC_NAA:
                return Blob(raw, "IPTC/NAA");
            case ExifTag.PhotoshopSettings:
                return Blob(raw, "Photoshop IRB");
            case ExifTag.CFAPattern:
                return Blob(raw, "CFA pattern");
            case ExifTag.OECF:
                return Blob(raw, "OECF");
            case ExifTag.SpatialFrequencyResponse:
                return Blob(raw, "SFR");
            case ExifTag.DeviceSettingDescription:
                return Blob(raw, "device setting");
            default:
                return null;
        }
    }

    private static string Blob(byte[] raw, string label) => $"{label}, {raw.Length} bytes: {Hex(raw)}";

    private static string DecodeVersion(byte[] raw)
    {
        var text = Encoding.ASCII.GetString(raw).Trim('\0', ' ');
        if (raw.Length >= 4 && IsAllDigits(text, 4))
        {
            var major = int.Parse(text[..2], Inv);
            var minor = text.Substring(2, 2);
            return $"{text} ({major}.{minor})";
        }

        return text.Length > 0 ? Quote(text) : Hex(raw);
    }

    private static bool IsAllDigits(string text, int count)
    {
        if (text.Length < count)
        {
            return false;
        }

        for (var index = 0; index < count; index++)
        {
            if (!char.IsDigit(text[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string DecodeComponentsConfiguration(byte[] raw)
    {
        string[] names = ["-", "Y", "Cb", "Cr", "R", "G", "B"];
        var parts = new List<string>();
        for (var index = 0; index < raw.Length && index < 4; index++)
        {
            parts.Add(raw[index] < names.Length ? names[raw[index]] : raw[index].ToString(Inv));
        }

        return $"{Hex(raw)}  ({string.Join(",", parts)})";
    }

    /// <summary>UserComment / GPSProcessingMethod: 8-byte charset code followed by text.</summary>
    private static string DecodeCharsetPrefixed(byte[] raw, bool littleEndian)
    {
        if (raw.Length <= 8)
        {
            return string.Empty;
        }

        var code = Encoding.ASCII.GetString(raw, 0, 8).TrimEnd('\0', ' ');
        var length = raw.Length - 8;
        if (code.StartsWith("UNICODE", StringComparison.OrdinalIgnoreCase))
        {
            var encoding = littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
            return encoding.GetString(raw, 8, length).Trim('\0', ' ');
        }

        return DecodeText(raw, 8, length).Trim('\0', ' ');
    }

    private static string DecodeUtf16Le(byte[] raw)
    {
        var length = raw.Length & ~1;
        return Encoding.Unicode.GetString(raw, 0, length).Trim('\0');
    }

    private static string DecodeAsciiMulti(byte[] raw)
    {
        var parts = new List<string>();
        var start = 0;
        for (var index = 0; index <= raw.Length; index++)
        {
            if (index == raw.Length || raw[index] == 0x00)
            {
                if (index > start)
                {
                    var text = DecodeText(raw, start, index - start).Trim();
                    if (text.Length > 0)
                    {
                        parts.Add(text);
                    }
                }

                start = index + 1;
            }
        }

        return string.Join(" | ", parts);
    }

    private static string DecodeText(byte[] raw, int offset, int length) =>
        TextDecoding.DisplayPredicted(raw.AsSpan(offset, length));

    public static string ReadAscii(byte[] raw)
    {
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        var length = Array.IndexOf(raw, (byte)0x00);
        if (length < 0)
        {
            length = raw.Length;
        }

        return Encoding.ASCII.GetString(raw, 0, length).Trim();
    }

    public static double[] ReadRationals(byte[] raw, bool littleEndian)
    {
        var count = raw.Length / 8;
        var result = new double[count];
        for (var index = 0; index < count; index++)
        {
            var numerator = U32(raw, index * 8, littleEndian);
            var denominator = U32(raw, index * 8 + 4, littleEndian);
            result[index] = denominator == 0 ? 0d : (double)numerator / denominator;
        }

        return result;
    }

    public static double? ToDegrees(double[]? dms, string? reference)
    {
        if (dms is null || dms.Length < 3)
        {
            return null;
        }

        var degrees = dms[0] + dms[1] / 60d + dms[2] / 3600d;
        if (double.IsNaN(degrees) || double.IsInfinity(degrees))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(reference))
        {
            var sign = char.ToUpperInvariant(reference[0]);
            if (sign is 'S' or 'W')
            {
                degrees = -degrees;
            }
        }

        return degrees;
    }

    private static string? Describe(ExifIfdKind kind, ushort tag, Num[] values)
    {
        if (values.Length == 0)
        {
            return null;
        }

        var first = values[0].Value;
        var firstInt = double.IsNaN(first) || double.IsInfinity(first) ? int.MinValue : (int)Math.Round(first);

        if (kind == ExifIfdKind.Gps)
        {
            return (GpsTag)tag switch
            {
                GpsTag.GPSVersionID => JoinInts(values, "."),
                GpsTag.GPSLatitude or GpsTag.GPSLongitude or GpsTag.GPSDestLatitude or GpsTag.GPSDestLongitude
                    => FormatDms(values),
                GpsTag.GPSAltitude => $"{first.ToString("0.###", Inv)} m",
                GpsTag.GPSAltitudeRef => Lookup(GpsAltitudeRef, firstInt),
                GpsTag.GPSDifferential => Lookup(GpsDifferential, firstInt),
                GpsTag.GPSTimeStamp => values.Length >= 3
                    ? $"{(int)values[0].Value:00}:{(int)values[1].Value:00}:{values[2].Value.ToString("00.###", Inv)} UTC"
                    : null,
                GpsTag.GPSImgDirection or GpsTag.GPSTrack or GpsTag.GPSDestBearing
                    => $"{first.ToString("0.##", Inv)}°",
                GpsTag.GPSHPositioningError => $"{first.ToString("0.##", Inv)} m",
                _ => null,
            };
        }

        if (kind == ExifIfdKind.Interop)
        {
            return null;
        }

        return (ExifTag)tag switch
        {
            ExifTag.Compression => Lookup(Compression, firstInt),
            ExifTag.PhotometricInterpretation => Lookup(Photometric, firstInt),
            ExifTag.Orientation => Lookup(Orientation, firstInt),
            ExifTag.PlanarConfiguration => Lookup(Planar, firstInt),
            ExifTag.ResolutionUnit or ExifTag.FocalPlaneResolutionUnit => Lookup(ResolutionUnit, firstInt),
            ExifTag.YCbCrPositioning => Lookup(YCbCrPositioning, firstInt),
            ExifTag.ExposureProgram => Lookup(ExposureProgram, firstInt),
            ExifTag.MeteringMode => Lookup(MeteringMode, firstInt),
            ExifTag.LightSource => Lookup(LightSource, firstInt),
            ExifTag.Flash => DescribeFlash(firstInt),
            ExifTag.ColorSpace => Lookup(ColorSpace, firstInt),
            ExifTag.SensingMethod => Lookup(SensingMethod, firstInt),
            ExifTag.FileSource => Lookup(FileSource, firstInt),
            ExifTag.SceneType => firstInt == 1 ? "directly photographed" : $"unknown ({firstInt})",
            ExifTag.CustomRendered => Lookup(CustomRendered, firstInt),
            ExifTag.ExposureMode => Lookup(ExposureMode, firstInt),
            ExifTag.WhiteBalance => Lookup(WhiteBalance, firstInt),
            ExifTag.SceneCaptureType => Lookup(SceneCaptureType, firstInt),
            ExifTag.GainControl => Lookup(GainControl, firstInt),
            ExifTag.Contrast or ExifTag.Saturation or ExifTag.Sharpness => Lookup(NormalSoftHard, firstInt),
            ExifTag.SubjectDistanceRange => Lookup(SubjectDistanceRange, firstInt),
            ExifTag.SensitivityType => Lookup(SensitivityType, firstInt),
            ExifTag.ExposureTime => FormatShutter(first),
            ExifTag.ShutterSpeedValue => FormatShutter(Math.Pow(2, -first)) is { } shutter
                ? $"{shutter}, APEX Tv"
                : "APEX Tv",
            ExifTag.FNumber => "f/" + first.ToString("0.##", Inv),
            ExifTag.ApertureValue or ExifTag.MaxApertureValue =>
                "f/" + Math.Pow(2, first / 2).ToString("0.##", Inv) + ", APEX Av",
            ExifTag.BrightnessValue => first.ToString("0.##", Inv) + " EV (APEX Bv)",
            ExifTag.ExposureBiasValue => (first >= 0 ? "+" : string.Empty) + first.ToString("0.##", Inv) + " EV",
            ExifTag.FocalLength => first.ToString("0.##", Inv) + " mm",
            ExifTag.FocalLengthIn35mmFormat => first.ToString("0.##", Inv) + " mm (35 mm equiv.)",
            ExifTag.SubjectDistance => first <= 0 ? "unknown / infinity" : first.ToString("0.###", Inv) + " m",
            ExifTag.DigitalZoomRatio => first == 0 ? "not used" : first.ToString("0.##", Inv) + "x",
            ExifTag.LensSpecification => DescribeLensSpec(values),
            ExifTag.XResolution or ExifTag.YResolution => first.ToString("0.##", Inv) + " px/unit",
            _ => null,
        };
    }

    private static string? FormatShutter(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
        {
            return null;
        }

        return seconds >= 1
            ? seconds.ToString("0.##", Inv) + " s"
            : $"1/{Math.Round(1 / seconds).ToString("0", Inv)} s";
    }

    private static string DescribeFlash(int value)
    {
        if (value == int.MinValue)
        {
            return "invalid";
        }

        var parts = new List<string> { (value & 0x01) != 0 ? "fired" : "did not fire" };
        switch ((value >> 1) & 0x03)
        {
            case 2:
                parts.Add("no strobe return");
                break;
            case 3:
                parts.Add("strobe return detected");
                break;
        }

        switch ((value >> 3) & 0x03)
        {
            case 1:
                parts.Add("compulsory firing");
                break;
            case 2:
                parts.Add("compulsory suppression");
                break;
            case 3:
                parts.Add("auto mode");
                break;
        }

        if ((value & 0x20) != 0)
        {
            parts.Add("no flash function");
        }

        if ((value & 0x40) != 0)
        {
            parts.Add("red-eye reduction");
        }

        return string.Join(", ", parts);
    }

    private static string? DescribeLensSpec(Num[] values)
    {
        if (values.Length < 4)
        {
            return null;
        }

        var focal = Math.Abs(values[0].Value - values[1].Value) < 0.01
            ? $"{values[0].Value.ToString("0.##", Inv)}mm"
            : $"{values[0].Value.ToString("0.##", Inv)}-{values[1].Value.ToString("0.##", Inv)}mm";
        var aperture = Math.Abs(values[2].Value - values[3].Value) < 0.01
            ? $"f/{values[2].Value.ToString("0.##", Inv)}"
            : $"f/{values[2].Value.ToString("0.##", Inv)}-{values[3].Value.ToString("0.##", Inv)}";
        return $"{focal} {aperture}";
    }

    private static string? FormatDms(Num[] values)
    {
        if (values.Length < 3)
        {
            return null;
        }

        return $"{values[0].Value.ToString("0.####", Inv)}° "
            + $"{values[1].Value.ToString("0.####", Inv)}' "
            + $"{values[2].Value.ToString("0.####", Inv)}\"";
    }

    private static string JoinInts(Num[] values, string separator)
    {
        var parts = new List<string>(values.Length);
        foreach (var value in values)
        {
            parts.Add(((int)value.Value).ToString(Inv));
        }

        return string.Join(separator, parts);
    }

    private static string GuessMakerNote(byte[] raw)
    {
        var head = Encoding.ASCII.GetString(raw, 0, Math.Min(raw.Length, 12));
        string[] signatures =
        [
            "Nikon", "OLYMP", "FUJIFILM", "SONY", "Apple iOS", "Panasonic",
            "Ricoh", "SIGMA", "SAMSUNG", "PENTAX", "LEICA", "CASIO",
        ];
        foreach (var signature in signatures)
        {
            if (head.StartsWith(signature, StringComparison.OrdinalIgnoreCase))
            {
                return $"MakerNote [{signature}] (not parsed)";
            }
        }

        return "MakerNote [vendor unknown] (not parsed)";
    }

    private static string Lookup(IReadOnlyDictionary<int, string> map, int value) =>
        map.TryGetValue(value, out var text) ? text : $"unknown ({value})";

    private static readonly FrozenDictionary<int, string> Compression = new Dictionary<int, string>()
    {
        [1] = "uncompressed",
        [2] = "CCITT 1D",
        [3] = "T4/G3",
        [4] = "T6/G4",
        [5] = "LZW",
        [6] = "JPEG (old style / thumbnail)",
        [7] = "JPEG",
        [8] = "Adobe Deflate",
        [32773] = "PackBits",
        [34712] = "JPEG2000",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> Photometric = new Dictionary<int, string>()
    {
        [0] = "WhiteIsZero",
        [1] = "BlackIsZero",
        [2] = "RGB",
        [3] = "palette",
        [4] = "transparency mask",
        [5] = "CMYK",
        [6] = "YCbCr",
        [8] = "CIELab",
        [32803] = "CFA (raw)",
        [34892] = "LinearRaw",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> Orientation = new Dictionary<int, string>()
    {
        [1] = "0° (normal)",
        [2] = "mirror horizontal",
        [3] = "rotate 180°",
        [4] = "mirror vertical",
        [5] = "mirror horizontal + rotate 270° CW",
        [6] = "rotate 90° CW",
        [7] = "mirror horizontal + rotate 90° CW",
        [8] = "rotate 270° CW",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> Planar = new Dictionary<int, string>()
    {
        [1] = "chunky",
        [2] = "planar",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> ResolutionUnit = new Dictionary<int, string>()
    {
        [1] = "none",
        [2] = "inch",
        [3] = "cm",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> YCbCrPositioning = new Dictionary<int, string>()
    {
        [1] = "centered",
        [2] = "co-sited",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> ColorSpace = new Dictionary<int, string>()
    {
        [1] = "sRGB",
        [2] = "Adobe RGB",
        [0xFFFD] = "Wide Gamut RGB",
        [0xFFFE] = "ICC profile",
        [0xFFFF] = "uncalibrated",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> ExposureProgram = new Dictionary<int, string>()
    {
        [0] = "not defined",
        [1] = "manual",
        [2] = "program AE",
        [3] = "aperture priority",
        [4] = "shutter priority",
        [5] = "creative (slow)",
        [6] = "action (fast)",
        [7] = "portrait",
        [8] = "landscape",
        [9] = "bulb",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> MeteringMode = new Dictionary<int, string>()
    {
        [0] = "unknown",
        [1] = "average",
        [2] = "center weighted average",
        [3] = "spot",
        [4] = "multi-spot",
        [5] = "multi-segment / pattern",
        [6] = "partial",
        [255] = "other",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> LightSource = new Dictionary<int, string>()
    {
        [0] = "unknown",
        [1] = "daylight",
        [2] = "fluorescent",
        [3] = "tungsten",
        [4] = "flash",
        [9] = "fine weather",
        [10] = "cloudy",
        [11] = "shade",
        [12] = "daylight fluorescent",
        [13] = "day white fluorescent",
        [14] = "cool white fluorescent",
        [15] = "white fluorescent",
        [17] = "standard light A",
        [18] = "standard light B",
        [19] = "standard light C",
        [20] = "D55",
        [21] = "D65",
        [22] = "D75",
        [23] = "D50",
        [24] = "ISO studio tungsten",
        [255] = "other",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> SensingMethod = new Dictionary<int, string>()
    {
        [1] = "not defined",
        [2] = "one-chip color area",
        [3] = "two-chip color area",
        [4] = "three-chip color area",
        [5] = "color sequential area",
        [7] = "trilinear",
        [8] = "color sequential linear",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> FileSource = new Dictionary<int, string>()
    {
        [1] = "film scanner",
        [2] = "reflection print scanner",
        [3] = "digital camera",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> CustomRendered = new Dictionary<int, string>()
    {
        [0] = "normal",
        [1] = "custom",
        [2] = "HDR (no original)",
        [3] = "HDR (with original)",
        [6] = "panorama",
        [8] = "portrait HDR",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> ExposureMode = new Dictionary<int, string>()
    {
        [0] = "auto",
        [1] = "manual",
        [2] = "auto bracket",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> WhiteBalance = new Dictionary<int, string>()
    {
        [0] = "auto",
        [1] = "manual",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> SceneCaptureType = new Dictionary<int, string>()
    {
        [0] = "standard",
        [1] = "landscape",
        [2] = "portrait",
        [3] = "night scene",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> GainControl = new Dictionary<int, string>()
    {
        [0] = "none",
        [1] = "low gain up",
        [2] = "high gain up",
        [3] = "low gain down",
        [4] = "high gain down",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> NormalSoftHard = new Dictionary<int, string>()
    {
        [0] = "normal",
        [1] = "low / soft",
        [2] = "high / hard",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> SubjectDistanceRange = new Dictionary<int, string>()
    {
        [0] = "unknown",
        [1] = "macro",
        [2] = "close",
        [3] = "distant",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> SensitivityType = new Dictionary<int, string>()
    {
        [0] = "unknown",
        [1] = "SOS",
        [2] = "REI",
        [3] = "ISO speed",
        [4] = "SOS+REI",
        [5] = "SOS+ISO",
        [6] = "REI+ISO",
        [7] = "SOS+REI+ISO",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> GpsAltitudeRef = new Dictionary<int, string>()
    {
        [0] = "above sea level",
        [1] = "below sea level",
        [2] = "positive (WGS-84 ellipsoid)",
        [3] = "negative (WGS-84 ellipsoid)",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> GpsDifferential = new Dictionary<int, string>()
    {
        [0] = "no correction",
        [1] = "differential corrected",
    }.ToFrozenDictionary();

    private static string Hex(byte[] raw)
    {
        if (raw.Length <= MaxHexBytes)
        {
            return Convert.ToHexString(raw);
        }

        return Convert.ToHexString(raw, 0, MaxHexBytes) + $"… (+{raw.Length - MaxHexBytes} bytes)";
    }

    private static string Quote(string text) => "\"" + Escape(text) + "\"";

    private static string Escape(string text) => TextEscaping.Escape(
        text,
        escapeNull: false,
        escapeAllControls: true,
        escapeQuote: true,
        maxOutputLength: MaxTextChars);

    private static ushort U16(byte[] data, int offset, bool littleEndian) => littleEndian
        ? (ushort)(data[offset] | (data[offset + 1] << 8))
        : (ushort)((data[offset] << 8) | data[offset + 1]);

    private static uint U32(byte[] data, int offset, bool littleEndian) => littleEndian
        ? (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24))
        : (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);

    private static ulong U64(byte[] data, int offset, bool littleEndian)
    {
        var low = U32(data, littleEndian ? offset : offset + 4, littleEndian);
        var high = U32(data, littleEndian ? offset + 4 : offset, littleEndian);
        return ((ulong)high << 32) | low;
    }
}
