using System;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    internal static partial class Codec
    {
        private const int MaxFlacComments = 64;      // guard against huge tag blocks
        private const int MaxFlacBlocks = 64;

        // =====================================================================
        // dfLa : FLACSpecificBox (Encapsulation of FLAC in ISOBMFF)
        // =====================================================================
        public static void ParseDfLa(ReadOnlySpan<byte> d, LineWriter w)
        {
            var c = new Cur(d);
            byte version = c.U8();
            uint flags = c.U24();

            w.Add("version", version + (version != 0 ? " (expected 0)" : ""));
            if (flags != 0) w.Add("flags", $"0x{flags:X6}");

            for (int i = 0; c.Left >= 4 && !c.Bad; i++)
            {
                if (i >= MaxFlacBlocks) { w.Add("parse", "too many metadata blocks, stop"); return; }

                byte h = c.U8();
                bool last = (h & 0x80) != 0;
                int type = h & 0x7F;
                int len = (int)c.U24();

                if (len > c.Left)
                {
                    w.Add($"block[{i}]", $"declared {len} bytes but only {c.Left} left -> stop");
                    return;
                }

                var body = c.Bytes(len);

                using (w.Push($"block[{i}]"))
                {
                    w.Add("block_type", $"{FlacBlockName(type)} ({type})");
                    w.Add("length", len);
                    w.Add("last_metadata_block", last ? 1 : 0);

                    switch (type)
                    {
                        case 0: ParseFlacStreamInfo(body, w); break;
                        case 1: w.Add("padding", $"{len} bytes"); break;
                        case 2: ParseFlacApplication(body, w); break;
                        case 3: ParseFlacSeekTable(body, w); break;
                        case 4: ParseFlacVorbisComment(body, w); break;
                        case 5: ParseFlacCueSheet(body, w); break;
                        case 6: ParseFlacPicture(body, w); break;
                        case 127: w.Add("parse", "block type 127 is forbidden"); break;
                        default: w.Add("raw", Helper.Hex(body, 24)); break;
                    }

                    if (i == 0 && type != 0)
                        w.Add("note", "spec violation: first metadata block must be STREAMINFO");
                }

                if (last) break;
            }

            if (c.Left > 0 && !c.Bad)
                w.Add("trailing_bytes", $"{c.Left} ({Helper.Hex(c.D.Slice(c.P), 16)})");
        }

        /// <summary>METADATA_BLOCK_STREAMINFO — 34 bytes, bit-packed.</summary>
        private static void ParseFlacStreamInfo(ReadOnlySpan<byte> d, LineWriter w)
        {
            if (d.Length < 34) { w.Add("parse", $"STREAMINFO must be 34 bytes, got {d.Length}"); return; }

            var r = new BitReader(d);
            int minBlockSize = (int)r.U(16);
            int maxBlockSize = (int)r.U(16);
            int minFrameSize = (int)r.U(24);
            int maxFrameSize = (int)r.U(24);
            int sampleRate   = (int)r.U(20);
            int channels     = (int)r.U(3) + 1;
            int bitsPerSample = (int)r.U(5) + 1;
            ulong totalSamples = ((ulong)r.U(4) << 32) | r.U(32);
            var md5 = d.Slice(18, 16);                     // bit 144 onwards is byte-aligned

            w.Add("min_block_size", $"{minBlockSize} samples");
            w.Add("max_block_size", $"{maxBlockSize} samples" +
                (minBlockSize == maxBlockSize ? " (fixed block size)" : " (variable block size)"));
            w.Add("min_frame_size", minFrameSize == 0 ? "unknown" : $"{minFrameSize} bytes");
            w.Add("max_frame_size", maxFrameSize == 0 ? "unknown" : $"{maxFrameSize} bytes");
            w.Add("sample_rate", $"{sampleRate} Hz");
            w.Add("channels", channels);
            w.Add("bits_per_sample", bitsPerSample);
            w.Add("total_samples", totalSamples == 0 ? "unknown" : totalSamples.ToString());

            if (totalSamples > 0 && sampleRate > 0)
            {
                double sec = totalSamples / (double)sampleRate;
                w.Add("duration", FormatDuration(sec));
                double uncompressed = (double)totalSamples * channels * bitsPerSample / 8.0;
                w.Add("uncompressed_size", $"{uncompressed / 1048576.0:0.##} MiB");
            }

            bool md5Zero = true;
            foreach (byte b in md5) if (b != 0) { md5Zero = false; break; }
            var sb = new StringBuilder(32);
            foreach (byte b in md5) sb.Append(b.ToString("x2"));
            w.Add("md5_signature", md5Zero ? "(not set)" : sb.ToString());

            if (sampleRate == 0) w.Add("note", "sample_rate = 0 means the stream is not audio-decodable");
            w.Add("codec_string", "fLaC");
        }

        private static void ParseFlacApplication(ReadOnlySpan<byte> d, LineWriter w)
        {
            var c = new Cur(d);
            string id = c.FourCC();
            w.Add("application_id", $"'{id}' (0x{(d.Length >= 4 ? (uint)((d[0] << 24) | (d[1] << 16) | (d[2] << 8) | d[3]) : 0):X8})");
            w.Add("application_data", $"{c.Left} bytes  {Helper.Hex(c.D.Slice(Math.Min(c.P, c.D.Length)), 16)}");
        }

        private static void ParseFlacSeekTable(ReadOnlySpan<byte> d, LineWriter w)
        {
            int count = d.Length / 18;
            w.Add("seek_point_count", count);
            if (d.Length % 18 != 0) w.Add("note", $"length not a multiple of 18 ({d.Length % 18} trailing bytes)");

            var c = new Cur(d);
            int placeholders = 0, shown = 0;
            for (int i = 0; i < count && !c.Bad; i++)
            {
                ulong sample = c.U64();
                ulong offset = c.U64();
                ushort frameSamples = c.U16();

                if (sample == ulong.MaxValue) { placeholders++; continue; }
                if (shown < 4)
                {
                    w.Add($"seek_point[{i}]", $"sample={sample}, stream_offset={offset}, frame_samples={frameSamples}");
                    shown++;
                }
            }
            if (shown < count - placeholders) w.Add("seek_point[...]", $"{count - placeholders - shown} more");
            if (placeholders > 0) w.Add("placeholder_points", placeholders);
        }

        /// <summary>METADATA_BLOCK_VORBIS_COMMENT — note: lengths are LITTLE-endian.</summary>
        private static void ParseFlacVorbisComment(ReadOnlySpan<byte> d, LineWriter w)
        {
            int o = 0;
            if (!TryU32Le(d, ref o, out uint vendorLen) || vendorLen > (uint)(d.Length - o))
            { w.Add("parse", "truncated vendor string"); return; }

            w.Add("vendor", Utf8(d.Slice(o, (int)vendorLen)));
            o += (int)vendorLen;

            if (!TryU32Le(d, ref o, out uint count)) { w.Add("parse", "truncated comment count"); return; }
            w.Add("comment_count", count);

            for (uint i = 0; i < count; i++)
            {
                if (i >= MaxFlacComments) { w.Add("comment[...]", $"{count - i} more not shown"); return; }
                if (!TryU32Le(d, ref o, out uint len) || len > (uint)(d.Length - o))
                { w.Add("parse", $"truncated comment[{i}]"); return; }

                string s = Utf8(d.Slice(o, (int)len));
                o += (int)len;

                int eq = s.IndexOf('=');
                if (eq > 0) w.Add(s.Substring(0, eq).ToUpperInvariant(), Ellipsis(s.Substring(eq + 1), 160));
                else w.Add($"comment[{i}]", Ellipsis(s, 160));
            }
        }

        private static void ParseFlacCueSheet(ReadOnlySpan<byte> d, LineWriter w)
        {
            var c = new Cur(d);
            string catalog = c.Ascii(128).Trim();
            ulong leadIn = c.U64();
            byte fl = c.U8();
            bool isCd = (fl & 0x80) != 0;
            c.Skip(258);                                   // reserved
            byte trackCount = c.U8();

            w.Add("media_catalog_number", catalog.Length == 0 ? "(empty)" : catalog);
            w.Add("lead_in_samples", leadIn);
            w.Add("is_cd", isCd ? 1 : 0);
            w.Add("track_count", trackCount);
            w.Add("note", "track/index list not decoded");
        }

        private static void ParseFlacPicture(ReadOnlySpan<byte> d, LineWriter w)
        {
            var c = new Cur(d);
            uint picType = c.U32();
            int mimeLen = (int)c.U32();
            if (mimeLen < 0 || mimeLen > c.Left) { w.Add("parse", "bad MIME length"); return; }
            string mime = Utf8(c.Bytes(mimeLen));
            int descLen = (int)c.U32();
            if (descLen < 0 || descLen > c.Left) { w.Add("parse", "bad description length"); return; }
            string desc = Utf8(c.Bytes(descLen));
            uint width = c.U32(), height = c.U32(), depth = c.U32(), colors = c.U32();
            uint dataLen = c.U32();

            w.Add("picture_type", $"{FlacPictureTypeName(picType)} ({picType})");
            w.Add("mime_type", mime);
            if (desc.Length > 0) w.Add("description", Ellipsis(desc, 120));
            w.Add("dimensions", $"{width} x {height}");
            w.Add("color_depth", $"{depth} bits/pixel");
            if (colors != 0) w.Add("indexed_colors", colors);
            w.Add("data_length", $"{dataLen} bytes" + (dataLen != c.Left ? $" (actual remaining {c.Left})" : ""));
        }

        // ---------------- small local helpers ----------------

        private static bool TryU32Le(ReadOnlySpan<byte> d, ref int o, out uint v)
        {
            if (o < 0 || o + 4 > d.Length) { v = 0; return false; }
            v = (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));
            o += 4;
            return true;
        }

        private static string Utf8(ReadOnlySpan<byte> b)
        {
            if (b.IsEmpty) return "";
            try { return Encoding.UTF8.GetString(b).Replace("\r", " ").Replace("\n", " "); }
            catch { return Helper.Hex(b, 16); }
        }

        private static string Ellipsis(string s, int max)
            => s.Length <= max ? s : s.Substring(0, max) + "...";

        private static string FormatDuration(double sec)
        {
            var t = TimeSpan.FromSeconds(sec);
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}"
                : $"{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
        }

        private static string FlacBlockName(int t) => t switch
        {
            0 => "STREAMINFO",
            1 => "PADDING",
            2 => "APPLICATION",
            3 => "SEEKTABLE",
            4 => "VORBIS_COMMENT",
            5 => "CUESHEET",
            6 => "PICTURE",
            127 => "FORBIDDEN",
            _ => "reserved"
        };

        private static string FlacPictureTypeName(uint t) => t switch
        {
            0 => "Other",
            1 => "32x32 PNG file icon",
            2 => "Other file icon",
            3 => "Cover (front)",
            4 => "Cover (back)",
            5 => "Leaflet page",
            6 => "Media",
            7 => "Lead artist / performer",
            8 => "Artist / performer",
            9 => "Conductor",
            10 => "Band / orchestra",
            11 => "Composer",
            12 => "Lyricist",
            13 => "Recording location",
            14 => "During recording",
            15 => "During performance",
            16 => "Movie / video screen capture",
            17 => "A bright coloured fish",
            18 => "Illustration",
            19 => "Band / artist logotype",
            20 => "Publisher / studio logotype",
            _ => "reserved"
        };
    }
}