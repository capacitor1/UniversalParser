using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    internal static partial class Codec
    {
        private const int DoviRecordSize = 24;   // 192 bits, fixed

        // =====================================================================
        // dvcC / dvvC / dvwC : DolbyVisionConfigurationBox
        //   DOVIDecoderConfigurationRecord:
        //     dv_version_major(8) dv_version_minor(8)
        //     dv_profile(7) dv_level(6)
        //     rpu_present_flag(1) el_present_flag(1) bl_present_flag(1)
        //     dv_bl_signal_compatibility_id(4)
        //     reserved(28) reserved(32)[4]
        //
        // dv_bl_signal_compatibility_id only exists since Dolby Vision v2.1;
        // older (V1.2.93) files leave those 4 bits inside the reserved field.
        // =====================================================================
        public static void ParseDoviConfig(ReadOnlySpan<byte> d, LineWriter w,
                                           string boxType, string? entryType = null)
        {
            if (d.Length < 4)
            {
                w.Add("parse", $"{boxType} needs at least 4 bytes, got {d.Length}");
                w.Add("raw", Helper.Hex(d, 8));
                return;
            }

            var r = new BitReader(d);
            int major = (int)r.U(8);
            int minor = (int)r.U(8);
            int profile = (int)r.U(7);
            int level = (int)r.U(6);
            bool rpu = r.Flag();
            bool el = r.Flag();
            bool bl = r.Flag();

            // 4-bit compatibility id: present only when the record is long enough
            bool compatPresent = d.Length >= 5;
            int compatId = compatPresent ? (int)r.U(4) : 0;

            w.Add("dv_version", $"{major}.{minor}");
            w.Add("dv_profile", $"{DoviProfileName(profile)} ({profile})");
            w.Add("dv_level", DoviLevelName(level));
            w.Add("rpu_present_flag", rpu ? 1 : 0);
            w.Add("el_present_flag", el ? 1 : 0);
            w.Add("bl_present_flag", bl ? 1 : 0);
            w.Add("layers", DoviLayerString(bl, el, rpu));
            w.Add("dv_bl_signal_compatibility_id", compatPresent
                ? $"{DoviCompatName(compatId)} ({compatId})"
                : "not present (pre-v2.1 record, treated as 0 = None)");

            string fourcc = DoviCodecFourCc(profile, entryType);
            w.Add("codec_string", $"{fourcc}.{profile:D2}.{level:D2}");
            w.Add("summary", DoviSummary(major, minor, fourcc, profile, level, bl, el, rpu, compatId));

            // ---- reserved fields ----
            if (d.Length >= DoviRecordSize)
            {
                uint rsvd28 = r.U(28);
                bool tailZero = true;
                for (int i = 0; i < 4; i++) if (r.U(32) != 0) tailZero = false;
                if (rsvd28 != 0 || !tailZero)
                    w.Add("reserved", $"non-zero (0x{rsvd28:X7} + tail), record may be non-conformant");
            }

            // ---- consistency checks ----
            var notes = new List<string>();

            if (d.Length != DoviRecordSize)
                notes.Add($"record is {d.Length} bytes, expected {DoviRecordSize}");

            if (major != 1 && major != 2)
                notes.Add($"unexpected dv_version_major={major} (known values: 1, 2)");

            switch (boxType)
            {
                case "dvcC" when profile >= 8:
                    notes.Add($"profile {profile} is cross-compatible and should use 'dvvC', not 'dvcC'");
                    break;
                case "dvvC" when profile < 8 && profile != 0:
                    notes.Add($"profile {profile} is not cross-compatible and normally uses 'dvcC'");
                    break;
            }

            if (!bl)
                notes.Add("bl_present_flag=0: the base layer lives in another track");
            if (el && profile != 4 && profile != 6 && profile != 7)
                notes.Add($"el_present_flag=1 but profile {profile} is single-layer");
            if (!el && (profile == 4 || profile == 7))
                notes.Add($"profile {profile} is dual-layer but el_present_flag=0");
            if (!rpu)
                notes.Add("rpu_present_flag=0: no dynamic metadata, the track is not really Dolby Vision");
            if (profile == 5 && compatId != 0)
                notes.Add("profile 5 has no backward compatibility, dv_bl_signal_compatibility_id should be 0");
            if (profile == 8 && compatId == 0)
                notes.Add("profile 8 is cross-compatible, dv_bl_signal_compatibility_id should be 1/2/4");
            if (level == 0 || level > 13)
                notes.Add($"dv_level={level} is outside the defined range 1..13");

            if (entryType != null)
            {
                bool avcEntry = entryType is "avc1" or "avc3" or "dva1" or "dvav";
                bool hevcEntry = entryType is "hvc1" or "hev1" or "dvh1" or "dvhe";
                bool av1Entry = entryType is "av01" or "dav1";

                bool wantAvc = profile is 0 or 1 or 9;
                bool wantAv1 = profile == 10;
                bool wantHevc = !wantAvc && !wantAv1;

                if ((wantAvc && !avcEntry) || (wantHevc && !hevcEntry) || (wantAv1 && !av1Entry))
                    notes.Add($"profile {profile} does not match the '{entryType}' sample entry");
            }

            for (int i = 0; i < notes.Count; i++)
                w.Add(notes.Count == 1 ? "note" : $"note[{i}]", notes[i]);

            if (d.Length > DoviRecordSize)
                w.Add("extra_bytes", $"{d.Length - DoviRecordSize} ({Helper.Hex(d.Slice(DoviRecordSize), 16)})");
        }

        // =====================================================================
        // tables
        // =====================================================================

        private static string DoviProfileName(int p) => p switch
        {
            0  => "dvav.per - AVC, deprecated",
            1  => "dvav.pen - AVC High, deprecated",
            2  => "dvhe.den - HEVC Main10, deprecated",
            3  => "dvhe.dtb - HEVC Main, deprecated",
            4  => "dvhe.dtr - HEVC Main10 dual layer, SDR compatible",
            5  => "dvhe.stn - HEVC Main10 single layer, IPT-PQ-c2, not backward compatible",
            6  => "dvhe.dth - HEVC Main10 dual layer, HDR10 base (UHD Blu-ray), deprecated",
            7  => "dvhe.dtr - HEVC Main10 dual layer BL+EL+RPU (Blu-ray)",
            8  => "dvhe.st - HEVC Main10 single layer, cross compatible",
            9  => "dvav.se - AVC single layer, SDR compatible",
            10 => "dav1.10 - AV1 Main10 single layer",
            20 => "MV-HEVC stereoscopic",
            _  => "unknown / reserved"
        };

        /// <summary>dv_level encodes a max resolution / frame rate tier.</summary>
        private static string DoviLevelName(int l) => l switch
        {
            1  => "1 (1280x720 @ 24)",
            2  => "2 (1280x720 @ 30)",
            3  => "3 (1920x1080 @ 24)",
            4  => "4 (1920x1080 @ 30)",
            5  => "5 (1920x1080 @ 60)",
            6  => "6 (3840x2160 @ 24)",
            7  => "7 (3840x2160 @ 30)",
            8  => "8 (3840x2160 @ 48)",
            9  => "9 (3840x2160 @ 60)",
            10 => "10 (3840x2160 @ 120)",
            11 => "11 (7680x4320 @ 24)",
            12 => "12 (7680x4320 @ 30)",
            13 => "13 (7680x4320 @ 60)",
            _  => $"{l} (undefined)"
        };

        private static string DoviCompatName(int id) => id switch
        {
            0 => "None",
            1 => "HDR10 (PQ / BT.2020)",
            2 => "SDR (BT.709)",
            4 => "HLG (BT.2100)",
            6 => "Blu-ray HDR10",
            _ => "reserved"
        };

        private static string DoviLayerString(bool bl, bool el, bool rpu)
        {
            var parts = new List<string>(3);
            if (bl) parts.Add("BL");
            if (el) parts.Add("EL");
            if (rpu) parts.Add("RPU");
            return parts.Count == 0 ? "(none)" : string.Join("+", parts);
        }

        /// <summary>
        /// Picks the fourcc used in the RFC 6381 style Dolby Vision codec string.
        /// The '1' variants mean parameter sets live in the sample entry, the 'e'/'v'
        /// variants mean they are in-band, so we follow the actual sample entry when known.
        /// </summary>
        private static string DoviCodecFourCc(int profile, string? entryType)
        {
            bool inBand = entryType is "avc3" or "hev1" or "dvhe" or "dvav";

            if (profile is 0 or 1 or 9) return inBand ? "dvav" : "dva1";
            if (profile == 10) return "dav1";
            return inBand ? "dvhe" : "dvh1";
        }

        /// <summary>MediaInfo-like one-liner, handy for eyeballing against other tools.</summary>
        private static string DoviSummary(int major, int minor, string fourcc, int profile,
                                          int level, bool bl, bool el, bool rpu, int compatId)
        {
            var sb = new StringBuilder();
            sb.Append("Dolby Vision, Version ").Append(major).Append('.').Append(minor);
            sb.Append(", ").Append(fourcc).Append('.').Append(profile.ToString("D2"))
              .Append('.').Append(level.ToString("D2"));
            sb.Append(", ").Append(DoviLayerString(bl, el, rpu));
            if (compatId != 0) sb.Append(", ").Append(DoviCompatName(compatId)).Append(" compatible");
            return sb.ToString();
        }
    }
}