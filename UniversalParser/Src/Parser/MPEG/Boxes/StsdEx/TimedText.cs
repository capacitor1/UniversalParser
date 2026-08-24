using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    /// <summary>
    /// Timed text / subtitle sample entries: tx3g (3GPP TS 26.245), wvtt and stpp
    /// (ISO/IEC 14496-30), plus the QuickTime 'text' variant.
    /// </summary>
    internal static class TimedText
    {
        // =====================================================================
        // tx3g : TextSampleEntry (30 fixed bytes + FontTableBox)
        // =====================================================================
        public static void ParseTx3g(ref Cur c, LineWriter w)
        {
            uint displayFlags = c.U32();
            sbyte hj = (sbyte)c.U8();
            sbyte vj = (sbyte)c.U8();
            string bgColor = ReadRgba(ref c);

            w.Add("display_flags", DescribeDisplayFlags(displayFlags));
            w.Add("horizontal_justification", JustificationName(hj, true));
            w.Add("vertical_justification", JustificationName(vj, false));
            w.Add("background_color_rgba", bgColor);

            // BoxRecord default-text-box
            short top = c.S16(), left = c.S16(), bottom = c.S16(), right = c.S16();
            w.Add("default_text_box", $"top={top}, left={left}, bottom={bottom}, right={right} "
                                    + $"({right - left} x {bottom - top})");

            // StyleRecord default-style
            using (w.Push("default_style"))
            {
                w.Add("start_char", c.U16());
                w.Add("end_char", c.U16());
                w.Add("font_id", c.U16());
                w.Add("face_style_flags", DescribeFaceStyle(c.U8()));
                w.Add("font_size", $"{c.U8()} px");
                w.Add("text_color_rgba", ReadRgba(ref c));
            }

            if (c.Bad) w.Add("parse", "tx3g sample entry truncated");
        }

        // =====================================================================
        // text : QuickTime Text Sample Description (43 bytes + pascal font name)
        // =====================================================================
        public static void ParseQtText(ref Cur c, LineWriter w)
        {
            uint displayFlags = c.U32();
            int justification = (int)c.U32();

            w.Add("display_flags", $"0x{displayFlags:X8}");
            w.Add("text_justification", JustificationName((sbyte)justification, true));
            w.Add("background_color", ReadRgb48(ref c));

            short top = c.S16(), left = c.S16(), bottom = c.S16(), right = c.S16();
            w.Add("default_text_box", $"top={top}, left={left}, bottom={bottom}, right={right}");

            c.Skip(8);                                  // reserved
            w.Add("font_number", c.U16());
            w.Add("font_face", DescribeFaceStyle((byte)c.U16()));
            c.Skip(1);                                  // reserved
            c.Skip(2);                                  // reserved
            w.Add("foreground_color", ReadRgb48(ref c));

            if (c.Left >= 1)
            {
                int len = c.U8();
                string name = c.Ascii(Math.Min(len, c.Left));
                w.Add("font_name", name.Length == 0 ? "(empty)" : name);
            }

            if (c.Bad) w.Add("parse", "QuickTime text sample entry truncated");
        }

        // =====================================================================
        // stpp : XMLSubtitleSampleEntry (TTML), three null-terminated strings
        // =====================================================================
        public static void ParseStpp(ref Cur c, LineWriter w)
        {
            w.Add("namespace", ReadCString(ref c));
            w.Add("schema_location", ReadCString(ref c));
            w.Add("auxiliary_mime_types", ReadCString(ref c));
        }

        // =====================================================================
        // child boxes
        // =====================================================================

        /// <summary>ftab : FontTableBox</summary>
        public static void ParseFtab(ReadOnlySpan<byte> d, LineWriter w)
        {
            var c = new Cur(d);
            int count = c.U16();
            w.Add("entry_count", count);

            for (int i = 0; i < count && !c.Bad; i++)
            {
                int id = c.U16();
                int len = c.U8();
                string name = c.Ascii(Math.Min(len, c.Left));
                if (c.Bad) { w.Add("parse", $"font entry {i} truncated"); return; }
                w.Add($"font[{i}]", $"id={id}, name=\"{name}\"");
            }
        }

        /// <summary>vttC : WebVTTConfigurationBox - the WebVTT file header lines.</summary>
        public static void ParseVttC(ReadOnlySpan<byte> d, LineWriter w)
        {
            string text = Utf8(d).TrimEnd('\0');
            w.Add("length", d.Length);

            if (text.Length == 0) { w.Add("config", "(empty)"); return; }

            if (!text.StartsWith("WEBVTT", StringComparison.Ordinal) &&
                !text.StartsWith("\uFEFFWEBVTT", StringComparison.Ordinal))
                w.Add("note", "config should start with the 'WEBVTT' signature");

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i >= 32) { w.Add("config[...]", $"{lines.Length - i} more lines"); break; }
                w.Add($"config[{i}]", lines[i].Length == 0 ? "(blank)" : lines[i]);
            }
        }

        /// <summary>vlab : WebVTTSourceLabelBox</summary>
        public static void ParseVlab(ReadOnlySpan<byte> d, LineWriter w)
            => w.Add("source_label", Utf8(d).TrimEnd('\0'));

        /// <summary>txtC : TextConfigBox (ISO/IEC 14496-30, used by 'stxt')</summary>
        public static void ParseTxtC(ReadOnlySpan<byte> d, LineWriter w)
        {
            var c = new Cur(d);
            c.Skip(4);                                  // version + flags
            var rest = c.D.Slice(Math.Min(c.P, c.D.Length));
            w.Add("text_config", Utf8(rest).TrimEnd('\0'));
        }

        // =====================================================================
        // helpers
        // =====================================================================

        // 3GPP TS 26.245 / ISO 14496-12 displayFlags
        private static readonly (uint Mask, string Name)[] DisplayFlagBits =
        {
            (0x00000020, "scroll-in"),
            (0x00000040, "scroll-out"),
            (0x00000800, "continuous-karaoke"),
            (0x00020000, "write-text-vertically"),
            (0x00040000, "fill-text-region"),
        };

        private const uint ScrollDirectionMask = 0x00000180;

        private static string DescribeDisplayFlags(uint f)
        {
            if (f == 0) return "0x00000000 (none)";

            var parts = new List<string>();
            uint known = ScrollDirectionMask;

            foreach (var (mask, name) in DisplayFlagBits)
            {
                known |= mask;
                if ((f & mask) != 0) parts.Add(name);
            }

            if ((f & 0x00000060) != 0)              // scroll direction is only meaningful when scrolling
            {
                parts.Add(((f & ScrollDirectionMask) >> 7) switch
                {
                    0 => "scroll-credits (bottom to top)",
                    1 => "scroll-marquee (right to left)",
                    2 => "scroll-down (top to bottom)",
                    _ => "scroll-right (left to right)"
                });
            }

            uint unknown = f & ~known;
            if (unknown != 0) parts.Add($"unknown bits 0x{unknown:X8}");

            return $"0x{f:X8} ({string.Join(", ", parts)})";
        }

        private static string DescribeFaceStyle(byte face)
        {
            if (face == 0) return "0 (plain)";
            var parts = new List<string>(3);
            if ((face & 0x01) != 0) parts.Add("bold");
            if ((face & 0x02) != 0) parts.Add("italic");
            if ((face & 0x04) != 0) parts.Add("underline");
            uint rest = (uint)(face & ~0x07);
            if (rest != 0) parts.Add($"unknown 0x{rest:X2}");
            return $"0x{face:X2} ({string.Join(", ", parts)})";
        }

        private static string JustificationName(sbyte v, bool horizontal) => v switch
        {
            0 => horizontal ? "0 (left)" : "0 (top)",
            1 => "1 (center)",
            -1 => horizontal ? "-1 (right)" : "-1 (bottom)",
            _ => $"{v} (undefined)"
        };

        private static string ReadRgba(ref Cur c)
        {
            byte r = c.U8(), g = c.U8(), b = c.U8(), a = c.U8();
            string alpha = a switch { 0 => " transparent", 255 => " opaque", _ => $" alpha={a}" };
            return $"#{r:X2}{g:X2}{b:X2}{a:X2} (rgb {r},{g},{b};{alpha})";
        }

        private static string ReadRgb48(ref Cur c)
        {
            ushort r = c.U16(), g = c.U16(), b = c.U16();
            return $"#{r >> 8:X2}{g >> 8:X2}{b >> 8:X2} (16-bit {r},{g},{b})";
        }

        private static string ReadCString(ref Cur c)
        {
            int start = c.P;
            while (c.P < c.D.Length && c.D[c.P] != 0) c.P++;
            var span = c.D.Slice(start, c.P - start);
            if (c.P < c.D.Length) c.P++;                // consume the NUL
            return span.IsEmpty ? "(empty)" : Utf8(span);
        }

        private static string Utf8(ReadOnlySpan<byte> b)
        {
            if (b.IsEmpty) return "";
            try { return Encoding.UTF8.GetString(b); }
            catch { return Helper.Hex(b, 24); }
        }
    }
}