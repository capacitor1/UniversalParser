using System;

namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    internal static partial class Codec
    {
        private static readonly int[] AacSampleRates =
            { 96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350, 0, 0, -1 };

        private sealed class EsdsState { public byte ObjectType; }

        public static void ParseEsds(ReadOnlySpan<byte> d, LineWriter w)
        {
            var c = new Cur(d);
            c.Skip(4);                                   // FullBox version + flags
            var st = new EsdsState();
            ParseDescriptors(ref c, w, st, 0);
        }

        private static void ParseDescriptors(ref Cur c, LineWriter w, EsdsState st, int depth)
        {
            if (depth > 6) return;

            while (c.Left >= 2 && !c.Bad)
            {
                byte tag = c.U8();
                int size = ReadExpandableSize(ref c);
                if (c.Bad || size < 0) return;
                if (size > c.Left)
                {
                    w.Add($"descriptor[0x{tag:X2}]", $"declared {size} bytes but only {c.Left} left -> stop");
                    return;
                }

                var body = c.Bytes(size);
                var b = new Cur(body);

                switch (tag)
                {
                    case 0x03:                           // ES_Descriptor
                        using (w.Push("es"))
                        {
                            w.Add("es_id", b.U16());
                            byte fl = b.U8();
                            w.Add("stream_priority", fl & 0x1F);
                            if ((fl & 0x80) != 0) w.Add("depends_on_es_id", b.U16());
                            if ((fl & 0x40) != 0) { int n = b.U8(); w.Add("url", Helper.Ascii(b.Bytes(n))); }
                            if ((fl & 0x20) != 0) w.Add("ocr_es_id", b.U16());
                            ParseDescriptors(ref b, w, st, depth + 1);
                        }
                        break;

                    case 0x04:                           // DecoderConfigDescriptor
                        using (w.Push("dcd"))
                        {
                            byte oti = b.U8();
                            st.ObjectType = oti;
                            byte x = b.U8();
                            int streamType = (x >> 2) & 0x3F;
                            w.Add("object_type", $"{OtiName(oti)} (0x{oti:X2})");
                            w.Add("stream_type", $"{StreamTypeName(streamType)} ({streamType})");
                            w.Add("up_stream", (x & 0x02) != 0 ? 1 : 0);
                            w.Add("buffer_size_db", b.U24());
                            w.Add("max_bitrate", $"{b.U32()} bps");
                            w.Add("avg_bitrate", $"{b.U32()} bps");
                            ParseDescriptors(ref b, w, st, depth + 1);
                        }
                        break;

                    case 0x05:                           // DecoderSpecificInfo
                        using (w.Push("dsi"))
                        {
                            w.Add("bytes", $"{size} | {Helper.Hex(body, 24)}");
                            if (st.ObjectType == 0x40 || (st.ObjectType >= 0x66 && st.ObjectType <= 0x68))
                                ParseAudioSpecificConfig(body, w, st.ObjectType);
                        }
                        break;

                    case 0x06:                           // SLConfigDescriptor
                        w.Add("sl_config.predefined", b.U8());
                        break;

                    default:
                        w.Add($"descriptor[0x{tag:X2}]", $"{size} bytes | {Helper.Hex(body, 16)}");
                        break;
                }
            }
        }

        /// <summary>ISO 14496-1 expandable size: 7 bits per byte, MSB = continuation. Max 4 bytes.</summary>
        private static int ReadExpandableSize(ref Cur c)
        {
            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                if (!c.Need(1)) return -1;
                byte b = c.U8();
                value = (value << 7) | (b & 0x7F);
                if ((b & 0x80) == 0) break;
            }
            return value;
        }

        // ---------------- AudioSpecificConfig (ISO/IEC 14496-3) ----------------
        public static void ParseAudioSpecificConfig(ReadOnlySpan<byte> d, LineWriter w, byte oti = 0x40)
        {
            var r = new BitReader(d);

            int aot = ReadAot(ref r);
            int baseAot = aot;
            int sfIndex = (int)r.U(4);
            int sampleRate = sfIndex == 0x0F ? (int)r.U(24) : RateFromIndex(sfIndex);
            int chCfg = (int)r.U(4);

            bool sbr = false, ps = false;
            int extRate = 0;

            if (aot == 5 || aot == 29)
            {
                sbr = true;
                ps = aot == 29;
                int ei = (int)r.U(4);
                extRate = ei == 0x0F ? (int)r.U(24) : RateFromIndex(ei);
                aot = ReadAot(ref r);
                if (aot == 22) r.Skip(4);                 // extensionChannelConfiguration
            }

            w.Add("audio_object_type", $"{AotName(aot)} ({aot})");
            w.Add("sampling_frequency", sfIndex == 0x0F
                ? $"{sampleRate} Hz (explicit)"
                : $"{sampleRate} Hz (index {sfIndex})");
            w.Add("channel_configuration", $"{ChannelConfigName(chCfg)} ({chCfg})");

            if (IsGaSpecific(aot))
            {
                bool len960 = r.Flag();
                w.Add("frame_length", len960 ? "960 samples" : "1024 samples");
                if (r.Flag()) r.Skip(14);                 // dependsOnCoreCoder -> coreCoderDelay
                bool extensionFlag = r.Flag();
                if (chCfg == 0) w.Note("note: channel layout carried in program_config_element (not decoded)");
                if (extensionFlag && (aot == 22)) r.Skip(16);
                if (extensionFlag && (aot == 17 || aot == 19 || aot == 20 || aot == 23))
                    r.Skip(3);                            // aacSectionDataResilience etc.
            }

            // explicit backward-compatible SBR/PS signalling
            if (baseAot != 5 && baseAot != 29 && r.BitsLeft >= 16)
            {
                uint sync = r.U(11);
                if (sync == 0x2B7)
                {
                    int extAot = (int)r.U(5);
                    if (extAot == 5)
                    {
                        sbr = r.Flag();
                        if (sbr)
                        {
                            int ei = (int)r.U(4);
                            extRate = ei == 0x0F ? (int)r.U(24) : RateFromIndex(ei);
                            if (r.BitsLeft >= 12 && r.U(11) == 0x548) ps = r.Flag();
                        }
                    }
                }
            }

            if (sbr)
            {
                w.Add("sbr", "present");
                if (extRate > 0) w.Add("extension_sampling_frequency", $"{extRate} Hz");
            }
            if (ps) w.Add("ps", "present");

            string tail = sbr ? (ps ? " (HE-AAC v2)" : " (HE-AAC v1)") : "";
            w.Add("summary", $"{AotName(aot)}{tail}, {sampleRate} Hz, {ChannelConfigName(chCfg)}");
            w.Add("codec_string", $"mp4a.{oti:X2}.{aot}");
        }

        private static int ReadAot(ref BitReader r)
        {
            int a = (int)r.U(5);
            return a == 31 ? 32 + (int)r.U(6) : a;
        }

        private static int RateFromIndex(int i)
            => i >= 0 && i < AacSampleRates.Length && AacSampleRates[i] > 0 ? AacSampleRates[i] : 0;

        private static bool IsGaSpecific(int aot) => aot switch
        {
            1 or 2 or 3 or 4 or 6 or 7 or 17 or 19 or 20 or 21 or 22 or 23 => true,
            _ => false
        };

        private static string AotName(int a) => a switch
        {
            1  => "AAC Main",   2  => "AAC LC",   3  => "AAC SSR",  4  => "AAC LTP",
            5  => "SBR",        6  => "AAC Scalable", 7 => "TwinVQ", 8 => "CELP",
            9  => "HVXC",       14 => "ER AAC LD (14496-3/AMD)",
            17 => "ER AAC LC",  19 => "ER AAC LTP", 20 => "ER AAC Scalable",
            21 => "ER TwinVQ",  22 => "ER BSAC",  23 => "ER AAC LD",
            29 => "AAC LC + SBR + PS", 32 => "MPEG-1/2 Layer I",
            33 => "MPEG-1/2 Layer II", 34 => "MPEG-1/2 Layer III",
            36 => "ALS",        39 => "AAC ELD",  42 => "USAC/xHE-AAC",
            _  => "unknown"
        };

        private static string ChannelConfigName(int c) => c switch
        {
            0 => "defined in AOT specific config",
            1 => "1ch mono", 2 => "2ch stereo", 3 => "3ch (L,R,C)",
            4 => "4ch (L,R,C,Cs)", 5 => "5ch (L,R,C,Ls,Rs)",
            6 => "6ch 5.1", 7 => "8ch 7.1",
            _ => "reserved"
        };

        private static string OtiName(byte o) => o switch
        {
            0x20 => "MPEG-4 Visual",
            0x21 => "H.264 / AVC",
            0x23 => "H.265 / HEVC",
            0x40 => "MPEG-4 Audio (AAC)",
            0x66 => "MPEG-2 AAC Main",
            0x67 => "MPEG-2 AAC LC",
            0x68 => "MPEG-2 AAC SSR",
            0x69 => "MPEG-2 Audio Part 3 (MP3)",
            0x6B => "MPEG-1 Audio (MP3)",
            0x6C => "JPEG",
            0xA5 => "AC-3",
            0xA6 => "E-AC-3",
            0xA9 => "DTS",
            0xDD => "Vorbis (non-standard)",
            _    => "other / private"
        };

        private static string StreamTypeName(int t) => t switch
        {
            1 => "ObjectDescriptor", 2 => "ClockReference", 3 => "SceneDescription",
            4 => "Visual", 5 => "Audio", 6 => "MPEG-7", 7 => "IPMP",
            8 => "ObjectContentInfo", 9 => "MPEG-J", 10 => "Interaction",
            _ => "unknown"
        };
    }
}