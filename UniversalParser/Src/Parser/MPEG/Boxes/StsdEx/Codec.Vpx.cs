using System;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    internal static partial class Codec
    {
        // =====================================================================
        // vpcC : VPCodecConfigurationBox (FullBox) - VP Codec ISOBMFF Binding
        //
        // WARNING: version 0 and version 1 have DIFFERENT field layouts.
        //   v0: profile(8) level(8) bitDepth(4) colorSpace(4) chromaSubsampling(4)
        //       transferFunction(3) videoFullRangeFlag(1) codecInitDataSize(16)
        //   v1: profile(8) level(8) bitDepth(4) chromaSubsampling(3) fullRange(1)
        //       colourPrimaries(8) transferCharacteristics(8) matrixCoefficients(8)
        //       codecInitDataSize(16)
        // v0 is deprecated but still exists in the wild, so we branch on version.
        // =====================================================================
        public static void ParseVpcC(ReadOnlySpan<byte> d, LineWriter w, string entryType = null)
        {
            var c = new Cur(d);
            byte version = c.U8();
            uint flags = c.U24();

            w.Add("version", version + (version == 0 ? " (DEPRECATED layout)" : ""));
            if (flags != 0) w.Add("flags", $"0x{flags:X6}");

            int minBody = version == 0 ? 6 : 8;
            if (c.Left < minBody)
            {
                w.Add("parse", $"version {version} needs {minBody} payload bytes, only {c.Left} left");
                w.Add("raw", Helper.Hex(d, 16));
                return;
            }

            var r = new BitReader(c.D.Slice(c.P));

            int profile = (int)r.U(8);
            int level = (int)r.U(8);
            int bitDepth = (int)r.U(4);

            int chroma, fullRange;
            int cp = -1, tc = -1, mc = -1;
            int colorSpace = -1, transferFunc = -1;

            if (version == 0)
            {
                colorSpace = (int)r.U(4);
                chroma = (int)r.U(4);
                transferFunc = (int)r.U(3);
                fullRange = (int)r.U(1);
            }
            else
            {
                chroma = (int)r.U(3);
                fullRange = (int)r.U(1);
                cp = (int)r.U(8);
                tc = (int)r.U(8);
                mc = (int)r.U(8);
            }

            int initDataSize = (int)r.U(16);

            string fourcc = entryType == "vp08" ? "vp08" : "vp09";

            w.Add("profile", $"{VpxProfileName(profile)} ({profile})");
            w.Add("level", VpxLevelName(level));
            w.Add("bit_depth", bitDepth is 8 or 10 or 12 ? $"{bitDepth}" : $"{bitDepth} (invalid, expected 8/10/12)");
            w.Add("chroma_subsampling", VpxChromaName(chroma));
            w.Add("video_full_range_flag", fullRange == 1 ? "1 (full range)" : "0 (legal/studio range)");

            if (version == 0)
            {
                w.Add("color_space", $"{VpxColorSpaceName(colorSpace)} ({colorSpace})");
                w.Add("transfer_function", transferFunc switch
                {
                    0 => "BT.709 / BT.601 / BT.2020 (0)",
                    1 => "SMPTE ST 2084 / PQ (1)",
                    _ => $"reserved ({transferFunc})"
                });
                // v0 has no separate cp/tc/mc, so we deliberately do NOT fabricate them.
                w.Add("codec_string", $"{fourcc}.{profile:D2}.{level:D2}.{bitDepth:D2}");
                w.Add("note", "version 0 record: colourPrimaries / transferCharacteristics / "
                            + "matrixCoefficients are not available, codec string is truncated");
            }
            else
            {
                w.Add("colour_primaries", $"{ColourPrimariesName(cp)} ({cp})");
                w.Add("transfer_characteristics", $"{TransferCharacteristicsName(tc)} ({tc})");
                w.Add("matrix_coefficients", $"{MatrixCoefficientsName(mc)} ({mc})");
                w.Add("codec_string",
                    $"{fourcc}.{profile:D2}.{level:D2}.{bitDepth:D2}.{chroma:D2}.{cp:D2}.{tc:D2}.{mc:D2}.{fullRange:D2}");
            }

            w.Add("codec_initialization_data_size", initDataSize);
            if (initDataSize != 0)
                w.Add("note", "codecInitializationDataSize must be 0 for VP8 and VP9");

            // ---- consistency checks against the profile constraints ----
            bool wantHighBitDepth = profile is 2 or 3;
            bool want420 = profile is 0 or 2;

            if (wantHighBitDepth && bitDepth == 8)
                w.Add("note", $"profile {profile} requires 10 or 12 bit, got 8");
            if (!wantHighBitDepth && bitDepth != 8)
                w.Add("note", $"profile {profile} is 8-bit only, got {bitDepth}");
            if (want420 && chroma > 1)
                w.Add("note", $"profile {profile} is 4:2:0 only, got {VpxChromaName(chroma)}");
            if (!want420 && chroma <= 1)
                w.Add("note", $"profile {profile} does not allow 4:2:0, got {VpxChromaName(chroma)}");
            if (version == 1 && mc == 0 && chroma != 3)
                w.Add("note", "matrixCoefficients=0 (RGB) requires chromaSubsampling=3 (4:4:4)");
        }

        // =====================================================================
        // SmDm / CoLL : HDR metadata boxes used alongside vpcC
        // (WebM/VP9 binding; the ISO equivalents are 'mdcv' and 'clli')
        // =====================================================================
        public static void ParseSmDm(ReadOnlySpan<byte> d, LineWriter w)
        {
            var c = new Cur(d);
            c.Skip(4);                                  // version + flags
            if (c.Left < 24) { w.Add("parse", $"SmDm needs 24 payload bytes, got {c.Left}"); return; }

            const double Chroma = 0.00002, Lum = 0.0001;
            string Pt(ref Cur cc) => $"x={cc.U16() * Chroma:0.#####}, y={cc.U16() * Chroma:0.#####}";

            w.Add("primary_r", Pt(ref c));
            w.Add("primary_g", Pt(ref c));
            w.Add("primary_b", Pt(ref c));
            w.Add("white_point", Pt(ref c));
            w.Add("luminance_max", $"{c.U32() * Lum:0.##} cd/m^2");
            w.Add("luminance_min", $"{c.U32() * Lum:0.####} cd/m^2");
        }

        public static void ParseCoLL(ReadOnlySpan<byte> d, LineWriter w)
        {
            var c = new Cur(d);
            c.Skip(4);                                  // version + flags
            if (c.Left < 4) { w.Add("parse", $"CoLL needs 4 payload bytes, got {c.Left}"); return; }
            w.Add("max_cll", $"{c.U16()} cd/m^2");
            w.Add("max_fall", $"{c.U16()} cd/m^2");
        }

        // =====================================================================
        // tables
        // =====================================================================

        private static string VpxProfileName(int p) => p switch
        {
            0 => "8-bit 4:2:0",
            1 => "8-bit 4:2:2 / 4:4:4",
            2 => "10/12-bit 4:2:0",
            3 => "10/12-bit 4:2:2 / 4:4:4",
            _ => "unknown"
        };

        private static string VpxLevelName(int l) => l switch
        {
            0 => "0 (undefined)",
            10 => "1 (10)",   11 => "1.1 (11)",
            20 => "2 (20)",   21 => "2.1 (21)",
            30 => "3 (30)",   31 => "3.1 (31)",
            40 => "4 (40)",   41 => "4.1 (41)",
            50 => "5 (50)",   51 => "5.1 (51)",  52 => "5.2 (52)",
            60 => "6 (60)",   61 => "6.1 (61)",  62 => "6.2 (62)",
            _ => $"{l} (reserved)"
        };

        private static string VpxChromaName(int cs) => cs switch
        {
            0 => "4:2:0 vertical (0)",
            1 => "4:2:0 colocated with luma (1)",
            2 => "4:2:2 (2)",
            3 => "4:4:4 (3)",
            _ => $"reserved ({cs})"
        };

        /// <summary>VP9 private colour space enum, only used by vpcC version 0.</summary>
        private static string VpxColorSpaceName(int cs) => cs switch
        {
            0 => "Unknown",
            1 => "BT.601",
            2 => "BT.709",
            3 => "SMPTE 170M",
            4 => "SMPTE 240M",
            5 => "BT.2020",
            6 => "Reserved",
            7 => "sRGB",
            _ => "invalid"
        };

        // ---- ISO/IEC 23001-8 tables, shared with colr / AV1 / HEVC VUI ----
        internal static string ColourPrimariesName(int v) => v switch
        {
            1 => "BT.709",
            2 => "unspecified",
            4 => "BT.470M",
            5 => "BT.470BG / BT.601-625",
            6 => "SMPTE 170M / BT.601-525",
            7 => "SMPTE 240M",
            8 => "Generic film",
            9 => "BT.2020 / BT.2100",
            10 => "SMPTE ST 428 (XYZ)",
            11 => "SMPTE RP 431 (DCI-P3)",
            12 => "SMPTE EG 432 (Display P3)",
            22 => "EBU Tech 3213-E",
            _ => "reserved"
        };

        internal static string TransferCharacteristicsName(int v) => v switch
        {
            1 => "BT.709",
            2 => "unspecified",
            4 => "gamma 2.2",
            5 => "gamma 2.8",
            6 => "SMPTE 170M",
            7 => "SMPTE 240M",
            8 => "linear",
            9 => "log 100:1",
            10 => "log 316:1",
            11 => "IEC 61966-2-4",
            12 => "BT.1361",
            13 => "sRGB / sYCC",
            14 => "BT.2020 10-bit",
            15 => "BT.2020 12-bit",
            16 => "SMPTE ST 2084 (PQ / HDR10)",
            17 => "SMPTE ST 428",
            18 => "ARIB STD-B67 (HLG)",
            _ => "reserved"
        };

        internal static string MatrixCoefficientsName(int v) => v switch
        {
            0 => "Identity / RGB (GBR)",
            1 => "BT.709",
            2 => "unspecified",
            4 => "FCC 47",
            5 => "BT.470BG",
            6 => "SMPTE 170M",
            7 => "SMPTE 240M",
            8 => "YCgCo",
            9 => "BT.2020 non-constant luminance",
            10 => "BT.2020 constant luminance",
            11 => "SMPTE ST 2085",
            14 => "ICtCp",
            _ => "reserved"
        };
    }
}