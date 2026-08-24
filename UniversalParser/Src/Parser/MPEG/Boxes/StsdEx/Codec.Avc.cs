using System;

namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    internal static partial class Codec
    {
        // ISO/IEC 14496-15: the avcC trailing extension exists for these profiles only
        private static readonly int[] AvcCExtProfiles = { 100, 110, 122, 144 };
        // ITU-T H.264 7.3.2.1.1: profiles that carry chroma_format_idc etc. in the SPS
        private static readonly int[] AvcSpsHighProfiles =
            { 100, 110, 122, 244, 44, 83, 86, 118, 128, 138, 139, 134, 135 };

        public static void ParseAvcC(ReadOnlySpan<byte> d, LineWriter w)
        {
            var c = new Cur(d);

            byte cfgVersion = c.U8();
            byte profile    = c.U8();
            byte compat     = c.U8();
            byte level      = c.U8();
            byte b4         = c.U8();
            byte b5         = c.U8();

            int lengthSize = (b4 & 0x03) + 1;
            int numSps     = b5 & 0x1F;

            w.Add("configuration_version", cfgVersion);
            w.Add("profile", $"{AvcProfileName(profile)} ({profile})");
            w.Add("profile_compatibility", $"0x{compat:X2}");
            w.Add("level", $"{level / 10.0:0.0} ({level})");
            w.Add("nal_length_size", $"{lengthSize} byte(s)");
            w.Add("num_of_sps", numSps);

            ReadOnlySpan<byte> firstSps = default;
            for (int i = 0; i < numSps && !c.Bad; i++)
            {
                int len = c.U16();
                var nal = c.Bytes(len);
                if (c.Bad) break;
                if (i == 0) firstSps = nal;
                w.Add($"sps[{i}]", $"{len} bytes | {Helper.Hex(nal, 16)}");
            }

            int numPps = c.U8();
            w.Add("num_of_pps", numPps);
            for (int i = 0; i < numPps && !c.Bad; i++)
            {
                int len = c.U16();
                var nal = c.Bytes(len);
                if (c.Bad) break;
                w.Add($"pps[{i}]", $"{len} bytes | {Helper.Hex(nal, 16)}");
            }

            if (Array.IndexOf(AvcCExtProfiles, (int)profile) >= 0 && c.Left >= 4)
            {
                int chroma  = c.U8() & 0x03;
                int bdLuma  = (c.U8() & 0x07) + 8;
                int bdChr   = (c.U8() & 0x07) + 8;
                int numExt  = c.U8();
                w.Add("chroma_format", Helper.ChromaName(chroma));
                w.Add("bit_depth_luma", bdLuma);
                w.Add("bit_depth_chroma", bdChr);
                w.Add("num_of_sps_ext", numExt);
                for (int i = 0; i < numExt && !c.Bad; i++) c.Bytes(c.U16());
            }

            w.Add("codec_string", $"avc1.{profile:X2}{compat:X2}{level:X2}");

            if (!firstSps.IsEmpty)
                using (w.Push("sps"))
                    ParseAvcSps(firstSps, w);
        }

        private static void ParseAvcSps(ReadOnlySpan<byte> nal, LineWriter w)
        {
            if (nal.Length < 4) { w.Add("parse", "too short"); return; }
            int nalType = nal[0] & 0x1F;
            if (nalType != 7) { w.Add("parse", $"not an SPS (nal_unit_type={nalType})"); return; }

            var r = new BitReader(BitIO.Unescape(nal.Slice(1)));   // strip 1-byte NAL header

            int profileIdc  = (int)r.U(8);
            uint constraint = r.U(8);
            int levelIdc    = (int)r.U(8);
            r.UE();                                               // seq_parameter_set_id

            int chromaFormatIdc = 1, bdLuma = 8, bdChroma = 8;
            bool separateColourPlane = false;

            if (Array.IndexOf(AvcSpsHighProfiles, profileIdc) >= 0)
            {
                chromaFormatIdc = (int)r.UE();
                if (chromaFormatIdc == 3) separateColourPlane = r.Flag();
                bdLuma   = 8 + (int)r.UE();
                bdChroma = 8 + (int)r.UE();
                r.Skip(1);                                        // qpprime_y_zero_transform_bypass_flag
                if (r.Flag())                                     // seq_scaling_matrix_present_flag
                {
                    int n = chromaFormatIdc != 3 ? 8 : 12;
                    for (int i = 0; i < n && !r.Bad; i++)
                        if (r.Flag()) SkipScalingList(ref r, i < 6 ? 16 : 64);
                }
            }

            r.UE();                                               // log2_max_frame_num_minus4
            uint pocType = r.UE();
            if (pocType == 0) r.UE();
            else if (pocType == 1)
            {
                r.Skip(1); r.SE(); r.SE();
                uint n = r.UE();
                for (uint i = 0; i < n && !r.Bad; i++) r.SE();
            }

            uint refFrames = r.UE();
            r.Skip(1);                                            // gaps_in_frame_num_value_allowed_flag
            uint widthMbs      = r.UE() + 1;
            uint heightMapUnit = r.UE() + 1;
            bool frameMbsOnly  = r.Flag();
            bool mbaff = false;
            if (!frameMbsOnly) mbaff = r.Flag();
            r.Skip(1);                                            // direct_8x8_inference_flag

            uint cl = 0, cr = 0, ct = 0, cb = 0;
            if (r.Flag()) { cl = r.UE(); cr = r.UE(); ct = r.UE(); cb = r.UE(); }

            if (r.Bad) { w.Add("parse", "truncated before geometry"); return; }

            int subW = chromaFormatIdc == 3 ? 1 : (chromaFormatIdc == 0 ? 1 : 2);
            int subH = chromaFormatIdc == 1 ? 2 : 1;
            if (chromaFormatIdc == 0 || separateColourPlane) { subW = 1; subH = 1; }
            int cropX = subW;
            int cropY = subH * (frameMbsOnly ? 1 : 2);

            long width  = (long)widthMbs * 16 - cropX * (long)(cl + cr);
            long height = (frameMbsOnly ? 1 : 2) * (long)heightMapUnit * 16 - cropY * (long)(ct + cb);

            w.Add("profile_idc", $"{AvcProfileName((byte)profileIdc)} ({profileIdc})");
            w.Add("constraint_flags", $"0x{constraint:X2}");
            w.Add("level_idc", $"{levelIdc / 10.0:0.0} ({levelIdc})");
            w.Add("chroma_format", Helper.ChromaName(chromaFormatIdc));
            w.Add("bit_depth", $"luma {bdLuma} / chroma {bdChroma}");
            w.Add("mb_dimensions", $"{widthMbs} x {heightMapUnit}");
            w.Add("crop", $"L{cl} R{cr} T{ct} B{cb}");
            w.Add("coded_size", $"{width} x {height}");
            w.Add("scan_type", frameMbsOnly ? "progressive" : (mbaff ? "interlaced (MBAFF)" : "interlaced (PAFF)"));
            w.Add("max_num_ref_frames", refFrames);

            if (r.Flag()) ParseAvcVui(ref r, w);                  // vui_parameters_present_flag
        }

        private static void ParseAvcVui(ref BitReader r, LineWriter w)
        {
            using (w.Push("vui"))
            {
                if (r.Flag())                                     // aspect_ratio_info_present_flag
                {
                    int idc = (int)r.U(8);
                    if (idc == 255) w.Add("sar", $"{r.U(16)}:{r.U(16)}");
                    else w.Add("aspect_ratio_idc", idc);
                }
                if (r.Flag()) r.Skip(1);                          // overscan
                if (r.Flag())                                     // video_signal_type
                {
                    r.Skip(3);                                    // video_format
                    w.Add("full_range_flag", r.Flag() ? 1 : 0);
                    if (r.Flag())
                        w.Add("colour", $"primaries={r.U(8)} transfer={r.U(8)} matrix={r.U(8)}");
                }
                if (r.Flag()) { r.UE(); r.UE(); }                 // chroma_loc_info
                if (r.Flag())                                     // timing_info_present_flag
                {
                    uint units = r.U(32), scale = r.U(32);
                    bool fixedRate = r.Flag();
                    w.Add("time_scale", $"{scale} / {units}");
                    if (units > 0 && !r.Bad)
                        w.Add("frame_rate", $"{scale / (2.0 * units):0.###} fps{(fixedRate ? " (fixed)" : "")}");
                }
            }
        }

        private static void SkipScalingList(ref BitReader r, int size)
        {
            int last = 8, next = 8;
            for (int i = 0; i < size && !r.Bad; i++)
            {
                if (next != 0) { next = (last + r.SE() + 256) % 256; }
                last = next == 0 ? last : next;
            }
        }

        private static string AvcProfileName(byte p) => p switch
        {
            66  => "Baseline",
            77  => "Main",
            88  => "Extended",
            100 => "High",
            110 => "High 10",
            122 => "High 4:2:2",
            144 => "High 4:4:4 (pre-2009)",
            244 => "High 4:4:4 Predictive",
            44  => "CAVLC 4:4:4 Intra",
            83  => "Scalable Baseline",
            86  => "Scalable High",
            118 => "Multiview High",
            128 => "Stereo High",
            _   => "unknown"
        };
    }
}