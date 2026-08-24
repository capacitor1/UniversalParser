using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    /// <summary>Bounds-safe forward cursor over a byte span. Never throws; sets Bad on overrun.</summary>
    internal ref struct Cur(ReadOnlySpan<byte> data, int pos = 0)
    {
        public ReadOnlySpan<byte> D = data;
        public int P = pos;
        public bool Bad = false;

        public int Left => Math.Max(0, D.Length - P);
        public bool Eof => P >= D.Length;

        public bool Need(int n)
        {
            if (n < 0 || (long)P + n > D.Length) { Bad = true; return false; }
            return true;
        }

        public byte   U8()  => Need(1) ? D[P++] : (byte)0;
        public ushort U16() { if (!Need(2)) return 0; ushort v = (ushort)((D[P] << 8) | D[P + 1]); P += 2; return v; }
        public uint   U24() { if (!Need(3)) return 0; uint v = ((uint)D[P] << 16) | ((uint)D[P + 1] << 8) | D[P + 2]; P += 3; return v; }
        public uint   U32() { if (!Need(4)) return 0; uint v = ((uint)D[P] << 24) | ((uint)D[P + 1] << 16) | ((uint)D[P + 2] << 8) | D[P + 3]; P += 4; return v; }
        public ulong  U48() { if (!Need(6)) return 0; ulong v = 0; for (int i = 0; i < 6; i++) v = (v << 8) | D[P + i]; P += 6; return v; }
        public ulong  U64() { if (!Need(8)) return 0; ulong v = 0; for (int i = 0; i < 8; i++) v = (v << 8) | D[P + i]; P += 8; return v; }

        public short S16() => unchecked((short)U16());

        public void Skip(int n) { if (Need(n)) P += n; }

        public ReadOnlySpan<byte> Bytes(int n)
        {
            if (!Need(n)) return default;
            var s = D.Slice(P, n);
            P += n;
            return s;
        }

        public string FourCC()
        {
            if (!Need(4)) return "????";
            var s = Helper.Ascii(D.Slice(P, 4));
            P += 4;
            return s;
        }
        /// <summary>Reads n bytes as ASCII. Stops at the first NUL (fields are NUL-padded),
        /// maps non-printable bytes to '.', and trims trailing whitespace.</summary>
        public string Ascii(int n)
        {
            var s = Bytes(n);
            if (s.IsEmpty) return string.Empty;

            int end = s.Length;
            for (int i = 0; i < s.Length; i++)
                if (s[i] == 0) { end = i; break; }

            var sb = new StringBuilder(end);
            for (int i = 0; i < end; i++)
                sb.Append(s[i] >= 0x20 && s[i] < 0x7F ? (char)s[i] : '.');

            return sb.ToString().TrimEnd();
        }

        /// <summary>ISO 14496-12 compressorname: 1 length byte + 31 chars. Some muxers write a plain C string.</summary>
        public string Pascal32()
        {
            if (!Need(32)) return string.Empty;
            var raw = D.Slice(P, 32);
            P += 32;
            byte len = raw[0];
            if (len <= 31 && len != 0 && raw[0] < 0x20)      // proper pascal string
                return Helper.Ascii(raw.Slice(1, len)).TrimEnd('\0');
            if (len == 0) return string.Empty;
            return Helper.Ascii(raw).TrimEnd('\0').Trim();   // fallback: C string
        }

        /// <summary>NUL-terminated UTF-8 string (ISO 14496-12 utf8string).</summary>
        public string CStringUtf8()
        {
            int start = P;
            while (P < D.Length && D[P] != 0) P++;
            string s = start == P ? "" : Encoding.UTF8.GetString(D.Slice(start, P - start));
            if (P < D.Length) P++;              // consume the NUL
            return s;
        }

        public string Utf8(int n)
        {
            var s = Bytes(n);
            return s.IsEmpty ? "" : Encoding.UTF8.GetString(s).TrimEnd('\0');
        }

        /// <summary>Heuristic: does the data at the current position look like a box header?</summary>
        public bool LooksLikeBox()
        {
            if (Left < 8) return false;
            uint sz = ((uint)D[P] << 24) | ((uint)D[P + 1] << 16) | ((uint)D[P + 2] << 8) | D[P + 3];
            if (sz != 0 && sz != 1 && (sz < 8 || sz > (uint)Left)) return false;
            for (int i = 4; i < 8; i++)
                if (!(D[P + i] >= 0x20 && D[P + i] < 0x7F)) return false;
            return true;
        }
    }

    /// <summary>Writes key/value lines with a hierarchical prefix, e.g. "entry[0].avcC.sps.coded_size".</summary>
    internal sealed class LineWriter(List<(string K, string V)> lines)
    {
        public string Prefix = string.Empty;

        public void Add(string key, string value)
            => lines.Add((Prefix.Length == 0 ? key : Prefix + "." + key, value ?? string.Empty));

        public void Add(string key, long value) => Add(key, value.ToString());
        public void Add(string key, ulong value) => Add(key, value.ToString());
        public void Note(string text) => lines.Add((string.Empty, text));

        public Scope Push(string name)
        {
            var old = Prefix;
            Prefix = old.Length == 0 ? name : old + "." + name;
            return new Scope(this, old);
        }

        internal readonly struct Scope(LineWriter w, string old) : IDisposable
        {
            public void Dispose() => w.Prefix = old;
        }
    }

    internal static class Helper
    {
        public static string Ascii(ReadOnlySpan<byte> b)
        {
            var sb = new StringBuilder(b.Length);
            foreach (byte c in b) sb.Append(c >= 0x20 && c < 0x7F ? (char)c : (c == 0 ? '\0' : '.'));
            return sb.ToString();
        }

        public static string Fix1616(uint v) => (v / 65536.0).ToString("0.##");

        public static string Hex(ReadOnlySpan<byte> b, int max = 32)
        {
            if (b.IsEmpty) return "(empty)";
            int n = Math.Min(b.Length, max);
            var sb = new StringBuilder(n * 3 + 4);
            for (int i = 0; i < n; i++) { if (i > 0) sb.Append(' '); sb.Append(b[i].ToString("X2")); }
            if (n < b.Length) sb.Append(" ...");
            return sb.ToString();
        }

        public static uint ReverseBits32(uint v)
        {
            uint r = 0;
            for (int i = 0; i < 32; i++) { r = (r << 1) | (v & 1); v >>= 1; }
            return r;
        }

        public static string ChromaName(int idc) => idc switch
        {
            0 => "monochrome (0)",
            1 => "4:2:0 (1)",
            2 => "4:2:2 (2)",
            3 => "4:4:4 (3)",
            _ => $"unknown ({idc})"
        };
        // 加到 static class Helper 内部

        /// <summary>Render a 32-bit id as a FourCC when printable, otherwise as a number.</summary>
        public static string Id32(uint v)
        {
            Span<byte> b = [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
            bool printable = true;
            var sb = new StringBuilder(4);
            foreach (byte c in b)
            {
                if (c == 0xA9) { sb.Append('©'); continue; }         // QuickTime '©xyz' keys
                if (c >= 0x20 && c < 0x7F) sb.Append((char)c);
                else { printable = false; break; }
            }
            return printable ? $"'{sb}' (0x{v:X8})" : $"{v} (0x{v:X8})";
        }
    }
}