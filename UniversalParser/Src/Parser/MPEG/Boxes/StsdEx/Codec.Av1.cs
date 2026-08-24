using System;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    internal static partial class Codec
    {
        /// <summary>Values carried out of the Sequence Header OBU for the RFC 6381 codec string.</summary>
        private sealed class Av1SeqInfo
        {
            public bool Parsed;
            public int BitDepth = 8;
            public bool Monochrome;
            public int SubsamplingX = 1, SubsamplingY = 1, ChromaSamplePosition;
            public int ColourPrimaries = 1, TransferCharacteristics = 1, MatrixCoefficients = 1;
            public bool FullRange;
        }

        // =====================================================================
        // av1C : AV1CodecConfigurationRecord (AV1 ISOBMFF binding §2.3)
        // =====================================================================
        public static void ParseAv1C(ReadOnlySpan<byte> d, LineWriter w)
        {
            var c = new Cur(d);

            byte b0 = c.U8();
            bool marker = (b0 & 0x80) != 0;
            int version = b0 & 0x7F;

            byte b1 = c.U8();
            int seqProfile   = (b1 >> 5) & 0x07;
            int seqLevelIdx0 = b1 & 0x1F;

            byte b2 = c.U8();
            bool seqTier0     = (b2 & 0x80) != 0;
            bool highBitdepth = (b2 & 0x40) != 0;
            bool twelveBit    = (b2 & 0x20) != 0;
            bool monochrome   = (b2 & 0x10) != 0;
            int  ssX          = (b2 & 0x08) != 0 ? 1 : 0;
            int  ssY          = (b2 & 0x04) != 0 ? 1 : 0;
            int  chromaPos    = b2 & 0x03;

            byte b3 = c.U8();
            bool ipdPresent = (b3 & 0x10) != 0;
            int  ipdMinus1  = b3 & 0x0F;

            if (c.Bad) { w.Add("parse", "av1C shorter than 4 bytes"); return; }

            int bitDepth = (seqProfile == 2 && highBitdepth) ? (twelveBit ? 12 : 10)
                                                            : (highBitdepth ? 10 : 8);

            if (!marker) w.Add("marker", "0 (INVALID, must be 1)");
            w.Add("version", version + (version != 1 ? " (expected 1)" : ""));
            w.Add("seq_profile", $"{Av1ProfileName(seqProfile)} ({seqProfile})");
            w.Add("seq_level_idx_0", Av1LevelName(seqLevelIdx0));
            w.Add("seq_tier_0", seqTier0 ? "1 (High)" : "0 (Main)");
            w.Add("bit_depth", $"{bitDepth} (high_bitdepth={(highBitdepth ? 1 : 0)}, twelve_bit={(twelveBit ? 1 : 0)})");
            w.Add("monochrome", monochrome ? 1 : 0);
            w.Add("chroma_format", Av1ChromaName(monochrome, ssX, ssY));
            w.Add("chroma_sample_position", chromaPos switch
            {
                0 => "unknown (0)",
                1 => "vertical / left-aligned (1)",
                2 => "colocated (2)",
                _ => "reserved (3)"
            });
            w.Add("initial_presentation_delay",
                ipdPresent ? $"{ipdMinus1 + 1} sample(s)" : "not present");

            var seq = new Av1SeqInfo
            {
                BitDepth = bitDepth,
                Monochrome = monochrome,
                SubsamplingX = ssX,
                SubsamplingY = ssY,
                ChromaSamplePosition = chromaPos
            };

            w.Add("config_obus_size", c.Left);
            if (c.Left > 0)
            {
                using (w.Push("configOBUs"))
                    WalkObus(ref c, w, seq);
            }
            else
            {
                w.Add("note", "no configOBUs (Sequence Header OBU should normally be present)");
            }

            w.Add("codec_string", Av1CodecString(seqProfile, seqLevelIdx0, seqTier0, seq));
        }

        // =====================================================================
        // OBU stream inside configOBUs
        // =====================================================================
        private static void WalkObus(ref Cur c, LineWriter w, Av1SeqInfo seq)
        {
            for (int i = 0; c.Left >= 1 && !c.Bad; i++)
            {
                if (i > 32) { w.Add("parse", "too many OBUs, stop"); return; }

                byte h = c.U8();
                bool forbidden = (h & 0x80) != 0;
                int  obuType   = (h >> 3) & 0x0F;
                bool extFlag   = (h & 0x04) != 0;
                bool hasSize   = (h & 0x02) != 0;

                if (forbidden) { w.Add("parse", $"obu[{i}] forbidden_bit set -> not an OBU stream"); return; }

                int temporalId = 0, spatialId = 0;
                if (extFlag)
                {
                    byte e = c.U8();
                    temporalId = (e >> 5) & 0x07;
                    spatialId  = (e >> 3) & 0x03;
                }

                long len = hasSize ? ReadLeb128(ref c) : c.Left;   // no size field -> extends to end
                if (c.Bad || len < 0 || len > c.Left)
                {
                    w.Add("parse", $"obu[{i}] invalid obu_size {len} (only {c.Left} left)");
                    return;
                }

                var payload = c.Bytes((int)len);

                using (w.Push($"obu[{i}]"))
                {
                    w.Add("obu_type", $"{Av1ObuName(obuType)} ({obuType})");
                    w.Add("obu_size", len);
                    if (extFlag) w.Add("layer", $"temporal_id={temporalId} spatial_id={spatialId}");

                    switch (obuType)
                    {
                        case 1: ParseAv1SequenceHeader(payload, w, seq); break;   // OBU_SEQUENCE_HEADER
                        case 5: ParseAv1Metadata(payload, w); break;              // OBU_METADATA
                        default: w.Add("raw", Helper.Hex(payload, 24)); break;
                    }
                }
            }
        }

        // =====================================================================
        // sequence_header_obu() — AV1 spec §5.5.1
        // =====================================================================
        private static void ParseAv1SequenceHeader(ReadOnlySpan<byte> d, LineWriter w, Av1SeqInfo seq)
        {
            var r = new BitReader(d);

            int seqProfile     = (int)r.U(3);
            bool stillPicture  = r.Flag();
            bool reducedStill  = r.Flag();

            w.Add("seq_profile", $"{Av1ProfileName(seqProfile)} ({seqProfile})");
            w.Add("still_picture", stillPicture ? 1 : 0);
            w.Add("reduced_still_picture_header", reducedStill ? 1 : 0);

            int bufferDelayLen = 0;
            bool decoderModelPresent = false, initialDisplayDelayPresent = false;

            if (reducedStill)
            {
                w.Add("seq_level_idx[0]", Av1LevelName((int)r.U(5)));
            }
            else
            {
                if (r.Flag())                                   // timing_info_present_flag
                {
                    uint numUnits  = r.U(32);
                    uint timeScale = r.U(32);
                    bool equalInterval = r.Flag();
                    ulong ticksPerPic = 1;
                    if (equalInterval) ticksPerPic = ReadUvlc(ref r) + 1;

                    w.Add("timing_info", $"time_scale={timeScale}, num_units_in_display_tick={numUnits}" +
                                         (equalInterval ? $", ticks_per_picture={ticksPerPic}" : ", variable interval"));
                    if (numUnits > 0 && ticksPerPic > 0 && !r.Bad)
                        w.Add("frame_rate", $"{timeScale / (double)numUnits / ticksPerPic:0.###} fps");

                    decoderModelPresent = r.Flag();
                    if (decoderModelPresent)
                    {
                        bufferDelayLen = (int)r.U(5) + 1;
                        r.Skip(32);                             // num_units_in_decoding_tick
                        r.Skip(5);                              // buffer_removal_time_length_minus_1
                        r.Skip(5);                              // frame_presentation_time_length_minus_1
                        w.Add("decoder_model_info", $"buffer_delay_length={bufferDelayLen}");
                    }
                }

                initialDisplayDelayPresent = r.Flag();
                int opCount = (int)r.U(5) + 1;
                w.Add("operating_points_cnt", opCount);

                for (int i = 0; i < opCount && !r.Bad; i++)
                {
                    uint idc = r.U(12);
                    int lvl = (int)r.U(5);
                    bool tier = lvl > 7 && r.Flag();            // seq_tier only coded when level > 7

                    w.Add($"operating_point[{i}]",
                        $"idc=0x{idc:X3}, level={Av1LevelName(lvl)}, tier={(tier ? "High" : "Main")}");

                    if (decoderModelPresent && r.Flag())
                    {
                        r.Skip(bufferDelayLen);                 // decoder_buffer_delay
                        r.Skip(bufferDelayLen);                 // encoder_buffer_delay
                        r.Skip(1);                              // low_delay_mode_flag
                    }
                    if (initialDisplayDelayPresent && r.Flag())
                        w.Add($"operating_point[{i}].initial_display_delay", r.U(4) + 1);
                }
            }

            int fwBits = (int)r.U(4) + 1;
            int fhBits = (int)r.U(4) + 1;
            long maxW = r.U(fwBits) + 1L;
            long maxH = r.U(fhBits) + 1L;
            w.Add("max_frame_size", $"{maxW} x {maxH}");

            if (!reducedStill && r.Flag())                      // frame_id_numbers_present_flag
            {
                r.Skip(4);                                      // delta_frame_id_length_minus_2
                r.Skip(3);                                      // additional_frame_id_length_minus_1
                w.Add("frame_id_numbers_present", 1);
            }

            bool sb128 = r.Flag();
            bool filterIntra = r.Flag();
            bool intraEdgeFilter = r.Flag();

            bool enableOrderHint = false;
            int orderHintBits = 0;

            if (!reducedStill)
            {
                bool interIntra = r.Flag();
                bool maskedComp = r.Flag();
                bool warpedMotion = r.Flag();
                bool dualFilter = r.Flag();
                enableOrderHint = r.Flag();
                if (enableOrderHint) { r.Skip(1); r.Skip(1); }  // enable_jnt_comp, enable_ref_frame_mvs

                int forceScreenTools = r.Flag() ? 2 : (int)r.U(1);
                if (forceScreenTools > 0 && !r.Flag()) r.Skip(1);   // seq_choose/force_integer_mv
                if (enableOrderHint) orderHintBits = (int)r.U(3) + 1;

                w.Add("coding_tools",
                    $"128x128_superblock={(sb128 ? 1 : 0)}, filter_intra={(filterIntra ? 1 : 0)}, " +
                    $"intra_edge_filter={(intraEdgeFilter ? 1 : 0)}, interintra_compound={(interIntra ? 1 : 0)}, " +
                    $"masked_compound={(maskedComp ? 1 : 0)}, warped_motion={(warpedMotion ? 1 : 0)}, " +
                    $"dual_filter={(dualFilter ? 1 : 0)}, order_hint={(enableOrderHint ? 1 : 0)}");
                if (orderHintBits > 0) w.Add("order_hint_bits", orderHintBits);
            }

            bool superres = r.Flag();
            bool cdef = r.Flag();
            bool restoration = r.Flag();
            w.Add("post_filters", $"superres={(superres ? 1 : 0)}, cdef={(cdef ? 1 : 0)}, restoration={(restoration ? 1 : 0)}");

            ParseAv1ColorConfig(ref r, seqProfile, w, seq);

            w.Add("film_grain_params_present", r.Flag() ? 1 : 0);

            if (r.Bad) w.Add("parse", "sequence header truncated");
            else seq.Parsed = true;
        }

        /// <summary>color_config() — AV1 spec §5.5.2</summary>
        private static void ParseAv1ColorConfig(ref BitReader r, int seqProfile, LineWriter w, Av1SeqInfo seq)
        {
            bool highBitdepth = r.Flag();
            int bitDepth;
            if (seqProfile == 2 && highBitdepth) bitDepth = r.Flag() ? 12 : 10;
            else bitDepth = highBitdepth ? 10 : 8;

            bool mono = seqProfile != 1 && r.Flag();            // profile 1 is always 4:4:4 colour

            int cp = 2, tc = 2, mc = 2;                         // *_UNSPECIFIED
            bool descPresent = r.Flag();
            if (descPresent) { cp = (int)r.U(8); tc = (int)r.U(8); mc = (int)r.U(8); }

            int ssX, ssY, csp = 0;
            bool fullRange;

            if (mono)
            {
                fullRange = r.Flag();
                ssX = 1; ssY = 1; csp = 0;
                // separate_uv_delta_q is NOT coded in this branch
            }
            else if (cp == 1 && tc == 13 && mc == 0)            // BT.709 + sRGB + Identity => implicit sRGB
            {
                fullRange = true; ssX = 0; ssY = 0;
            }
            else
            {
                fullRange = r.Flag();
                if (seqProfile == 0) { ssX = 1; ssY = 1; }
                else if (seqProfile == 1) { ssX = 0; ssY = 0; }
                else if (bitDepth == 12)
                {
                    ssX = (int)r.U(1);
                    ssY = ssX == 1 ? (int)r.U(1) : 0;
                }
                else { ssX = 1; ssY = 0; }

                if (ssX == 1 && ssY == 1) csp = (int)r.U(2);
                r.Skip(1);                                      // separate_uv_delta_q
            }

            w.Add("color.bit_depth", bitDepth);
            w.Add("color.monochrome", mono ? 1 : 0);
            w.Add("color.chroma_format", Av1ChromaName(mono, ssX, ssY));
            w.Add("color.range", fullRange ? "full" : "limited/studio");
            w.Add("color.description", descPresent
                ? $"primaries={cp}, transfer={tc}, matrix={mc}"
                : $"not present (defaults: {cp}/{tc}/{mc} = unspecified)");

            seq.BitDepth = bitDepth;
            seq.Monochrome = mono;
            seq.SubsamplingX = ssX;
            seq.SubsamplingY = ssY;
            seq.ChromaSamplePosition = csp;
            seq.ColourPrimaries = cp;
            seq.TransferCharacteristics = tc;
            seq.MatrixCoefficients = mc;
            seq.FullRange = fullRange;
        }

        /// <summary>metadata_obu() — only the simple HDR payloads are decoded.</summary>
        private static void ParseAv1Metadata(ReadOnlySpan<byte> d, LineWriter w)
        {
            var c = new Cur(d);
            long type = ReadLeb128(ref c);
            w.Add("metadata_type", type switch
            {
                1 => "HDR_CLL (1)",
                2 => "HDR_MDCV (2)",
                3 => "SCALABILITY (3)",
                4 => "ITUT_T35 (4)",
                5 => "TIMECODE (5)",
                _ => $"unknown ({type})"
            });

            var r = new BitReader(c.D.Slice(Math.Min(c.P, c.D.Length)));

            if (type == 1)
            {
                w.Add("max_cll", $"{r.U(16)} cd/m^2");
                w.Add("max_fall", $"{r.U(16)} cd/m^2");
            }
            else if (type == 2)
            {
                for (int i = 0; i < 3; i++)
                    w.Add($"primary[{i}]", $"x={r.U(16) * 0.00002:0.#####}, y={r.U(16) * 0.00002:0.#####}");
                w.Add("white_point", $"x={r.U(16) * 0.00002:0.#####}, y={r.U(16) * 0.00002:0.#####}");
                w.Add("luminance_max", $"{r.U(32) * 0.1:0.#} cd/m^2");
                w.Add("luminance_min", $"{r.U(32) * 0.0001:0.####} cd/m^2");
            }
            else
            {
                w.Add("raw", Helper.Hex(d, 24));
            }
            if (r.Bad) w.Add("parse", "metadata truncated");
        }

        // =====================================================================
        // helpers
        // =====================================================================

        /// <summary>leb128() — AV1 spec §4.10.5, at most 8 bytes.</summary>
        private static long ReadLeb128(ref Cur c)
        {
            long value = 0;
            for (int i = 0; i < 8; i++)
            {
                byte b = c.U8();
                if (c.Bad) return -1;
                value |= (long)(b & 0x7F) << (i * 7);
                if ((b & 0x80) == 0) return value;
            }
            return value;
        }

        /// <summary>uvlc() — AV1 spec §4.10.3.</summary>
        private static ulong ReadUvlc(ref BitReader r)
        {
            int leadingZeros = 0;
            while (true)
            {
                if (r.BitsLeft == 0) return 0;
                if (r.Flag()) break;
                if (++leadingZeros >= 32) return uint.MaxValue;
            }
            if (leadingZeros == 0) return 0;
            return r.U(leadingZeros) + ((1UL << leadingZeros) - 1);
        }

        /// <summary>RFC 6381 / AV1 ISOBMFF §5: av01.P.LLT.DD[.M.CCC.cp.tc.mc.F]</summary>
        private static string Av1CodecString(int profile, int levelIdx, bool tier, Av1SeqInfo s)
        {
            var sb = new StringBuilder("av01.");
            sb.Append(profile).Append('.');
            sb.Append(levelIdx.ToString("D2")).Append(tier ? 'H' : 'M').Append('.');
            sb.Append(s.BitDepth.ToString("D2"));
            sb.Append('.').Append(s.Monochrome ? 1 : 0);
            sb.Append('.').Append(s.SubsamplingX).Append(s.SubsamplingY).Append(s.ChromaSamplePosition);
            if (s.Parsed)
            {
                sb.Append('.').Append(s.ColourPrimaries.ToString("D2"));
                sb.Append('.').Append(s.TransferCharacteristics.ToString("D2"));
                sb.Append('.').Append(s.MatrixCoefficients.ToString("D2"));
                sb.Append('.').Append(s.FullRange ? 1 : 0);
            }
            return sb.ToString();
        }

        private static string Av1ProfileName(int p) => p switch
        {
            0 => "Main",
            1 => "High",
            2 => "Professional",
            _ => "reserved"
        };

        /// <summary>seq_level_idx -> X.Y where X = 2 + (idx >> 2), Y = idx &amp; 3.</summary>
        private static string Av1LevelName(int idx)
        {
            if (idx == 31) return "maximum parameters (31)";
            int major = 2 + (idx >> 2);
            int minor = idx & 3;
            bool defined = idx switch
            {
                0 or 1 or 4 or 5 or 8 or 9 => true,             // 2.0 2.1 3.0 3.1 4.0 4.1
                >= 12 and <= 19 => true,                        // 5.0-5.3, 6.0-6.3
                _ => false
            };
            return $"{major}.{minor} (idx {idx}){(defined ? "" : " [reserved]")}";
        }

        private static string Av1ChromaName(bool mono, int ssX, int ssY)
        {
            if (mono) return "monochrome / 4:0:0";
            if (ssX == 1 && ssY == 1) return "4:2:0";
            if (ssX == 1 && ssY == 0) return "4:2:2";
            if (ssX == 0 && ssY == 0) return "4:4:4";
            return $"unknown (x={ssX}, y={ssY})";
        }

        private static string Av1ObuName(int t) => t switch
        {
            1  => "OBU_SEQUENCE_HEADER",
            2  => "OBU_TEMPORAL_DELIMITER",
            3  => "OBU_FRAME_HEADER",
            4  => "OBU_TILE_GROUP",
            5  => "OBU_METADATA",
            6  => "OBU_FRAME",
            7  => "OBU_REDUNDANT_FRAME_HEADER",
            8  => "OBU_TILE_LIST",
            15 => "OBU_PADDING",
            _  => "reserved"
        };
    }
}