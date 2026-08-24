#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UniversalParser.Src.Parser.EXIF
{
    internal static class ExifTagResolver
    {
        private const int MaxHexBytes = 32;    // binary blobs are truncated for display
        private const int MaxTextChars = 256;
        private const int MaxListItems = 16;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        // =====================================================
        // key:  "     IFD0.Make<ASCII>[6]"     ([n] = component count)
        // =====================================================
        public static string ResolveK(ExifIfdKind kind, string ifdName, ushort tag, ushort type, uint count)
            => $"     {ifdName}.{ResolveTagName(kind, tag)}<{ResolveTypeName(type)}>[{count}]";

        public static string ResolveTagName(ExifIfdKind kind, ushort tag) => kind switch
        {
            ExifIfdKind.Gps => Enum.IsDefined(typeof(GpsTag), tag) ? ((GpsTag)tag).ToString() : Unknown(tag),
            ExifIfdKind.Interop => Enum.IsDefined(typeof(InteropTag), tag) ? ((InteropTag)tag).ToString() : Unknown(tag),
            _ => Enum.IsDefined(typeof(ExifTag), tag) ? ((ExifTag)tag).ToString() : Unknown(tag)
        };

        private static string Unknown(ushort tag) => $"UnknownTag_0x{tag:X4}";

        public static string ResolveTypeName(ushort type)
            => Enum.IsDefined(typeof(ExifType), type) ? ((ExifType)type).ToString() : $"Type{type}";

        /// <summary>Component size in bytes. 0 = unknown type (caller must NOT guess).</summary>
        public static int GetTypeSize(ushort type) => type switch
        {
            1 or 2 or 6 or 7 => 1,                  // BYTE / ASCII / SBYTE / UNDEFINED
            3 or 8 => 2,                            // SHORT / SSHORT
            4 or 9 or 11 or 13 => 4,                // LONG / SLONG / FLOAT / IFD
            5 or 10 or 12 or 16 or 17 or 18 => 8,   // RATIONAL / SRATIONAL / DOUBLE / *8
            _ => 0
        };

        // =====================================================
        // value
        // =====================================================
        public static string ResolveV(ExifIfdKind kind, ushort tag, ushort type, byte[] raw, bool le)
        {
            if (raw == null || raw.Length == 0) return "<empty>";

            // tag-specific payloads that the type alone cannot describe
            string? special = DecodeSpecialTag(kind, tag, raw, le);
            if (special != null) return special;

            var t = (ExifType)type;
            Num[] values = ReadValues(t, raw, le, out int totalCount);

            switch (t)
            {
                case ExifType.ASCII:
                    return Quote(DecodeAsciiMulti(raw));

                case ExifType.Byte:
                case ExifType.SByte:
                case ExifType.Undefined:
                {
                    string hex = Hex(raw);
                    string? note = Describe(kind, tag, values);
                    return note == null ? hex : $"{hex}  ({note})";
                }

                default:
                {
                    if (values.Length == 0) return Hex(raw);
                    string body = FormatValues(values, totalCount);
                    string? note = Describe(kind, tag, values);
                    return note == null ? body : $"{body}  ({note})";
                }
            }
        }

        // -----------------------------------------------------
        // component reader: keeps rational numerator/denominator
        // -----------------------------------------------------
        private readonly struct Num
        {
            public readonly double Value;
            public readonly long Numerator;
            public readonly long Denominator;
            public readonly bool IsRational;

            public Num(double value)
            {
                Value = value; Numerator = 0; Denominator = 0; IsRational = false;
            }

            public Num(long numerator, long denominator)
            {
                Numerator = numerator; Denominator = denominator; IsRational = true;
                Value = denominator == 0 ? double.NaN : (double)numerator / denominator;
            }

            public override string ToString()
            {
                if (!IsRational) return Value.ToString("0.######", Inv);
                if (Denominator == 0) return $"{Numerator}/0 (undefined)";
                if (Denominator == 1) return Numerator.ToString(Inv);
                return $"{Numerator}/{Denominator} ({Value.ToString("0.####", Inv)})";
            }
        }

        private static Num[] ReadValues(ExifType t, byte[] raw, bool le, out int totalCount)
        {
            int size = GetTypeSize((ushort)t);
            totalCount = size == 0 ? 0 : raw.Length / size;
            if (totalCount == 0) return Array.Empty<Num>();

            int take = Math.Min(totalCount, MaxListItems);
            var list = new List<Num>(take);

            for (int i = 0; i < take; i++)
            {
                int o = i * size;
                switch (t)
                {
                    case ExifType.Byte:
                    case ExifType.Undefined: list.Add(new Num(raw[o])); break;
                    case ExifType.SByte: list.Add(new Num((sbyte)raw[o])); break;
                    case ExifType.Short: list.Add(new Num(U16(raw, o, le))); break;
                    case ExifType.SShort: list.Add(new Num((short)U16(raw, o, le))); break;
                    case ExifType.Long:
                    case ExifType.IFD: list.Add(new Num(U32(raw, o, le))); break;
                    case ExifType.SLong: list.Add(new Num((int)U32(raw, o, le))); break;
                    case ExifType.Rational: list.Add(new Num(U32(raw, o, le), U32(raw, o + 4, le))); break;
                    case ExifType.SRational: list.Add(new Num((int)U32(raw, o, le), (int)U32(raw, o + 4, le))); break;
                    case ExifType.Float: list.Add(new Num(BitConverter.Int32BitsToSingle((int)U32(raw, o, le)))); break;
                    case ExifType.Double: list.Add(new Num(BitConverter.Int64BitsToDouble((long)U64(raw, o, le)))); break;
                    case ExifType.Long8:
                    case ExifType.IFD8: list.Add(new Num((double)U64(raw, o, le))); break;
                    case ExifType.SLong8: list.Add(new Num((double)(long)U64(raw, o, le))); break;
                }
            }
            return list.ToArray();
        }

        private static string FormatValues(Num[] values, int totalCount)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(values[i].ToString());
            }
            if (totalCount > values.Length)
                sb.Append($", … (+{totalCount - values.Length} more)");
            return sb.ToString();
        }

        // =====================================================
        // tag specific payloads
        // =====================================================
        private static string? DecodeSpecialTag(ExifIfdKind kind, ushort tag, byte[] raw, bool le)
        {
            if (kind == ExifIfdKind.Gps)
            {
                return (GpsTag)tag switch
                {
                    GpsTag.GPSProcessingMethod or GpsTag.GPSAreaInformation
                        => Quote(DecodeCharsetPrefixed(raw, le)),
                    _ => null
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
                    return Quote(DecodeCharsetPrefixed(raw, le));

                // Windows XP tags: BYTE array holding UTF-16LE, NOT hex
                case ExifTag.XPTitle:
                case ExifTag.XPComment:
                case ExifTag.XPAuthor:
                case ExifTag.XPKeywords:
                case ExifTag.XPSubject:
                    return Quote(DecodeUtf16Le(raw));

                case ExifTag.ApplicationNotes: // XMP
                    return Quote(DecodeText(raw, 0, raw.Length));

                case ExifTag.MakerNote: return Blob(raw, GuessMakerNote(raw));
                case ExifTag.InterColorProfile: return Blob(raw, "ICC profile");
                case ExifTag.IPTC_NAA: return Blob(raw, "IPTC/NAA");
                case ExifTag.PhotoshopSettings: return Blob(raw, "Photoshop IRB");
                case ExifTag.CFAPattern: return Blob(raw, "CFA pattern");
                case ExifTag.OECF: return Blob(raw, "OECF");
                case ExifTag.SpatialFrequencyResponse: return Blob(raw, "SFR");
                case ExifTag.DeviceSettingDescription: return Blob(raw, "device setting");

                default: return null;
            }
        }

        private static string Blob(byte[] raw, string label)
            => $"{label}, {raw.Length} bytes: {Hex(raw)}";

        private static string DecodeVersion(byte[] raw)
        {
            string s = Encoding.ASCII.GetString(raw).Trim('\0', ' ');
            if (raw.Length >= 4 && IsAllDigits(s, 4))
            {
                int major = int.Parse(s.Substring(0, 2), Inv);
                string minor = s.Substring(2, 2);
                return $"{s} ({major}.{minor})";
            }
            return s.Length > 0 ? Quote(s) : Hex(raw);
        }

        private static bool IsAllDigits(string s, int n)
        {
            if (s.Length < n) return false;
            for (int i = 0; i < n; i++) if (!char.IsDigit(s[i])) return false;
            return true;
        }

        private static string DecodeComponentsConfiguration(byte[] raw)
        {
            string[] names = { "-", "Y", "Cb", "Cr", "R", "G", "B" };
            var parts = new List<string>();
            for (int i = 0; i < raw.Length && i < 4; i++)
                parts.Add(raw[i] < names.Length ? names[raw[i]] : raw[i].ToString(Inv));
            return $"{Hex(raw)}  ({string.Join(",", parts)})";
        }

        /// <summary>UserComment / GPSProcessingMethod: 8-byte charset code + payload.</summary>
        private static string DecodeCharsetPrefixed(byte[] raw, bool le)
        {
            if (raw.Length <= 8) return "";

            string code = Encoding.ASCII.GetString(raw, 0, 8).TrimEnd('\0', ' ');
            int len = raw.Length - 8;

            if (code.StartsWith("UNICODE", StringComparison.OrdinalIgnoreCase))
            {
                var enc = le ? Encoding.Unicode : Encoding.BigEndianUnicode;
                return enc.GetString(raw, 8, len).Trim('\0', ' ');
            }
            if (code.Length == 0)                       // undefined charset -> best effort
                return DecodeText(raw, 8, len).Trim('\0', ' ');

            return DecodeText(raw, 8, len).Trim('\0', ' ');   // ASCII / JIS(best effort)
        }

        private static string DecodeUtf16Le(byte[] raw)
        {
            int len = raw.Length & ~1;
            return Encoding.Unicode.GetString(raw, 0, len).Trim('\0');
        }

        /// <summary>Exif ASCII fields may hold several NUL separated strings.</summary>
        private static string DecodeAsciiMulti(byte[] raw)
        {
            var parts = new List<string>();
            int start = 0;
            for (int i = 0; i <= raw.Length; i++)
            {
                if (i == raw.Length || raw[i] == 0x00)
                {
                    if (i > start)
                    {
                        string s = DecodeText(raw, start, i - start).Trim();
                        if (s.Length > 0) parts.Add(s);
                    }
                    start = i + 1;
                }
            }
            return string.Join(" | ", parts);
        }

        /// <summary>Spec says ASCII, reality says UTF-8 or Latin-1.</summary>
        private static string DecodeText(byte[] raw, int offset, int length)
        {
            try { return StrictUtf8.GetString(raw, offset, length); }
            catch (DecoderFallbackException) { return Encoding.Latin1.GetString(raw, offset, length); }
        }

        public static string ReadAscii(byte[] raw)
        {
            if (raw == null || raw.Length == 0) return "";
            int len = Array.IndexOf(raw, (byte)0x00);
            if (len < 0) len = raw.Length;
            return Encoding.ASCII.GetString(raw, 0, len).Trim();
        }

        public static double[] ReadRationals(byte[] raw, bool le)
        {
            int n = raw.Length / 8;
            var result = new double[n];
            for (int i = 0; i < n; i++)
            {
                uint num = U32(raw, i * 8, le);
                uint den = U32(raw, i * 8 + 4, le);
                result[i] = den == 0 ? 0d : (double)num / den;
            }
            return result;
        }

        public static double? ToDegrees(double[]? dms, string? reference)
        {
            if (dms == null || dms.Length < 3) return null;
            double deg = dms[0] + dms[1] / 60d + dms[2] / 3600d;
            if (double.IsNaN(deg) || double.IsInfinity(deg)) return null;
            if (!string.IsNullOrEmpty(reference))
            {
                char c = char.ToUpperInvariant(reference![0]);
                if (c == 'S' || c == 'W') deg = -deg;
            }
            return deg;
        }

        // =====================================================
        // human readable annotations
        // =====================================================
        private static string? Describe(ExifIfdKind kind, ushort tag, Num[] v)
        {
            if (v.Length == 0) return null;

            double d0 = v[0].Value;
            int i0 = double.IsNaN(d0) || double.IsInfinity(d0) ? int.MinValue : (int)Math.Round(d0);

            if (kind == ExifIfdKind.Gps)
            {
                switch ((GpsTag)tag)
                {
                    case GpsTag.GPSVersionID: return JoinInts(v, ".");
                    case GpsTag.GPSLatitude:
                    case GpsTag.GPSLongitude:
                    case GpsTag.GPSDestLatitude:
                    case GpsTag.GPSDestLongitude: return FormatDms(v);
                    case GpsTag.GPSAltitude: return $"{d0.ToString("0.###", Inv)} m";
                    case GpsTag.GPSAltitudeRef: return Lookup(GpsAltitudeRef, i0);
                    case GpsTag.GPSDifferential: return Lookup(GpsDifferential, i0);
                    case GpsTag.GPSTimeStamp:
                        return v.Length >= 3
                            ? $"{(int)v[0].Value:00}:{(int)v[1].Value:00}:{v[2].Value.ToString("00.###", Inv)} UTC"
                            : null;
                    case GpsTag.GPSImgDirection:
                    case GpsTag.GPSTrack:
                    case GpsTag.GPSDestBearing: return $"{d0.ToString("0.##", Inv)}°";
                    case GpsTag.GPSHPositioningError: return $"{d0.ToString("0.##", Inv)} m";
                    default: return null;
                }
            }

            if (kind == ExifIfdKind.Interop) return null;

            switch ((ExifTag)tag)
            {
                // ---- enumerations ----
                case ExifTag.Compression: return Lookup(Compression, i0);
                case ExifTag.PhotometricInterpretation: return Lookup(Photometric, i0);
                case ExifTag.Orientation: return Lookup(Orientation, i0);
                case ExifTag.PlanarConfiguration: return Lookup(Planar, i0);
                case ExifTag.ResolutionUnit:
                case ExifTag.FocalPlaneResolutionUnit: return Lookup(ResolutionUnit, i0);
                case ExifTag.YCbCrPositioning: return Lookup(YCbCrPositioning, i0);
                case ExifTag.ExposureProgram: return Lookup(ExposureProgram, i0);
                case ExifTag.MeteringMode: return Lookup(MeteringMode, i0);
                case ExifTag.LightSource: return Lookup(LightSource, i0);
                case ExifTag.Flash: return DescribeFlash(i0);
                case ExifTag.ColorSpace: return Lookup(ColorSpace, i0);
                case ExifTag.SensingMethod: return Lookup(SensingMethod, i0);
                case ExifTag.FileSource: return Lookup(FileSource, i0);
                case ExifTag.SceneType: return i0 == 1 ? "directly photographed" : $"unknown ({i0})";
                case ExifTag.CustomRendered: return Lookup(CustomRendered, i0);
                case ExifTag.ExposureMode: return Lookup(ExposureMode, i0);
                case ExifTag.WhiteBalance: return Lookup(WhiteBalance, i0);
                case ExifTag.SceneCaptureType: return Lookup(SceneCaptureType, i0);
                case ExifTag.GainControl: return Lookup(GainControl, i0);
                case ExifTag.Contrast:
                case ExifTag.Saturation:
                case ExifTag.Sharpness: return Lookup(NormalSoftHard, i0);
                case ExifTag.SubjectDistanceRange: return Lookup(SubjectDistanceRange, i0);
                case ExifTag.SensitivityType: return Lookup(SensitivityType, i0);

                // ---- physical units / APEX ----
                case ExifTag.ExposureTime: return FormatShutter(d0);
                case ExifTag.ShutterSpeedValue:
                {
                    string? s = FormatShutter(Math.Pow(2, -d0));
                    return s == null ? "APEX Tv" : $"{s}, APEX Tv";
                }
                case ExifTag.FNumber: return "f/" + d0.ToString("0.##", Inv);
                case ExifTag.ApertureValue:
                case ExifTag.MaxApertureValue:
                    return "f/" + Math.Pow(2, d0 / 2).ToString("0.##", Inv) + ", APEX Av";
                case ExifTag.BrightnessValue: return d0.ToString("0.##", Inv) + " EV (APEX Bv)";
                case ExifTag.ExposureBiasValue:
                    return (d0 >= 0 ? "+" : "") + d0.ToString("0.##", Inv) + " EV";
                case ExifTag.FocalLength: return d0.ToString("0.##", Inv) + " mm";
                case ExifTag.FocalLengthIn35mmFormat: return d0.ToString("0.##", Inv) + " mm (35 mm equiv.)";
                case ExifTag.SubjectDistance:
                    return d0 <= 0 ? "unknown / infinity" : d0.ToString("0.###", Inv) + " m";
                case ExifTag.DigitalZoomRatio:
                    return d0 == 0 ? "not used" : d0.ToString("0.##", Inv) + "x";
                case ExifTag.LensSpecification: return DescribeLensSpec(v);
                case ExifTag.XResolution:
                case ExifTag.YResolution: return d0.ToString("0.##", Inv) + " px/unit";

                default: return null;
            }
        }

        private static string? FormatShutter(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return null;
            return seconds >= 1
                ? seconds.ToString("0.##", Inv) + " s"
                : $"1/{Math.Round(1 / seconds).ToString("0", Inv)} s";
        }

        private static string DescribeFlash(int v)
        {
            if (v == int.MinValue) return "invalid";
            var parts = new List<string> { (v & 0x01) != 0 ? "fired" : "did not fire" };

            switch ((v >> 1) & 0x03)
            {
                case 2: parts.Add("no strobe return"); break;
                case 3: parts.Add("strobe return detected"); break;
            }
            switch ((v >> 3) & 0x03)
            {
                case 1: parts.Add("compulsory firing"); break;
                case 2: parts.Add("compulsory suppression"); break;
                case 3: parts.Add("auto mode"); break;
            }
            if ((v & 0x20) != 0) parts.Add("no flash function");
            if ((v & 0x40) != 0) parts.Add("red-eye reduction");
            return string.Join(", ", parts);
        }

        private static string? DescribeLensSpec(Num[] v)
        {
            if (v.Length < 4) return null;
            string fl = Math.Abs(v[0].Value - v[1].Value) < 0.01
                ? $"{v[0].Value.ToString("0.##", Inv)}mm"
                : $"{v[0].Value.ToString("0.##", Inv)}-{v[1].Value.ToString("0.##", Inv)}mm";
            string ap = Math.Abs(v[2].Value - v[3].Value) < 0.01
                ? $"f/{v[2].Value.ToString("0.##", Inv)}"
                : $"f/{v[2].Value.ToString("0.##", Inv)}-{v[3].Value.ToString("0.##", Inv)}";
            return $"{fl} {ap}";
        }

        private static string? FormatDms(Num[] v)
        {
            if (v.Length < 3) return null;
            return $"{v[0].Value.ToString("0.####", Inv)}° " +
                   $"{v[1].Value.ToString("0.####", Inv)}' " +
                   $"{v[2].Value.ToString("0.####", Inv)}\"";
        }

        private static string JoinInts(Num[] v, string sep)
        {
            var parts = new List<string>(v.Length);
            foreach (var n in v) parts.Add(((int)n.Value).ToString(Inv));
            return string.Join(sep, parts);
        }

        private static string GuessMakerNote(byte[] raw)
        {
            string head = Encoding.ASCII.GetString(raw, 0, Math.Min(raw.Length, 12));
            string[] sigs = { "Nikon", "OLYMP", "FUJIFILM", "SONY", "Apple iOS", "Panasonic",
                              "Ricoh", "SIGMA", "SAMSUNG", "PENTAX", "LEICA", "CASIO" };
            foreach (string s in sigs)
                if (head.StartsWith(s, StringComparison.OrdinalIgnoreCase))
                    return $"MakerNote [{s}] (not parsed)";
            return "MakerNote [vendor unknown] (not parsed)";
        }

        // =====================================================
        // lookup tables
        // =====================================================
        private static string Lookup(Dictionary<int, string> map, int value)
            => map.TryGetValue(value, out string? s) ? s : $"unknown ({value})";

        private static readonly Dictionary<int, string> Compression = new()
        {
            [1] = "uncompressed", [2] = "CCITT 1D", [3] = "T4/G3", [4] = "T6/G4",
            [5] = "LZW", [6] = "JPEG (old style / thumbnail)", [7] = "JPEG",
            [8] = "Adobe Deflate", [32773] = "PackBits", [34712] = "JPEG2000"
        };

        private static readonly Dictionary<int, string> Photometric = new()
        {
            [0] = "WhiteIsZero", [1] = "BlackIsZero", [2] = "RGB", [3] = "palette",
            [4] = "transparency mask", [5] = "CMYK", [6] = "YCbCr", [8] = "CIELab",
            [32803] = "CFA (raw)", [34892] = "LinearRaw"
        };

        private static readonly Dictionary<int, string> Orientation = new()
        {
            [1] = "0° (normal)", [2] = "mirror horizontal", [3] = "rotate 180°",
            [4] = "mirror vertical", [5] = "mirror horizontal + rotate 270° CW",
            [6] = "rotate 90° CW", [7] = "mirror horizontal + rotate 90° CW",
            [8] = "rotate 270° CW"
        };

        private static readonly Dictionary<int, string> Planar = new()
        {
            [1] = "chunky", [2] = "planar"
        };

        private static readonly Dictionary<int, string> ResolutionUnit = new()
        {
            [1] = "none", [2] = "inch", [3] = "cm"
        };

        private static readonly Dictionary<int, string> YCbCrPositioning = new()
        {
            [1] = "centered", [2] = "co-sited"
        };

        private static readonly Dictionary<int, string> ColorSpace = new()
        {
            [1] = "sRGB", [2] = "Adobe RGB", [0xFFFD] = "Wide Gamut RGB",
            [0xFFFE] = "ICC profile", [0xFFFF] = "uncalibrated"
        };

        private static readonly Dictionary<int, string> ExposureProgram = new()
        {
            [0] = "not defined", [1] = "manual", [2] = "program AE",
            [3] = "aperture priority", [4] = "shutter priority", [5] = "creative (slow)",
            [6] = "action (fast)", [7] = "portrait", [8] = "landscape", [9] = "bulb"
        };

        private static readonly Dictionary<int, string> MeteringMode = new()
        {
            [0] = "unknown", [1] = "average", [2] = "center weighted average",
            [3] = "spot", [4] = "multi-spot", [5] = "multi-segment / pattern",
            [6] = "partial", [255] = "other"
        };

        private static readonly Dictionary<int, string> LightSource = new()
        {
            [0] = "unknown", [1] = "daylight", [2] = "fluorescent", [3] = "tungsten",
            [4] = "flash", [9] = "fine weather", [10] = "cloudy", [11] = "shade",
            [12] = "daylight fluorescent", [13] = "day white fluorescent",
            [14] = "cool white fluorescent", [15] = "white fluorescent",
            [17] = "standard light A", [18] = "standard light B", [19] = "standard light C",
            [20] = "D55", [21] = "D65", [22] = "D75", [23] = "D50",
            [24] = "ISO studio tungsten", [255] = "other"
        };

        private static readonly Dictionary<int, string> SensingMethod = new()
        {
            [1] = "not defined", [2] = "one-chip color area", [3] = "two-chip color area",
            [4] = "three-chip color area", [5] = "color sequential area",
            [7] = "trilinear", [8] = "color sequential linear"
        };

        private static readonly Dictionary<int, string> FileSource = new()
        {
            [1] = "film scanner", [2] = "reflection print scanner", [3] = "digital camera"
        };

        private static readonly Dictionary<int, string> CustomRendered = new()
        {
            [0] = "normal", [1] = "custom", [2] = "HDR (no original)",
            [3] = "HDR (with original)", [6] = "panorama", [8] = "portrait HDR"
        };

        private static readonly Dictionary<int, string> ExposureMode = new()
        {
            [0] = "auto", [1] = "manual", [2] = "auto bracket"
        };

        private static readonly Dictionary<int, string> WhiteBalance = new()
        {
            [0] = "auto", [1] = "manual"
        };

        private static readonly Dictionary<int, string> SceneCaptureType = new()
        {
            [0] = "standard", [1] = "landscape", [2] = "portrait", [3] = "night scene"
        };

        private static readonly Dictionary<int, string> GainControl = new()
        {
            [0] = "none", [1] = "low gain up", [2] = "high gain up",
            [3] = "low gain down", [4] = "high gain down"
        };

        private static readonly Dictionary<int, string> NormalSoftHard = new()
        {
            [0] = "normal", [1] = "low / soft", [2] = "high / hard"
        };

        private static readonly Dictionary<int, string> SubjectDistanceRange = new()
        {
            [0] = "unknown", [1] = "macro", [2] = "close", [3] = "distant"
        };

        private static readonly Dictionary<int, string> SensitivityType = new()
        {
            [0] = "unknown", [1] = "SOS", [2] = "REI", [3] = "ISO speed",
            [4] = "SOS+REI", [5] = "SOS+ISO", [6] = "REI+ISO", [7] = "SOS+REI+ISO"
        };

        private static readonly Dictionary<int, string> GpsAltitudeRef = new()
        {
            [0] = "above sea level", [1] = "below sea level",
            [2] = "positive (WGS-84 ellipsoid)", [3] = "negative (WGS-84 ellipsoid)"
        };

        private static readonly Dictionary<int, string> GpsDifferential = new()
        {
            [0] = "no correction", [1] = "differential corrected"
        };

        // =====================================================
        // output helpers
        // =====================================================
        private static string Hex(byte[] raw)
        {
            if (raw.Length <= MaxHexBytes) return Convert.ToHexString(raw);
            return Convert.ToHexString(raw, 0, MaxHexBytes) + $"… (+{raw.Length - MaxHexBytes} bytes)";
        }

        private static string Quote(string s) => "\"" + Escape(s) + "\"";

        private static string Escape(string s)
        {
            var sb = new StringBuilder(Math.Min(s.Length, MaxTextChars) + 8);
            foreach (char c in s)
            {
                if (sb.Length >= MaxTextChars) { sb.Append('…'); break; }
                switch (c)
                {
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '"': sb.Append("\\\""); break;
                    default:
                        if (char.IsControl(c)) sb.Append($"\\x{(int)c:X2}");
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // =====================================================
        // primitive readers
        // =====================================================
        private static ushort U16(byte[] d, int o, bool le) => le
            ? (ushort)(d[o] | (d[o + 1] << 8))
            : (ushort)((d[o] << 8) | d[o + 1]);

        private static uint U32(byte[] d, int o, bool le) => le
            ? (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24))
            : (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);

        private static ulong U64(byte[] d, int o, bool le)
        {
            ulong lo = U32(d, le ? o : o + 4, le);
            ulong hi = U32(d, le ? o + 4 : o, le);
            return (hi << 32) | lo;
        }
    }
}