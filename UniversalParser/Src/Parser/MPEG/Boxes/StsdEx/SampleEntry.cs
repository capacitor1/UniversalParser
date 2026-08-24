using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    internal static class SampleEntry
    {
        public enum Kind { Unknown, Video, Audio, Text,Metadata, Hint }

        private static readonly HashSet<string> VideoTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "avc1","avc2","avc3","avc4","hvc1","hev1","hvt1","dvh1","dvhe","dva1","dvav",
            "av01","vp08","vp09","mp4v","s263","encv","mjp2","jpeg","raw ","apch","apcn","apcs","apco","ap4h"
        };
        private static readonly HashSet<string> AudioTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "mp4a","enca","ac-3","ec-3","ac-4","dtsc","dtse","dtsh","dtsl","Opus","fLaC",
            "alac","samr","sawb","sowt","twos","lpcm","ipcm","fpcm",".mp3","mp3 ","MAC3","ulaw","alaw"
        };
        private static readonly HashSet<string> TextTypes = new HashSet<string>(StringComparer.Ordinal)
        { "tx3g","text","wvtt","stpp","stxt","c608","c708","subt","sbtl","enct" };
        
        private static readonly HashSet<string> MetaTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            // ---- standard ----
            "mebx",         // QTFF timed metadata / ISOBMFF boxed metadata  <-- 本次新增
            "metx",         // XMLMetaSampleEntry     (ISO 14496-12)
            "mett",         // TextMetaSampleEntry    (ISO 14496-12)
            "urim",         // URIMetaSampleEntry     (ISO 14496-12)
            "tmcd",         // QuickTime timecode
            "tc64",         // 64-bit timecode
            // ---- vendor, no config box, payload lives in mdat ----
            "gpmd",         // GoPro GPMF
            "camm",         // Google camera motion metadata
            "rtmd",         // Sony real-time metadata
            "fdsc",         // GoPro file descriptor
            "djmd","dbgi",  // DJI
            "ctmd",         // Canon timed metadata
            "psmd",         // Panasonic static metadata
            "ssmd",         // Rove
            "marl","mari",  // GM
        };

        public static Kind KindOf(string type, string handlerType = null)
        {
            if (VideoTypes.Contains(type)) return Kind.Video;
            if (AudioTypes.Contains(type)) return Kind.Audio;
            if (TextTypes.Contains(type))  return Kind.Text;
            if (MetaTypes.Contains(type))  return Kind.Metadata;
            if (type == "rtp " || type == "sm2t") return Kind.Hint;

            // fallback: use hdlr from the parent trak if the caller can supply it
            switch (handlerType)
            {
                case "vide": return Kind.Video;
                case "soun": return Kind.Audio;
                case "meta":
                case "mdta":
                case "nrtm":
                case "tmcd": return Kind.Metadata;
                case "subt":
                case "text":
                case "sbtl": return Kind.Text;
                case "hint": return Kind.Hint;
                default: return Kind.Unknown;
            }
        }

        /// <param name="body">entry payload WITHOUT the 8/16-byte box header.</param>
        public static void Parse(string type, ReadOnlySpan<byte> body, LineWriter w, string handlerType = null)
        {
            var c = new Cur(body);
            var kind = KindOf(type, handlerType);

            c.Skip(6);                                  // reserved[6]
            w.Add("data_reference_index", c.U16());
            w.Add("kind", kind.ToString());

            var ctx = new ParseCtx { EntryType = type, HandlerType = handlerType, Kind = kind };
            switch (kind)
            {
                case Kind.Video: ParseVisual(ref c, w, ctx); break;
                case Kind.Audio: ParseAudio(ref c, w, ctx); break;
                case Kind.Metadata: ParseMetadata(type, ref c, w, ctx); break; 
                case Kind.Text:  ParseText(ref c, w, type, ctx); break;
                default:
                    w.Add("payload", $"{c.Left} bytes not interpreted | {Helper.Hex(c.D.Slice(Math.Min(c.P, c.D.Length)), 32)}");
                    var rest = new Cur(c.D, c.P);
                    WalkChildBoxes(ref rest, w, 0, ctx);
                    return;
            }

            WalkChildBoxes(ref c, w, 0);
            if (c.Bad) w.Note("WARNING: sample entry truncated / malformed");
        }
        // ---------------- MetaDataSampleEntry family ----------------
        private static void ParseMetadata(string type, ref Cur c, LineWriter w, ParseCtx ctx = null)
        {
            switch (type)
            {
                case "mebx":
                    // Body is nothing but child boxes ('keys' required, 'btrt' optional).
                    break;

                case "tmcd":
                case "tc64":
                    Codec.ParseTimecodeBody(ref c, w, type == "tc64");
                    break;

                case "metx":
                    // utf8string content_encoding; utf8string namespace; utf8string schema_location;
                    if (!c.LooksLikeBox()) w.Add("content_encoding", Or(c.CStringUtf8(), "(none)"));
                    if (!c.LooksLikeBox()) w.Add("namespace", c.CStringUtf8());
                    if (!c.LooksLikeBox()) w.Add("schema_location", Or(c.CStringUtf8(), "(none)"));
                    break;

                case "mett":
                    // utf8string content_encoding; utf8string mime_format;
                    if (!c.LooksLikeBox()) w.Add("content_encoding", Or(c.CStringUtf8(), "(none)"));
                    if (!c.LooksLikeBox()) w.Add("mime_format", c.CStringUtf8());
                    break;

                case "urim":
                    // the URI lives in a mandatory 'uri ' child box
                    break;

                default:
                    w.Add("note", $"'{type}' is a metadata sample entry with no standardised body; " +
                                  "its payload format is defined by the vendor and lives in the samples");
                    break;
            }

            WalkChildBoxes(ref c, w, 0, ctx);

            static string Or(string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;
        }
        // ---------------- VisualSampleEntry (ISO/IEC 14496-12) ----------------
        private static void ParseVisual(ref Cur c, LineWriter w, ParseCtx ctx = null)
        {
            c.Skip(2);                                  // pre_defined
            c.Skip(2);                                  // reserved
            c.Skip(12);                                 // pre_defined[3]

            ushort width  = c.U16();
            ushort height = c.U16();
            uint hres = c.U32(), vres = c.U32();
            c.Skip(4);                                  // reserved
            ushort frameCount = c.U16();
            string compressor = c.Pascal32();
            ushort depth  = c.U16();
            short preDef  = c.S16();                    // = -1

            w.Add("width", width);
            w.Add("height", height);
            w.Add("resolution", $"{Helper.Fix1616(hres)} x {Helper.Fix1616(vres)} dpi");
            w.Add("frame_count", frameCount);
            w.Add("compressor_name", compressor.Length == 0 ? "(empty)" : compressor);
            w.Add("depth", depth switch
            {
                24 => "24 (colour, no alpha)",
                32 => "32 (colour + alpha)",
                40 or 48 or 56 => $"{depth} (greyscale)",
                _ => depth.ToString()
            });
            if (preDef != -1) w.Note($"note: pre_defined should be -1, got {preDef}");
        }

        // ---------------- AudioSampleEntry (+ QuickTime v1/v2) ----------------
        private static void ParseAudio(ref Cur c, LineWriter w, ParseCtx ctx = null)
        {
            ushort version = c.U16();                   // QT version (0 for plain ISO BMFF)
            c.Skip(2);                                  // revision level
            c.Skip(4);                                  // vendor

            ushort channels   = c.U16();
            ushort sampleSize = c.U16();
            short compId      = c.S16();
            ushort packetSize = c.U16();
            uint srRaw        = c.U32();                // 16.16

            w.Add("sound_version", version);
            w.Add("channel_count", channels);
            w.Add("sample_size", sampleSize);
            w.Add("sample_rate", Helper.Fix1616(srRaw));
            if (compId == -2) w.Note("note: compressionID = -2 -> QuickTime 'wave'/sound v1 layout");

            if (version == 1)
            {
                w.Add("samples_per_packet", c.U32());
                w.Add("bytes_per_packet", c.U32());
                w.Add("bytes_per_frame", c.U32());
                w.Add("bytes_per_sample", c.U32());
            }
            else if (version == 2)
            {
                uint structSize = c.U32();
                double sr = BitConverter.Int64BitsToDouble(unchecked((long)c.U64()));
                uint nch = c.U32();
                c.Skip(4);                              // always 0x7F000000
                uint constBitsPerChannel = c.U32();
                uint fmtFlags = c.U32();
                uint constBytesPerPacket = c.U32();
                uint constFramesPerPacket = c.U32();

                w.Add("v2.struct_size", structSize);
                w.Add("v2.sample_rate", sr.ToString("0.##"));
                w.Add("v2.channels", nch);
                w.Add("v2.bits_per_channel", constBitsPerChannel);
                w.Add("v2.format_flags", $"0x{fmtFlags:X8}");
                w.Add("v2.bytes_per_packet", constBytesPerPacket);
                w.Add("v2.frames_per_packet", constFramesPerPacket);
                w.Note("note: for sound v2 the fields above (channel_count/sample_rate) are placeholders");
            }
        }
        // ---------------- timed text / subtitle sample entries ----------------
        private static void ParseText(ref Cur c, LineWriter w, string entryType, ParseCtx ctx = null)
        {
            switch (entryType)
            {
                case "tx3g":
                    TimedText.ParseTx3g(ref c, w);
                    break;
                case "text":
                    TimedText.ParseQtText(ref c, w);
                    break;
                case "stpp":
                    TimedText.ParseStpp(ref c, w);
                    break;
                case "wvtt":
                case "stxt":
                    // PlainTextSampleEntry has no fields of its own; go straight to child boxes
                    break;
                default:
                    w.Add("payload", $"{c.Left} bytes not interpreted: {Helper.Hex(c.D.Slice(Math.Min(c.P, c.D.Length)), 24)}");
                    return;
            }

            WalkChildBoxes(ref c, w, 0);
        }

        // ---------------- child boxes ----------------
        private static void WalkChildBoxes(ref Cur c, LineWriter w, int depth, ParseCtx ctx = null)
        {
            if (depth > 6) return;

            while (c.Left >= 8 && !c.Bad)
            {
                int start = c.P;
                long size = c.U32();
                string t  = c.FourCC();
                int header = 8;

                if (size == 1) { size = (long)c.U64(); header = 16; }
                else if (size == 0) size = c.D.Length - start;       // extends to the end

                if (size < header || start + size > c.D.Length)
                {
                    w.Add($"child['{t}']", $"invalid size {size} at +{start} -> stop");
                    return;
                }

                var payload = c.D.Slice(start + header, (int)size - header);
                using (w.Push(t)) DispatchChild(t, payload, w, depth, ctx);
                c.P = start + (int)size;
            }
        }

        private static void DispatchChild(string t, ReadOnlySpan<byte> p, LineWriter w, int depth, ParseCtx ctx = null)
        {
            switch (t)
            {
                // ---- codec configuration ----
                case "avcC": Codec.ParseAvcC(p, w); break;
                case "hvcE":
                case "hvcC": Codec.ParseHvcC(p, w); break;
                case "esds": Codec.ParseEsds(p, w); break;
                case "av1C": Codec.ParseAv1C(p, w); break;
                case "dfLa": Codec.ParseDfLa(p, w); break;
                case "dOps": Codec.ParseDOps(p, w); break;
                case "dac3": Codec.ParseDac3(p, w); break;
                case "dec3": Codec.ParseDec3(p, w); break;
                case "dvcC":
                case "dvvC":
                case "dvwC": Codec.ParseDoviConfig(p, w, t); break;
                case "vpcC": Codec.ParseVpcC(p, w); break;
                case "SmDm": Codec.ParseSmDm(p, w); break;
                case "CoLL": Codec.ParseCoLL(p, w); break;
                case "ftab": TimedText.ParseFtab(p, w); break;
                case "vttC": TimedText.ParseVttC(p, w); break;
                case "vlab": TimedText.ParseVlab(p, w); break;
                case "txtC": TimedText.ParseTxtC(p, w); break;

                // ---- generic descriptive boxes ----
                case "btrt":
                {
                    var b = new Cur(p);
                    w.Add("buffer_size_db", b.U32());
                    w.Add("max_bitrate", $"{b.U32()} bps");
                    w.Add("avg_bitrate", $"{b.U32()} bps");
                    break;
                }
                case "pasp":
                {
                    var b = new Cur(p);
                    uint h = b.U32(), v = b.U32();
                    w.Add("pixel_aspect_ratio", $"{h}:{v}");
                    break;
                }
                case "colr":
                {
                    var b = new Cur(p);
                    string ct = b.FourCC();
                    w.Add("colour_type", ct);
                    if (ct == "nclx" || ct == "nclc")
                    {
                        int pcp = b.U16(), ptc = b.U16(), pmc = b.U16();
                        w.Add("colour_primaries", $"{Codec.ColourPrimariesName(pcp)} ({pcp})");
                        w.Add("transfer_characteristics", $"{Codec.TransferCharacteristicsName(ptc)} ({ptc})");
                        w.Add("matrix_coefficients", $"{Codec.MatrixCoefficientsName(pmc)} ({pmc})");
                        if (ct == "nclx") w.Add("full_range_flag", (b.U8() & 0x80) != 0 ? 1 : 0);
                    }
                    else w.Add("icc_profile", $"{p.Length - 4} bytes");
                    break;
                }
                case "clap":
                {
                    var b = new Cur(p);
                    w.Add("clean_aperture_width",  $"{b.U32()}/{b.U32()}");
                    w.Add("clean_aperture_height", $"{b.U32()}/{b.U32()}");
                    w.Add("horiz_off", $"{b.U32()}/{b.U32()}");
                    w.Add("vert_off",  $"{b.U32()}/{b.U32()}");
                    break;
                }
                case "srat": w.Add("sampling_rate", new Cur(p).U32()); break;

                // ---- protection (encv / enca) ----
                case "frma": w.Add("original_format", new Cur(p).FourCC()); break;
                case "schm":
                {
                    var b = new Cur(p);
                    b.Skip(4);                                  // version + flags
                    w.Add("scheme_type", b.FourCC());
                    uint ver = b.U32();
                    w.Add("scheme_version", $"{ver >> 16}.{ver & 0xFFFF}");
                    break;
                }
                case "sinf":
                case "schi":
                case "wave":                                    // QuickTime wrapper, may hold esds
                {
                    var b = new Cur(p);
                    WalkChildBoxes(ref b, w, depth + 1, ctx);
                    break;
                }
                // ---- timed metadata ----
                case "keys": Codec.ParseMebxKeyTable(p, w); break;      // 注意：mebx 语义，非 meta 语义

                case "uri ":                                                   // URIBox (FullBox)
                {
                    var b = new Cur(p); b.Skip(4);
                    w.Add("uri", b.CStringUtf8());
                    break;
                }
                case "uriI":                                                   // URIInitBox (FullBox)
                {
                    var b = new Cur(p); b.Skip(4);
                    w.Add("init_data", $"{b.Left} bytes  {Helper.Hex(b.D.Slice(b.P), 32)}");
                    break;
                }
                case "name":                                                   // QuickTime name atom (tmcd etc.)
                {
                    var b = new Cur(p);
                    int len = b.U16();
                    int lang = b.U16();
                    w.Add("name", len <= b.Left ? b.Utf8(len) : Encoding.UTF8.GetString(p).TrimEnd('\0'));
                    w.Add("language", lang);
                    break;
                }
                // ---- 本次实现的四个 ----
                case "damr": Codec.ParseDamr(p, w, ctx); break;
                case "alac": Codec.ParseAlacConfig(p, w, ctx); break;
                case "mhaC": Codec.ParseMhaC(p, w, ctx); break;
                case "pcmC": Codec.ParsePcmC(p, w, ctx); break;
                case "chnl": Codec.ParseChnl(p, w, ctx); break;

                // ---- 顺手带的（同族、各 3 行）----
                case "dawp":                                        // AMR-WB+ (3GPP TS 26.244 §6.9)
                {
                    var b = new Cur(p);
                    w.Add("vendor", b.FourCC());
                    w.Add("decoder_version", b.U8());
                    w.Add("note", "AMR-WB+ carries no per-sample framing info in this box");
                    break;
                }
                case "d263":                                        // H.263 (3GPP TS 26.244 §6.8)
                {
                    var b = new Cur(p);
                    w.Add("vendor", b.FourCC());
                    w.Add("decoder_version", b.U8());
                    w.Add("h263_level", b.U8());
                    w.Add("h263_profile", b.U8());
                    break;
                }
                case "enda":                                        // QuickTime endianness override
                {
                    var b = new Cur(p);
                    int le = b.U16();
                    w.Add("endianness", le == 1 ? "little-endian" : "big-endian");
                    w.Add("note", "QuickTime override for uncompressed audio (sowt / in24 / in32 / fl32 / fl64)");
                    break;
                }
                default:
                    w.Add("size", p.Length);
                    w.Add("raw", Helper.Hex(p, 32));
                    break;
            }
        }
    }
}