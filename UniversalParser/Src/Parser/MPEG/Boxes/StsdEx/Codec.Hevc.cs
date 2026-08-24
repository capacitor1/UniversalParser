using System;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    internal static partial class Codec
    {
        public static void ParseHvcC(ReadOnlySpan<byte> d, LineWriter w)
        {
            var c = new Cur(d);

            byte cfgVersion = c.U8();

            byte b1 = c.U8();                       // profile_space(2) | tier(1) | profile_idc(5)
            int profileSpace = (b1 >> 6) & 0x03;
            bool highTier    = (b1 & 0x20) != 0;
            int profileIdc   = b1 & 0x1F;

            uint compatFlags   = c.U32();           // general_profile_compatibility_flags
            ulong constraints  = c.U48();           // general_constraint_indicator_flags
            byte levelIdc      = c.U8();            // general_level_idc  <-- 12th byte!

            int minSpatialSeg  = c.U16() & 0x0FFF;
            int parallelism    = c.U8() & 0x03;
            int chromaFormat   = c.U8() & 0x03;
            int bdLuma         = (c.U8() & 0x07) + 8;
            int bdChroma       = (c.U8() & 0x07) + 8;
            int avgFrameRate   = c.U16();           // frames / 256 s

            byte b = c.U8();
            int constantFrameRate  = (b >> 6) & 0x03;
            int numTemporalLayers  = (b >> 3) & 0x07;
            bool temporalIdNested  = (b & 0x04) != 0;
            int lengthSize         = (b & 0x03) + 1;

            int numArrays = c.U8();

            w.Add("configuration_version", cfgVersion);
            w.Add("profile_space", profileSpace == 0 ? "0" : $"{profileSpace} ({(char)('A' + profileSpace - 1)})");
            w.Add("tier", highTier ? "High" : "Main");
            w.Add("profile", $"{HevcProfileName(profileIdc)} ({profileIdc})");
            w.Add("profile_compatibility_flags", $"0x{compatFlags:X8}");
            w.Add("constraint_flags", $"0x{constraints:X12}");
            w.Add("level", $"{levelIdc / 30.0:0.0} ({levelIdc})");
            w.Add("min_spatial_segmentation_idc", minSpatialSeg);
            w.Add("parallelism_type", parallelism switch
            {
                0 => "unknown (0)", 1 => "slices (1)", 2 => "tiles (2)", _ => "entropy sync / WPP (3)"
            });
            w.Add("chroma_format", Helper.ChromaName(chromaFormat));
            w.Add("bit_depth", $"luma {bdLuma} / chroma {bdChroma}");
            w.Add("avg_frame_rate", avgFrameRate == 0 ? "unspecified" : $"{avgFrameRate / 256.0:0.###} fps");
            w.Add("constant_frame_rate", constantFrameRate switch
            {
                0 => "unknown (0)", 1 => "constant (1)", 2 => "constant per temporal layer (2)", _ => "reserved (3)"
            });
            w.Add("num_temporal_layers", numTemporalLayers);
            w.Add("temporal_id_nested", temporalIdNested ? 1 : 0);
            w.Add("nal_length_size", $"{lengthSize} byte(s)");
            w.Add("num_of_arrays", numArrays);
            w.Add("codec_string", HevcCodecString(profileSpace, highTier, profileIdc, compatFlags, constraints, levelIdc));

            ReadOnlySpan<byte> firstSps = default;

            for (int i = 0; i < numArrays && !c.Bad; i++)
            {
                byte h = c.U8();
                bool complete = (h & 0x80) != 0;
                int nalType   = h & 0x3F;
                int count     = c.U16();

                using (w.Push($"array[{i}]"))
                {
                    w.Add("nal_unit_type", $"{HevcNalName(nalType)} ({nalType})");
                    w.Add("array_completeness", complete ? 1 : 0);
                    w.Add("num_nalus", count);

                    for (int j = 0; j < count; j++)
                    {
                        int len = c.U16();
                        var nal = c.Bytes(len);
                        if (c.Bad) { w.Add("parse", "truncated array"); return; }
                        w.Add($"nalu[{j}]", $"{len} bytes | {Helper.Hex(nal, 16)}");
                        if (nalType == 33 && firstSps.IsEmpty) firstSps = nal;   // SPS_NUT
                    }
                }
            }

            if (!firstSps.IsEmpty)
                using (w.Push("sps"))
                    ParseHevcSps(firstSps, w);
        }

        private static void ParseHevcSps(ReadOnlySpan<byte> nal, LineWriter w)
        {
            if (nal.Length < 4) { w.Add("parse", "too short"); return; }
            int nalType = (nal[0] >> 1) & 0x3F;
            if (nalType != 33) { w.Add("parse", $"not an SPS (nal_unit_type={nalType})"); return; }

            var r = new BitReader(BitIO.Unescape(nal.Slice(2)));   // strip 2-byte NAL header

            r.U(4);                                                // sps_video_parameter_set_id
            int maxSubLayersMinus1 = (int)r.U(3);
            r.Skip(1);                                             // sps_temporal_id_nesting_flag

            ParseProfileTierLevel(ref r, maxSubLayersMinus1, w);

            r.UE();                                                // sps_seq_parameter_set_id
            int chromaFormatIdc = (int)r.UE();
            if (chromaFormatIdc == 3) r.Skip(1);                   // separate_colour_plane_flag

            uint picW = r.UE();
            uint picH = r.UE();

            uint cl = 0, cr = 0, ct = 0, cb = 0;
            if (r.Flag()) { cl = r.UE(); cr = r.UE(); ct = r.UE(); cb = r.UE(); }

            int bdLuma   = 8 + (int)r.UE();
            int bdChroma = 8 + (int)r.UE();

            if (r.Bad) { w.Add("parse", "truncated"); return; }

            int subW = (chromaFormatIdc == 1 || chromaFormatIdc == 2) ? 2 : 1;
            int subH = chromaFormatIdc == 1 ? 2 : 1;

            w.Add("chroma_format", Helper.ChromaName(chromaFormatIdc));
            w.Add("pic_size_in_luma_samples", $"{picW} x {picH}");
            w.Add("conformance_window", $"L{cl} R{cr} T{ct} B{cb}");
            w.Add("coded_size", $"{picW - (long)subW * (cl + cr)} x {picH - (long)subH * (ct + cb)}");
            w.Add("bit_depth", $"luma {bdLuma} / chroma {bdChroma}");
            w.Add("max_sub_layers", maxSubLayersMinus1 + 1);
        }

        private static void ParseProfileTierLevel(ref BitReader r, int maxSubLayersMinus1, LineWriter w)
        {
            int space = (int)r.U(2);
            bool tier = r.Flag();
            int idc   = (int)r.U(5);
            uint compat = r.U(32);
            r.Skip(4);                    // progressive/interlaced/non_packed/frame_only
            r.Skip(43);                   // reserved / range-extension constraint flags
            r.Skip(1);                    // inbld / reserved
            int level = (int)r.U(8);

            w.Add("ptl", $"{HevcProfileName(idc)} profile, {(tier ? "High" : "Main")} tier, level {level / 30.0:0.0}" +
                         (space != 0 ? $", profile_space={space}" : "") + $", compat=0x{compat:X8}");

            var subProfile = new bool[8];
            var subLevel   = new bool[8];
            for (int i = 0; i < maxSubLayersMinus1; i++) { subProfile[i] = r.Flag(); subLevel[i] = r.Flag(); }
            if (maxSubLayersMinus1 > 0)
                for (int i = maxSubLayersMinus1; i < 8; i++) r.Skip(2);
            for (int i = 0; i < maxSubLayersMinus1 && !r.Bad; i++)
            {
                if (subProfile[i]) r.Skip(88);
                if (subLevel[i])   r.Skip(8);
            }
        }

        /// <summary>RFC 6381 style, e.g. hvc1.1.6.L93.B0</summary>
        private static string HevcCodecString(int space, bool tier, int idc, uint compat, ulong constraints, byte level)
        {
            var sb = new StringBuilder("hvc1.");
            if (space > 0) sb.Append((char)('A' + space - 1));
            sb.Append(idc).Append('.');
            sb.Append(Helper.ReverseBits32(compat).ToString("X")).Append('.');
            sb.Append(tier ? 'H' : 'L').Append(level);

            var bytes = new byte[6];
            for (int i = 0; i < 6; i++) bytes[i] = (byte)(constraints >> (40 - 8 * i));
            int last = 5;
            while (last >= 0 && bytes[last] == 0) last--;
            for (int i = 0; i <= last; i++) sb.Append('.').Append(bytes[i].ToString("X2"));
            return sb.ToString();
        }

        private static string HevcProfileName(int idc) => idc switch
        {
            1  => "Main",
            2  => "Main 10",
            3  => "Main Still Picture",
            4  => "Format Range Extensions",
            5  => "High Throughput",
            6  => "Multiview Main",
            7  => "Scalable Main",
            8  => "3D Main",
            9  => "Screen Content Coding",
            11 => "Scalable Range Extensions",
            _  => "unknown"
        };

        private static string HevcNalName(int t) => t switch
        {
            32 => "VPS", 33 => "SPS", 34 => "PPS",
            39 => "PREFIX_SEI", 40 => "SUFFIX_SEI",
            _  => "other"
        };
    }
}