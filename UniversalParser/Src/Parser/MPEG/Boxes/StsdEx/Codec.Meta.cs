using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    internal static partial class Codec
    {
        // =====================================================================
        // 'keys' : Metadata Key Table Atom, as found inside a 'mebx' sample entry.
        // WARNING: this is NOT the versioned 'keys' atom found inside a 'meta' atom.
        //          Here there is no version/flags/entry_count -- just an array of
        //          metadata key atoms whose "type" field is a 32-bit local_key_id.
        // =====================================================================
        public static void ParseMebxKeyTable(ReadOnlySpan<byte> d, LineWriter w)
        {
            var c = new Cur(d);
            int n = 0;

            while (c.Left >= 8)
            {
                int start = c.P;
                long size = c.U32();
                uint localKeyId = c.U32();          // <-- id, not a FourCC
                int hdr = 8;

                if (size == 1) { size = (long)c.U64(); hdr = 16; }
                else if (size == 0) size = d.Length - start;

                if (size < hdr || start + size > d.Length)
                {
                    w.Add($"key[{n}]", $"invalid size {size} at +{start} -> stop");
                    return;
                }

                var body = d.Slice(start + hdr, (int)size - hdr);

                using (w.Push($"key[{n}]"))
                {
                    w.Add("local_key_id", Helper.Id32(localKeyId));
                    if (localKeyId == 0)
                        w.Add("status", "reserved id 0 -> unused slot, must be ignored");
                    else if (localKeyId == 0xFFFFFFFF)
                        w.Add("status", "reserved id 0xFFFFFFFF -> must not appear in a key atom");

                    ParseMetadataKeyAtom(body, w);
                }

                c.P = start + (int)size;
                n++;
            }

            w.Add("key_count", n);
            if (n == 0) w.Add("warning", "'keys' contains no metadata key atom");
            if (c.Left > 0) w.Add("trailing_bytes", $"{c.Left}  {Helper.Hex(d.Slice(c.P), 16)}");
        }

        /// <summary>Children of a metadata key atom: 'keyd' (required), 'dtyp', 'loca'.</summary>
        private static void ParseMetadataKeyAtom(ReadOnlySpan<byte> d, LineWriter w)
        {
            var c = new Cur(d);
            bool hasKeyd = false;

            while (c.Left >= 8)
            {
                int start = c.P;
                long size = c.U32();
                string t = c.FourCC();
                int hdr = 8;

                if (size == 1) { size = (long)c.U64(); hdr = 16; }
                else if (size == 0) size = d.Length - start;

                if (size < hdr || start + size > d.Length)
                {
                    w.Add($"child'{t}'", $"invalid size {size} -> stop");
                    return;
                }

                var p = d.Slice(start + hdr, (int)size - hdr);

                switch (t)
                {
                    case "keyd": hasKeyd = true; ParseKeyd(p, w); break;
                    case "dtyp": ParseDtyp(p, w); break;
                    case "loca": w.Add("locale", new Cur(p).CStringUtf8()); break;
                    default:
                        w.Add($"extra'{t}'", $"{p.Length} bytes  {Helper.Hex(p, 24)}");
                        break;
                }

                c.P = start + (int)size;
            }

            if (!hasKeyd) w.Add("warning", "missing 'keyd' (required by QTFF)");
        }

        // ---------------- 'keyd' : MetadataKeyDeclarationBox ----------------
        private static void ParseKeyd(ReadOnlySpan<byte> d, LineWriter w)
        {
            if (d.Length < 4) { w.Add("keyd", "truncated"); return; }

            var c = new Cur(d);
            string ns = c.FourCC();
            var val = d.Slice(4);

            w.Add("key_namespace", $"{ns}  ({KeyNamespaceName(ns)})");

            switch (ns)
            {
                case "mdta":        // reverse-DNS UTF-8 string, e.g. com.apple.quicktime.detected-face
                case "uiso":
                {
                    string s = Encoding.UTF8.GetString(val).TrimEnd('\0');
                    w.Add("key_value", s.Length == 0 ? "(empty)" : s);
                    string meaning = FriendlyMdtaKey(s);
                    if (meaning != null) w.Add("meaning", meaning);
                    break;
                }

                case "udta":        // 4-byte QuickTime user-data type, may start with 0xA9 ('©')
                case "me4c":        // metadata sample entry FourCC
                    if (val.Length >= 4)
                    {
                        uint v = ((uint)val[0] << 24) | ((uint)val[1] << 16) | ((uint)val[2] << 8) | val[3];
                        w.Add("key_value", Helper.Id32(v));
                    }
                    else w.Add("key_value", Helper.Hex(val, 8));
                    break;

                default:
                    w.Add("key_value", $"{val.Length} bytes  {Helper.Hex(val, 24)}");
                    w.Add("note", "unregistered key namespace; interpretation unknown");
                    break;
            }
        }

        // ---------------- 'dtyp' : MetadataDatatypeDefinitionBox ----------------
        private static void ParseDtyp(ReadOnlySpan<byte> d, LineWriter w)
        {
            if (d.Length < 4) { w.Add("dtyp", "truncated"); return; }

            var c = new Cur(d);
            uint ns = c.U32();
            var arr = d.Slice(4);

            switch (ns)
            {
                case 0:     // datatype_array = BE uint32 well-known type
                {
                    var b = new Cur(arr);
                    uint wk = b.U32();
                    w.Add("datatype", $"{WellKnownTypeName(wk)}  (well-known {wk})");
                    if (arr.Length != 4)
                        w.Add("note", $"datatype_array should be 4 bytes, got {arr.Length}");
                    break;
                }
                case 1:     // reverse-DNS UTF-8 string, no NUL terminator
                    w.Add("datatype", $"{Encoding.UTF8.GetString(arr).TrimEnd('\0')}  (extended, reverse-DNS)");
                    break;
                default:
                    w.Add("datatype_namespace", ns);
                    w.Add("datatype_array", Helper.Hex(arr, 24));
                    w.Add("note", "datatype namespace not 0/1 -> values with this type should be ignored");
                    break;
            }
        }

        // ---------------- QuickTime timecode sample description ----------------
        public static void ParseTimecodeBody(ref Cur c, LineWriter w, bool is64)
        {
            c.Skip(4);                              // reserved, must be 0
            uint flags = c.U32();
            uint timeScale = c.U32();
            uint frameDuration = c.U32();
            byte numberOfFrames = c.U8();
            c.Skip(1);                              // reserved, must be 0

            var names = new List<string>();
            if ((flags & 0x0001) != 0) names.Add("drop-frame");
            if ((flags & 0x0002) != 0) names.Add("24-hour-max");
            if ((flags & 0x0004) != 0) names.Add("negative-times-ok");
            if ((flags & 0x0008) != 0) names.Add("counter");

            w.Add("timecode_flags", $"0x{flags:X8}" + (names.Count > 0 ? $"  ({string.Join(", ", names)})" : ""));
            w.Add("time_scale", timeScale);
            w.Add("frame_duration", frameDuration);
            w.Add("number_of_frames", numberOfFrames);
            if (frameDuration > 0)
                w.Add("nominal_frame_rate", $"{(double)timeScale / frameDuration:0.###} fps");
            if (is64) w.Add("note", "'tc64' -> sample payload is a 64-bit frame counter");
        }

        // ---------------- lookup tables ----------------
        internal static string KeyNamespaceName(string ns) => ns switch
        {
            "mdta" => "reverse-DNS naming convention (QuickTime)",
            "me4c" => "metadata sample entry four-character-code (ISO)",
            "udta" => "QuickTime user data",
            "uiso" => "ISO user data",
            _      => "unregistered"
        };

        /// <summary>Apple "well-known types" (QTFF Appendix D). Also used by 'data' atoms in 'ilst'.</summary>
        internal static string WellKnownTypeName(uint t) => t switch
        {
            0  => "reserved / raw binary",
            1  => "UTF-8",
            2  => "UTF-16BE",
            3  => "S/JIS (deprecated)",
            4  => "UTF-8 sort",
            5  => "UTF-16 sort",
            13 => "JPEG (JFIF)",
            14 => "PNG",
            21 => "BE signed integer (1-4 bytes, size-determined)",
            22 => "BE unsigned integer (1-4 bytes, size-determined)",
            23 => "BE float32 (IEEE754)",
            24 => "BE float64 (IEEE754)",
            27 => "BMP",
            28 => "QuickTime Metadata atom",
            65 => "int8",
            66 => "BE int16",
            67 => "BE int32",
            70 => "BE PointF32 { float x, y }",
            71 => "BE DimensionsF32 { float width, height }",
            72 => "BE RectF32 { float x, y, width, height }",
            74 => "BE int64",
            75 => "uint8",
            76 => "BE uint16",
            77 => "BE uint32",
            78 => "BE uint64",
            79 => "BE AffineTransformF64 { double m[3][3] }",
            _  => "unknown / unregistered"
        };

        /// <summary>Cosmetic only. Extend freely as you encounter keys in the wild.</summary>
        private static readonly Dictionary<string, string> MdtaKeyNotes = new(StringComparer.Ordinal)
        {
            ["com.apple.quicktime.detected-face"]        = "face detection result (bounds / roll / yaw)",
            ["com.apple.quicktime.video-orientation"]    = "per-sample video orientation (EXIF-style 1..8)",
            ["com.apple.quicktime.still-image-time"]      = "Live Photo key-frame marker",
            ["com.apple.quicktime.location.ISO6709"]      = "ISO 6709 geographic position",
            ["com.apple.quicktime.location.accuracy.horizontal"] = "horizontal position accuracy (m)",
        };

        private static string FriendlyMdtaKey(string key)
            => MdtaKeyNotes.TryGetValue(key, out var s) ? s : string.Empty;
    }
}