using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    internal static partial class Codec
    {
        // =====================================================================
        // 'damr' : AMRSpecificBox  (3GPP TS 26.244 clause 6.7)
        //   struct AMRDecSpecStruc {
        //       uint32 vendor; uint8 decoder_version; uint16 mode_set;
        //       uint8 mode_change_period; uint8 frames_per_sample;
        //   }
        // =====================================================================
        public static void ParseDamr(ReadOnlySpan<byte> d, LineWriter w, ParseCtx ctx = null)
        {
            var c = new Cur(d);

            string vendor = c.FourCC();
            byte decoderVersion = c.U8();
            ushort modeSet = c.U16();
            byte modeChangePeriod = c.U8();
            byte framesPerSample = c.U8();

            if (c.Bad) { w.Add("parse", $"truncated: need 9 bytes, got {d.Length}"); return; }

            // 'samr' = AMR-NB (TS 26.101), 'sawb' = AMR-WB (TS 26.201).
            // The FT->bitrate mapping differs, so we must know the parent entry type.
            bool wideBand = ctx?.EntryType == "sawb";
            bool known = ctx?.EntryType == "samr" || ctx?.EntryType == "sawb";

            w.Add("vendor", vendor);
            w.Add("decoder_version", decoderVersion == 0 ? "0 (irrelevant)" : decoderVersion.ToString());
            w.Add("mode_set", $"0x{modeSet:X4}");
            w.Add("active_modes", known
                ? DescribeAmrModeSet(modeSet, wideBand)
                : $"{DescribeAmrModeSet(modeSet, false)}  [assuming AMR-NB; parent entry type unknown]");
            w.Add("mode_change_period", modeChangePeriod == 0
                ? "0 (no restriction)"
                : $"{modeChangePeriod} (mode may only change every {modeChangePeriod} frames)");
            w.Add("frames_per_sample", framesPerSample == 0
                ? "0 (not signalled)"
                : $"{framesPerSample} AMR frame(s) per sample");

            if (known)
            {
                w.Add("codec", wideBand ? "AMR-WB (G.722.2), 16 kHz" : "AMR-NB, 8 kHz");
                if (framesPerSample > 0)
                    w.Add("sample_duration", $"{framesPerSample * 20} ms ({framesPerSample} x 20 ms)");
            }

            // Sanity: the well-known "everything present" masks.
            if (modeSet == 0x81FF) w.Add("note", "0x81FF = all AMR-NB modes + SID + no-data");
            if (modeSet == 0x83FF) w.Add("note", "0x83FF = all AMR-WB modes + SID + no-data");
            if (modeSet == 0) w.Add("warning", "mode_set = 0 -> no active codec mode declared");

            if (c.Left > 0) w.Add("trailing_bytes", $"{c.Left}  {Helper.Hex(d.Slice(c.P), 16)}");
        }

        // FT field -> mode, per TS 26.101 Table 1a (NB) and TS 26.201 Table 1a (WB)
        private static readonly string[] AmrNbModes =
        {
            "4.75", "5.15", "5.90", "6.70", "7.40", "7.95", "10.2", "12.2",
            "AMR SID", "GSM-EFR SID", "TDMA-EFR SID", "PDC-EFR SID",
            "future(12)", "future(13)", "future(14)", "no data"
        };

        private static readonly string[] AmrWbModes =
        {
            "6.60", "8.85", "12.65", "14.25", "15.85", "18.25", "19.85", "23.05", "23.85",
            "AMR-WB SID", "reserved(10)", "reserved(11)", "reserved(12)", "reserved(13)",
            "speech lost", "no data"
        };

        private static string DescribeAmrModeSet(ushort modeSet, bool wideBand)
        {
            var names = wideBand ? AmrWbModes : AmrNbModes;
            int speechModes = wideBand ? 9 : 8;

            var rates = new List<string>();
            var others = new List<string>();

            for (int i = 0; i < 16; i++)
            {
                if ((modeSet & (1 << i)) == 0) continue;
                if (i < speechModes) rates.Add(names[i]);
                else others.Add(names[i]);
            }

            if (rates.Count == 0 && others.Count == 0) return "none";

            var sb = new StringBuilder();
            if (rates.Count > 0) sb.Append(string.Join(" / ", rates)).Append(" kbit/s");
            if (others.Count > 0) sb.Append(sb.Length > 0 ? "  +  " : "").Append(string.Join(", ", others));
            return sb.ToString();
        }

        // =====================================================================
        // 'alac' : ALAC magic cookie, wrapped in a FullBox inside the 'alac' entry.
        //   FullBox header (4)
        //   ALACSpecificConfig    (24, mandatory)
        //   ALACChannelLayoutInfo (24, optional)
        // NOTE: this box has the SAME fourcc as its parent sample entry.
        // =====================================================================
        public static void ParseAlacConfig(ReadOnlySpan<byte> d, LineWriter w, ParseCtx ctx = null)
        {
            var c = new Cur(d);

            byte version = c.U8();
            uint flags = c.U24();
            if (version != 0) w.Add("version", $"{version} (expected 0)");
            if (flags != 0) w.Add("flags", $"0x{flags:X6} (expected 0)");

            if (c.Left < 24)
            {
                w.Add("parse", $"magic cookie truncated: {c.Left} bytes after FullBox header, need >= 24");
                w.Add("raw", Helper.Hex(d, 32));
                return;
            }

            uint frameLength = c.U32();
            byte compatibleVersion = c.U8();
            byte bitDepth = c.U8();
            byte pb = c.U8();
            byte mb = c.U8();
            byte kb = c.U8();
            byte numChannels = c.U8();
            ushort maxRun = c.U16();
            uint maxFrameBytes = c.U32();
            uint avgBitRate = c.U32();
            uint sampleRate = c.U32();

            w.Add("frame_length", $"{frameLength} samples/packet" + (frameLength == 4096 ? " (default)" : ""));
            w.Add("compatible_version", compatibleVersion == 0
                ? "0"
                : $"{compatibleVersion} (must be 0 -> stream may be undecodable)");
            w.Add("bit_depth", $"{bitDepth} bit");
            w.Add("tuning", $"pb={pb} mb={mb} kb={kb}  (unused; canonical 40/10/14)");
            w.Add("num_channels", numChannels);
            w.Add("max_run", maxRun);
            w.Add("max_frame_bytes", maxFrameBytes == 0 ? "0 (unknown)" : $"{maxFrameBytes} bytes");
            w.Add("avg_bit_rate", avgBitRate == 0 ? "0 (unknown)" : $"{avgBitRate} bps");
            w.Add("sample_rate", $"{sampleRate} Hz");

            if (bitDepth > 32) w.Add("warning", $"bit_depth {bitDepth} exceeds the ALAC maximum of 32");
            if (numChannels is 0 or > 8) w.Add("warning", $"num_channels {numChannels} outside the supported 1..8");

            // Apple writes the real values here; the AudioSampleEntry header is often just a stub.
            if (ctx != null)
            {
                if (ctx.ChannelCount != numChannels)
                    w.Add("note", $"AudioSampleEntry says channelcount={ctx.ChannelCount}, cookie says {numChannels} " +
                                  "(cookie is authoritative)");
                if (Math.Abs(ctx.SampleRate - sampleRate) > 0.5)
                    w.Add("note", $"AudioSampleEntry says samplerate={ctx.SampleRate:0.##}, cookie says {sampleRate} " +
                                  "(cookie is authoritative)");
            }

            // ---- optional ALACChannelLayoutInfo ----
            if (c.Left >= 24)
            {
                int at = c.P;
                uint infoSize = c.U32();
                string infoId = c.FourCC();
                uint versionFlags = c.U32();
                uint layoutTag = c.U32();
                uint reserved1 = c.U32();
                uint reserved2 = c.U32();

                using (w.Push("channel_layout_info"))
                {
                    if (infoId == "chan" && infoSize == 24)
                    {
                        w.Add("channel_layout_tag", $"{AlacChannelLayoutTagName(layoutTag)}  (0x{layoutTag:X8})");
                        w.Add("channels_in_tag", layoutTag & 0xFFFF);
                        if ((layoutTag & 0xFFFF) != numChannels)
                            w.Add("warning", $"tag encodes {layoutTag & 0xFFFF} channels but num_channels={numChannels}");
                        if (versionFlags != 0) w.Add("version_flags", $"0x{versionFlags:X8} (expected 0)");
                        if (reserved1 != 0 || reserved2 != 0)
                            w.Add("reserved", $"0x{reserved1:X8} 0x{reserved2:X8} (expected 0)");
                    }
                    else
                    {
                        w.Add("info_size", infoSize);
                        w.Add("info_id", infoId);
                        w.Add("raw", Helper.Hex(d.Slice(at, Math.Min(24, d.Length - at)), 24));
                        w.Add("note", "not a well-formed 'chan' layout block");
                    }
                }
            }
            else
            {
                w.Add("channel_layout_info", numChannels switch
                {
                    1 => "absent -> mono",
                    2 => "absent -> stereo (L R)",
                    _ => $"absent -> {numChannels} discrete channels, no defined ordering"
                });
            }

            // Legacy encoders appended an 8-byte terminator (size = 8, id = 0).
            if (c.Left == 8)
            {
                uint termSize = c.U32();
                uint termId = c.U32();
                if (termSize == 8 && termId == 0)
                    w.Add("note", "trailing 8-byte terminator -> cookie written by a legacy ALAC encoder");
                else
                    w.Add("trailing_bytes", $"8  {Helper.Hex(d.Slice(d.Length - 8), 8)}");
            }
            else if (c.Left > 0)
            {
                w.Add("trailing_bytes", $"{c.Left}  {Helper.Hex(d.Slice(c.P), 16)}");
            }
        }

        // ALACAudioTypes.h : (tagId << 16) | channelCount
        private static string AlacChannelLayoutTagName(uint tag) => tag switch
        {
            (100u << 16) | 1 => "Mono — C",
            (101u << 16) | 2 => "Stereo — L R",
            (113u << 16) | 3 => "MPEG 3.0 B — C L R",
            (116u << 16) | 4 => "MPEG 4.0 B — C L R Cs",
            (120u << 16) | 5 => "MPEG 5.0 D — C L R Ls Rs",
            (124u << 16) | 6 => "MPEG 5.1 D — C L R Ls Rs LFE",
            (142u << 16) | 7 => "AAC 6.1 — C L R Ls Rs Cs LFE",
            (127u << 16) | 8 => "MPEG 7.1 B — C Lc Rc L R Ls Rs LFE",
            _ => "unknown / not an ALAC-supported layout"
        };

        // =====================================================================
        // 'mhaC' : MHADecoderConfigurationRecord (ISO/IEC 23008-3 clause 20)
        //   uint8  configurationVersion
        //   uint8  mpegh3daProfileLevelIndication
        //   uint8  referenceChannelLayout        <- CICP ChannelConfiguration (23001-8)
        //   uint16 mpegh3daConfigLength
        //   uint8  mpegh3daConfig[mpegh3daConfigLength]
        // =====================================================================
        public static void ParseMhaC(ReadOnlySpan<byte> d, LineWriter w, ParseCtx ctx = null)
        {
            var c = new Cur(d);

            byte cfgVersion = c.U8();
            byte profileLevel = c.U8();
            byte refChannelLayout = c.U8();
            int cfgLength = c.U16();

            if (c.Bad) { w.Add("parse", $"truncated header: got {d.Length} bytes, need >= 5"); return; }

            w.Add("configuration_version", cfgVersion == 1 ? "1" : $"{cfgVersion} (expected 1)");
            w.Add("profile_level_indication", $"{MpeghProfileLevelName(profileLevel)}  (0x{profileLevel:X2})");
            w.Add("reference_channel_layout", $"{CicpChannelConfigName(refChannelLayout)}  (CICP {refChannelLayout})");
            w.Add("mpegh3da_config_length", cfgLength);

            // RFC 6381 / 23008-3 clause 21: <4CC>.0x<profile-level-id>
            string fourcc = ctx?.EntryType;
            if (fourcc is not ("mha1" or "mha2" or "mhm1" or "mhm2")) fourcc = "mhm1";
            w.Add("codec_string", $"{fourcc}.0x{profileLevel:X2}");

            var cfg = c.Bytes(cfgLength);
            if (c.Bad)
            {
                w.Add("parse", $"mpegh3daConfig declares {cfgLength} bytes but only {d.Length - 5} available");
                return;
            }

            w.Add("mpegh3da_config", $"{cfgLength} bytes  {Helper.Hex(cfg, 32)}");

            if (!cfg.IsEmpty)
                using (w.Push("mpegh3daConfig"))
                    ParseMpegh3daConfigHead(cfg, profileLevel, w);

            if (c.Left > 0) w.Add("trailing_bytes", $"{c.Left}  {Helper.Hex(d.Slice(c.P), 16)}");
        }

        /// <summary>Leading fields of mpegh3daConfig() — everything up to SpeakerConfig3d().</summary>
        private static void ParseMpegh3daConfigHead(ReadOnlySpan<byte> cfg, byte recordProfileLevel, LineWriter w)
        {
            var r = new BitReader(cfg);

            int profileLevel = (int)r.U(8);
            int sfIndex = (int)r.U(5);
            int sampleRate = sfIndex == 0x1F ? (int)r.U(24) : UsacRateFromIndex(sfIndex);
            int frameLenIdx = (int)r.U(3);
            r.Skip(1);                               // cfg_reserved
            bool receiverDelayComp = r.Flag();

            if (r.Bad) { w.Add("parse", "config shorter than the mandatory header"); return; }

            w.Add("profile_level_indication", $"{MpeghProfileLevelName((byte)profileLevel)}  (0x{profileLevel:X2})");
            if (profileLevel != recordProfileLevel)
                w.Add("warning", $"mismatch: record says 0x{recordProfileLevel:X2}, bitstream says 0x{profileLevel:X2}");

            w.Add("usac_sampling_frequency", sfIndex == 0x1F
                ? $"{sampleRate} Hz (explicit)"
                : sampleRate > 0 ? $"{sampleRate} Hz (index {sfIndex})"
                                 : $"reserved index {sfIndex}");
            w.Add("core_sbr_frame_length_index", $"{frameLenIdx} -> {UsacFrameLengthName(frameLenIdx)}");
            w.Add("receiver_delay_compensation", receiverDelayComp ? 1 : 0);
            w.Add("note", "SpeakerConfig3d / FrameworkConfig3d / decoder configs not decoded " +
                          "(requires a full 23008-3 bitstream reader)");
        }

        /// <summary>ISO/IEC 23008-3 Table for mpegh3daProfileLevelIndication.</summary>
        internal static string MpeghProfileLevelName(byte v)
        {
            if (v == 0x00) return "reserved";
            if (v >= 0x01 && v <= 0x05) return $"Main profile, level {v}";
            if (v >= 0x06 && v <= 0x0A) return $"High profile, level {v - 0x05}";
            if (v >= 0x0B && v <= 0x0F) return $"Low Complexity (LC) profile, level {v - 0x0A}";
            if (v >= 0x10 && v <= 0x14) return $"Baseline profile, level {v - 0x0F}";
            return "unknown / reserved";
        }

        // ISO/IEC 23003-3 usacSamplingFrequencyIndex
        private static readonly int[] UsacSampleRates =
        {
            96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050,
            16000, 12000, 11025,  8000,  7350,     0,     0, 57600,
            51200, 40000, 38400, 34150, 28800, 25600, 20000, 19200,
            17075, 14400, 12800,  9600,     0,     0,     0,    -1
        };

        private static int UsacRateFromIndex(int i)
            => i >= 0 && i < UsacSampleRates.Length && UsacSampleRates[i] > 0 ? UsacSampleRates[i] : 0;

        // ISO/IEC 23003-3 Table 70 (coreCoderFrameLength / sbrRatioIndex).
        // NOTE: MPEG-H defines its own outputFrameLength column; verify against 23008-3 if you need it.
        private static string UsacFrameLengthName(int idx) => idx switch
        {
            0 => "coreCoderFrameLength 768, no SBR",
            1 => "coreCoderFrameLength 1024, no SBR",
            2 => "coreCoderFrameLength 1024, SBR 8:3",
            3 => "coreCoderFrameLength 2048, SBR 4:1",
            4 => "coreCoderFrameLength 2048, SBR 2:1",
            _ => "reserved"
        };

        /// <summary>
        /// ISO/IEC 23091-3 (ex-23001-8) ChannelConfiguration, a.k.a. CICP.
        /// Used by 'mhaC'.referenceChannelLayout, 'chnl'.definedLayout and DASH AudioChannelConfiguration.
        /// NOTE: values 1..7 keep the legacy MPEG-2 AAC ordering (centre first);
        ///       values >= 8 use the newer L R C LFE ... base ordering.
        ///       Verify exact orderings against your edition of 23091-3 Table 8 before relying on them.
        /// </summary>
        internal static string CicpChannelConfigName(int v) => v switch
        {
            0  => "any setup / defined in the bitstream",
            1  => "mono, 1ch — C",
            2  => "stereo, 2ch — L R",
            3  => "3.0, 3ch — C L R",
            4  => "4.0, 4ch — C L R Cs",
            5  => "5.0, 5ch — C L R Ls Rs",
            6  => "5.1, 6ch — C L R Ls Rs LFE",
            7  => "7.1 front-wide, 8ch — C Lc Rc L R Ls Rs LFE",
            8  => "dual channel / downmix stereo, 2ch",
            9  => "3.0 back, 3ch",
            10 => "quad, 4ch",
            11 => "6.1, 7ch",
            12 => "7.1, 8ch",
            13 => "22.2, 24ch",
            14 => "5.1.2, 8ch",
            _  => "see ISO/IEC 23091-3 Table 8"
        };

        /// <summary>Channel count of a CICP ChannelConfiguration, or -1 if unknown.</summary>
        internal static int CicpChannelCount(int v) => v switch
        {
            1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 => 5, 6 => 6, 7 => 8,
            8 => 2, 9 => 3, 10 => 4, 11 => 7, 12 => 8, 13 => 24, 14 => 8,
            _ => -1
        };

        // =====================================================================
        // 'pcmC' : PCMConfigBox (ISO/IEC 23003-5:2020)
        //   FullBox('pcmC', version = 0, flags = 0)
        //   uint8 format_flags      // bit0 = 1 -> little-endian, else big-endian
        //   uint8 PCM_sample_size   // in bits
        // =====================================================================
        public static void ParsePcmC(ReadOnlySpan<byte> d, LineWriter w, ParseCtx ctx = null)
        {
            var c = new Cur(d);

            byte version = c.U8();
            uint flags = c.U24();
            byte formatFlags = c.U8();
            byte sampleSize = c.U8();

            if (c.Bad) { w.Add("parse", $"truncated: need 6 bytes, got {d.Length}"); return; }

            if (version != 0 || flags != 0)
                w.Add("warning", $"23003-5 mandates version=0 flags=0, got version={version} flags=0x{flags:X6}");

            bool little = (formatFlags & 0x01) != 0;

            w.Add("format_flags", $"0x{formatFlags:X2}");
            w.Add("endianness", little ? "little-endian" : "big-endian");
            if ((formatFlags & 0xFE) != 0)
                w.Add("note", $"undefined bits set in format_flags: 0x{formatFlags & 0xFE:X2}");

            w.Add("pcm_sample_size", $"{sampleSize} bit");

            string entry = ctx?.EntryType;
            bool isFloat = entry == "fpcm";
            bool isInt = entry == "ipcm";

            if (isInt || isFloat)
            {
                w.Add("sample_format", isFloat
                    ? $"IEEE 754 floating point, {sampleSize} bit"
                    : $"two's complement signed integer, {sampleSize} bit");
                w.Add("equivalent_name", $"{(isFloat ? "f" : "s")}{sampleSize}{(little ? "le" : "be")}");

                bool ok = isFloat ? sampleSize is 32 or 64
                                  : sampleSize is 16 or 24 or 32 or 64;
                if (!ok) w.Add("warning", $"{entry} with PCM_sample_size={sampleSize} is not permitted by 23003-5");
            }
            else
            {
                w.Add("note", $"parent sample entry '{entry ?? "?"}' is not 'ipcm'/'fpcm'; " +
                              "integer vs float cannot be determined");
            }

            // This is the ONLY place the real bit depth lives: ISOBMFF pins
            // AudioSampleEntry.samplesize to the template value 16.
            if (ctx != null && ctx.SampleSize != sampleSize)
                w.Add("note", $"AudioSampleEntry.samplesize={ctx.SampleSize} (template value); " +
                              $"pcmC is authoritative at {sampleSize} bit");

            if (ctx != null && ctx.ChannelCount > 0 && sampleSize > 0 && ctx.SampleRate > 0)
            {
                double bps = ctx.SampleRate * ctx.ChannelCount * sampleSize;
                w.Add("uncompressed_bitrate", $"{bps / 1000.0:0.#} kbps " +
                                              $"({ctx.SampleRate:0.#} Hz x {ctx.ChannelCount}ch x {sampleSize} bit)");
                w.Add("bytes_per_frame", ctx.ChannelCount * ((sampleSize + 7) / 8));
            }

            if (c.Left > 0) w.Add("trailing_bytes", $"{c.Left}  {Helper.Hex(d.Slice(c.P), 16)}");
        }

        // =====================================================================
        // 'chnl' : ChannelLayoutBox  (ISO/IEC 14496-12 clause 12.2.4)
        //
        // version 0:
        //   uint8 stream_structure
        //   if (stream_structure & 1) {                  // channelStructured
        //       uint8 definedLayout
        //       if (definedLayout == 0)
        //           for (i = 0; i < channelcount; i++)   // <- from the sample entry!
        //               uint8 speaker_position
        //               if (speaker_position == 126) { int16 azimuth; int8 elevation; }
        //       else
        //           uint64 omittedChannelsMap
        //   }
        //   if (stream_structure & 2) uint8 object_count // objectStructured
        //
        // version 1:
        //   uint4 stream_structure; uint4 format_ordering; uint8 baseChannelCount
        //   if (stream_structure & 1) {
        //       uint8 definedLayout
        //       if (definedLayout == 0) {
        //           uint8 layout_channel_count            // <- self-contained, no ctx needed
        //           for (...) speaker_position [+ azimuth/elevation]
        //       } else {
        //           uint4 reserved; uint3 channel_order_definition; uint1 omitted_channels_present
        //           if (omitted_channels_present) uint64 omittedChannelsMap
        //       }
        //   }
        //   // object_count is DERIVED from baseChannelCount, not stored
        // =====================================================================
        public static void ParseChnl(ReadOnlySpan<byte> d, LineWriter w, ParseCtx ctx = null)
        {
            var c = new Cur(d);

            byte version = c.U8();
            uint flags = c.U24();

            if (c.Bad) { w.Add("parse", $"truncated: got {d.Length} bytes"); return; }

            w.Add("version", version == 1 ? "1 (preferred)" : version.ToString());
            if (flags != 0) w.Add("flags", $"0x{flags:X6} (expected 0)");

            if (ctx != null && ctx.Kind != SampleEntry.Kind.Audio)
                w.Add("warning", $"'chnl' found under a non-audio sample entry '{ctx.EntryType}'");

            switch (version)
            {
                case 0: ParseChnlV0(ref c, w, ctx); break;
                case 1: ParseChnlV1(ref c, w, ctx); break;
                default:
                    w.Add("parse", $"unknown version {version}; only 0 and 1 are defined");
                    w.Add("raw", Helper.Hex(d, 32));
                    return;
            }

            if (c.Bad) w.Add("parse", "truncated before the end of the box");
            else if (c.Left > 0) w.Add("trailing_bytes", $"{c.Left}  {Helper.Hex(d.Slice(c.P), 16)}");
        }

        private static void ParseChnlV0(ref Cur c, LineWriter w, ParseCtx ctx)
        {
            byte streamStructure = c.U8();
            w.Add("stream_structure", DescribeStreamStructure(streamStructure));

            if ((streamStructure & 0x01) != 0)                      // channelStructured
            {
                byte definedLayout = c.U8();

                if (definedLayout == 0)
                {
                    w.Add("defined_layout", "0 (custom layout — positions listed below)");

                    // v0 has no explicit count: it MUST be taken from AudioSampleEntry.channelcount.
                    int n = ctx?.ChannelCount ?? 0;
                    if (n <= 0)
                    {
                        w.Add("parse", "AudioSampleEntry.channelcount unavailable/zero -> cannot walk the speaker list");
                        w.Add("remaining", $"{c.Left} bytes  {Helper.Hex(c.D.Slice(c.P), 24)}");
                        c.Skip(c.Left);
                        return;
                    }

                    w.Add("channel_count_source", $"{n} (from AudioSampleEntry.channelcount)");
                    ParseSpeakerList(ref c, w, n);
                }
                else
                {
                    w.Add("defined_layout", $"{(CicpChannelCount(definedLayout) is int n2 && n2 > 0 ? CicpChannelConfigName(definedLayout) : CicpChannelConfigName(definedLayout))}  (CICP {definedLayout})");
                    ulong omitted = c.U64();
                    if (!c.Bad) DescribeOmittedChannels(omitted, definedLayout, w);
                }
            }

            if ((streamStructure & 0x02) != 0)                      // objectStructured
            {
                byte objectCount = c.U8();
                if (!c.Bad) w.Add("object_count", objectCount);
            }

            if (streamStructure == 0)
                w.Add("note", "neither channel- nor object-structured: renderer may choose freely");
        }

        private static void ParseChnlV1(ref Cur c, LineWriter w, ParseCtx ctx)
        {
            byte b0 = c.U8();
            int streamStructure = (b0 >> 4) & 0x0F;                 // high nibble
            int formatOrdering = b0 & 0x0F;                         // low nibble
            byte baseChannelCount = c.U8();

            if (c.Bad) { w.Add("parse", "truncated v1 header"); return; }

            w.Add("stream_structure", DescribeStreamStructure(streamStructure));
            w.Add("format_ordering", formatOrdering switch
            {
                0 => "unknown (0)",
                1 => "channels, possibly followed by objects (1)",
                2 => "objects, possibly followed by channels (2)",
                _ => $"reserved ({formatOrdering})"
            });
            w.Add("base_channel_count", $"{baseChannelCount} (total, for DRC)");

            int channelStructuredCount = -1;

            if ((streamStructure & 0x01) != 0)                      // channelStructured
            {
                byte definedLayout = c.U8();

                if (definedLayout == 0)
                {
                    byte layoutChannelCount = c.U8();
                    if (c.Bad) { w.Add("parse", "truncated before layout_channel_count"); return; }

                    w.Add("defined_layout", "0 (custom layout — positions listed below)");
                    w.Add("layout_channel_count", layoutChannelCount);
                    channelStructuredCount = layoutChannelCount;

                    if (ctx != null && ctx.ChannelCount > 0 && layoutChannelCount > ctx.ChannelCount)
                        w.Add("warning", $"layout_channel_count {layoutChannelCount} exceeds " +
                                         $"AudioSampleEntry.channelcount {ctx.ChannelCount}");

                    ParseSpeakerList(ref c, w, layoutChannelCount);
                }
                else
                {
                    w.Add("defined_layout", $"{CicpChannelConfigName(definedLayout)}  (CICP {definedLayout})");

                    byte b1 = c.U8();
                    int reserved = (b1 >> 4) & 0x0F;
                    int orderDef = (b1 >> 1) & 0x07;
                    bool omittedPresent = (b1 & 0x01) != 0;

                    if (c.Bad) { w.Add("parse", "truncated before channel_order_definition"); return; }
                    if (reserved != 0) w.Add("note", $"reserved nibble = 0x{reserved:X} (expected 0)");

                    w.Add("channel_order_definition", orderDef switch
                    {
                        0 => "0 — as listed for ChannelConfigurations in ISO/IEC 23091-3",
                        1 => "1 — default order of the audio codec specification",
                        2 => "2 — channel ordering #2 of the audio codec specification",
                        3 => "3 — channel ordering #3 of the audio codec specification",
                        4 => "4 — channel ordering #4 of the audio codec specification",
                        _ => $"{orderDef} — reserved"
                    });

                    int layoutTotal = CicpChannelCount(definedLayout);

                    if (omittedPresent)
                    {
                        ulong omitted = c.U64();
                        if (c.Bad) { w.Add("parse", "truncated omittedChannelsMap"); return; }
                        int omittedCount = DescribeOmittedChannels(omitted, definedLayout, w);
                        if (layoutTotal > 0) channelStructuredCount = layoutTotal - omittedCount;
                    }
                    else
                    {
                        w.Add("omitted_channels_map", "absent -> the track carries the complete layout");
                        channelStructuredCount = layoutTotal;
                    }
                }
            }
            else
            {
                channelStructuredCount = 0;
            }

            if ((streamStructure & 0x02) != 0)                      // objectStructured
            {
                if (channelStructuredCount >= 0)
                    w.Add("object_count", $"{Math.Max(0, baseChannelCount - channelStructuredCount)} " +
                                          $"(derived: baseChannelCount {baseChannelCount} - " +
                                          $"{channelStructuredCount} channel-structured)");
                else
                    w.Add("object_count", "not derivable (channel-structured count unknown)");
            }

            if (streamStructure == 0)
                w.Add("note", "neither channel- nor object-structured: renderer may choose freely");
            if ((streamStructure & 0x0C) != 0)
                w.Add("note", $"reserved bits set in stream_structure: 0x{streamStructure & 0x0C:X}");
        }

        // ---------------- shared helpers ----------------

        private static string DescribeStreamStructure(int s)
        {
            var parts = new List<string>();
            if ((s & 0x01) != 0) parts.Add("channelStructured");
            if ((s & 0x02) != 0) parts.Add("objectStructured");
            if ((s & ~0x03) != 0) parts.Add($"reserved(0x{s & ~0x03:X})");
            return $"0x{s:X2}" + (parts.Count > 0 ? $"  ({string.Join(" | ", parts)})" : "  (neither)");
        }

        private static void ParseSpeakerList(ref Cur c, LineWriter w, int count)
        {
            var shortNames = new List<string>(count);

            for (int i = 0; i < count; i++)
            {
                byte pos = c.U8();
                if (c.Bad) { w.Add("parse", $"truncated in the speaker list at channel[{i}]"); return; }

                if (pos == 126)                                     // explicit position
                {
                    short azimuth = c.S16();
                    sbyte elevation = unchecked((sbyte)c.U8());
                    if (c.Bad) { w.Add("parse", $"truncated explicit position at channel[{i}]"); return; }

                    w.Add($"channel[{i}]", $"explicit: azimuth {azimuth}°, elevation {elevation}° (speaker_position=126)");
                    shortNames.Add($"[{azimuth}/{elevation}]");

                    if (azimuth < -180 || azimuth > 180)
                        w.Add($"channel[{i}].warning", $"azimuth {azimuth}° outside the usual -180..180 range");
                    if (elevation < -90 || elevation > 90)
                        w.Add($"channel[{i}].warning", $"elevation {elevation}° outside the usual -90..90 range");
                }
                else
                {
                    var (abbr, name) = OutputChannelPosition(pos);
                    w.Add($"channel[{i}]", $"{abbr} — {name}  (position {pos})");
                    shortNames.Add(abbr);
                }
            }

            if (shortNames.Count > 0)
                w.Add("layout_summary", string.Join(" ", shortNames));
        }

        /// <summary>omittedChannelsMap: a '1' bit means the channel is NOT present in this track.</summary>
        private static int DescribeOmittedChannels(ulong map, int definedLayout, LineWriter w)
        {
            int count = BitOperations.PopCount(map);

            w.Add("omitted_channels_map", $"0x{map:X16}");
            if (map == 0)
            {
                w.Add("omitted_channels", "none — the track carries the complete layout");
                return 0;
            }

            // Bit-to-channel mapping: the spec numbers channels from the start of the
            // defined layout. We report MSB-first (channel 1 = bit 63), which matches
            // ISOBMFF bit-field order, and also print the raw popcount which is
            // convention-independent. Verify against your spec edition if it matters.
            var idx = new List<string>();
            for (int i = 0; i < 64; i++)
                if ((map & (1UL << (63 - i))) != 0) idx.Add((i + 1).ToString());

            w.Add("omitted_channels", $"{count} channel(s) omitted; indices (MSB-first): {string.Join(", ", idx)}");

            int total = CicpChannelCount(definedLayout);
            if (total > 0)
                w.Add("channels_in_this_track", $"{Math.Max(0, total - count)} of {total}");

            return count;
        }

        /// <summary>
        /// OutputChannelPosition, ISO/IEC 23091-3 clause 6.1 Table 7.
        /// Values 0..29 are the widely-deployed set; higher values were added in later
        /// editions/amendments — verify before relying on them.
        /// </summary>
        internal static (string Abbr, string Name) OutputChannelPosition(int p) => p switch
        {
            0  => ("L",    "left front"),
            1  => ("R",    "right front"),
            2  => ("C",    "centre front"),
            3  => ("LFE1", "low frequency enhancement 1"),
            4  => ("Ls",   "left surround"),
            5  => ("Rs",   "right surround"),
            6  => ("Lc",   "left front centre"),
            7  => ("Rc",   "right front centre"),
            8  => ("Lsr",  "left surround rear"),
            9  => ("Rsr",  "right surround rear"),
            10 => ("Cs",   "rear centre"),
            11 => ("Lsd",  "left surround direct"),
            12 => ("Rsd",  "right surround direct"),
            13 => ("Lss",  "left side surround"),
            14 => ("Rss",  "right side surround"),
            15 => ("Lw",   "left wide front"),
            16 => ("Rw",   "right wide front"),
            17 => ("Lv",   "left front vertical height"),
            18 => ("Rv",   "right front vertical height"),
            19 => ("Cv",   "centre front vertical height"),
            20 => ("Lvr",  "left surround vertical height rear"),
            21 => ("Rvr",  "right surround vertical height rear"),
            22 => ("Cvr",  "centre vertical height rear"),
            23 => ("Lvss", "left vertical height side surround"),
            24 => ("Rvss", "right vertical height side surround"),
            25 => ("Ts",   "top centre surround"),
            26 => ("LFE2", "low frequency enhancement 2"),
            27 => ("Lb",   "left front vertical bottom"),
            28 => ("Rb",   "right front vertical bottom"),
            29 => ("Cb",   "centre front vertical bottom"),
            126 => ("expl", "explicit position (azimuth/elevation follow)"),
            127 => ("unk",  "unknown / undefined"),
            _   => ($"pos{p}", "see ISO/IEC 23091-3 Table 7")
        };
    }
}