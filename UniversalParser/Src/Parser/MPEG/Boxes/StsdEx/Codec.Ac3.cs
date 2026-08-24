using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    internal static partial class Codec
    {
        private const int MaxEac3Substreams = 8;

        // ETSI TS 102 366 Table 4.4
        private static readonly int[] Ac3SampleRates = { 48000, 44100, 32000, 0 };

        // ETSI TS 102 366 Table 4.13, indexed by frmsizecod >> 1
        private static readonly int[] Ac3BitRates =
        { 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 448, 512, 576, 640 };

        // =====================================================================
        // dac3 : AC3SpecificBox (ETSI TS 102 366 Annex F)
        //   fscod(2) bsid(5) bsmod(3) acmod(3) lfeon(1) bit_rate_code(5) reserved(5)
        //   -> exactly 24 bits
        // =====================================================================
        public static void ParseDac3(ReadOnlySpan<byte> d, LineWriter w)
        {
            if (d.Length < 3)
            {
                w.Add("parse", $"dac3 must be 3 bytes, got {d.Length}");
                w.Add("raw", Helper.Hex(d, 8));
                return;
            }

            var r = new BitReader(d);
            int fscod  = (int)r.U(2);
            int bsid   = (int)r.U(5);
            int bsmod  = (int)r.U(3);
            int acmod  = (int)r.U(3);
            int lfeon  = (int)r.U(1);
            int brCode = (int)r.U(5);
            int rsvd   = (int)r.U(5);

            w.Add("fscod", Ac3FscodName(fscod));
            w.Add("bsid", $"{bsid}{(bsid == 8 ? " (AC-3)" : bsid == 6 ? " (AC-3 alternate syntax)" : " (unexpected for AC-3)")}");
            w.Add("bsmod", $"{Ac3BsmodName(bsmod, acmod)} ({bsmod})");
            w.Add("acmod", $"{Ac3AcmodName(acmod)} ({acmod})");
            w.Add("lfeon", lfeon);
            w.Add("channels", $"{Ac3FullBandwidthChannels(acmod) + lfeon} ({Ac3ChannelLayout(acmod, lfeon != 0)})");
            w.Add("bit_rate_code", brCode < Ac3BitRates.Length
                ? $"{Ac3BitRates[brCode]} kbps (code {brCode})"
                : $"reserved (code {brCode})");

            if (rsvd != 0) w.Add("reserved", $"0x{rsvd:X2} (expected 0)");
            if (d.Length > 3) w.Add("extra_bytes", $"{d.Length - 3} ({Helper.Hex(d.Slice(3), 8)})");

            w.Add("codec_string", "ac-3");
        }

        // =====================================================================
        // dec3 : EC3SpecificBox (ETSI TS 102 366 Annex F)
        //   data_rate(13) num_ind_sub(3)
        //   per independent substream (23 bits):
        //     fscod(2) bsid(5) reserved(1) asvc(1) bsmod(3) acmod(3) lfeon(1)
        //     reserved(3) num_dep_sub(4)
        //   then chan_loc(9) if num_dep_sub > 0, else reserved(1)
        //   optional JOC tail (ETSI TS 103 420):
        //     reserved(7) flag_ec3_extension_type_a(1) complexity_index_type_a(8)
        //
        // WARNING: ffmpeg's writer emits 5 reserved bits after lfeon instead of 3.
        // Both layouts agree when num_dep_sub == 0 (the extra bits are zero), so we
        // follow ETSI and validate afterwards.
        // =====================================================================
        public static void ParseDec3(ReadOnlySpan<byte> d, LineWriter w)
        {
            if (d.Length < 5)
            {
                w.Add("parse", $"dec3 must be at least 5 bytes, got {d.Length}");
                w.Add("raw", Helper.Hex(d, 8));
                return;
            }

            var r = new BitReader(d);
            int dataRate  = (int)r.U(13);
            int numIndSub = (int)r.U(3) + 1;

            w.Add("data_rate", dataRate == 0 ? "0 (unspecified)" : $"{dataRate} kbps (max)");
            w.Add("num_ind_sub", numIndSub);

            int totalChannels = 0;
            bool suspicious = false;

            for (int i = 0; i < numIndSub && i < MaxEac3Substreams; i++)
            {
                int fscod = (int)r.U(2);
                int bsid  = (int)r.U(5);
                r.Skip(1);                          // reserved
                int asvc  = (int)r.U(1);
                int bsmod = (int)r.U(3);
                int acmod = (int)r.U(3);
                int lfeon = (int)r.U(1);
                r.Skip(3);                          // reserved
                int numDepSub = (int)r.U(4);

                int chanLoc = 0;
                if (numDepSub > 0) chanLoc = (int)r.U(9);
                else r.Skip(1);                     // reserved

                if (r.Bad)
                {
                    w.Add("parse", $"truncated at independent substream {i}");
                    return;
                }

                int baseCh = Ac3FullBandwidthChannels(acmod) + lfeon;
                int extraCh = numDepSub > 0 ? Eac3ChanLocChannels(chanLoc) : 0;
                totalChannels += baseCh + extraCh;

                if (bsid != 16) suspicious = true;

                using (w.Push($"ind_sub[{i}]"))
                {
                    w.Add("fscod", Ac3FscodName(fscod));
                    w.Add("bsid", $"{bsid}{(bsid == 16 ? " (E-AC-3)" : " (unexpected: E-AC-3 requires 16)")}");
                    w.Add("asvc", asvc);
                    w.Add("bsmod", $"{Ac3BsmodName(bsmod, acmod)} ({bsmod})");
                    w.Add("acmod", $"{Ac3AcmodName(acmod)} ({acmod})");
                    w.Add("lfeon", lfeon);
                    w.Add("channels", $"{baseCh} ({Ac3ChannelLayout(acmod, lfeon != 0)})");
                    w.Add("num_dep_sub", numDepSub);

                    if (numDepSub > 0)
                    {
                        w.Add("chan_loc", $"0x{chanLoc:X3}");
                        w.Add("chan_loc_speakers", Eac3ChanLocNames(chanLoc));
                        w.Add("total_channels", $"{baseCh + extraCh} (with dependent substreams)");
                    }
                }
            }

            if (numIndSub > MaxEac3Substreams)
                w.Add("note", $"num_ind_sub={numIndSub} exceeds the guard limit, remaining substreams skipped");

            w.Add("total_channels", totalChannels);

            // Optional Dolby Atmos / JOC tail
            if (r.BitsLeft >= 16)
            {
                r.Skip(7);                          // reserved
                bool typeA = r.Flag();
                int complexity = (int)r.U(8);
                w.Add("flag_ec3_extension_type_a", typeA ? "1 (JOC / Dolby Atmos present)" : "0");
                w.Add("complexity_index_type_a", complexity == 0
                    ? "0 (no JOC objects)"
                    : $"{complexity} JOC object(s)");
                w.Add("codec_string", typeA ? "ec-3 (Atmos)" : "ec-3");
            }
            else
            {
                w.Add("codec_string", "ec-3");
            }

            if (suspicious)
                w.Add("note", "bsid != 16 on at least one substream: the box may use ffmpeg's "
                            + "5-reserved-bit variant, or the data is corrupt. Raw bytes: "
                            + Helper.Hex(d, 16));

            if (r.BitsLeft >= 8)
                w.Add("trailing_bits", $"{r.BitsLeft} bits unread");
        }

        // =====================================================================
        // shared AC-3 / E-AC-3 tables
        // =====================================================================
        private static string Ac3FscodName(int fscod)
            => fscod < 3 ? $"{Ac3SampleRates[fscod]} Hz ({fscod})"
                         : "reserved / see fscod2 (3)";

        /// <summary>Number of full-bandwidth channels, LFE excluded.</summary>
        private static int Ac3FullBandwidthChannels(int acmod) => acmod switch
        {
            0 => 2,   // 1+1 dual mono
            1 => 1,   // 1/0
            2 => 2,   // 2/0
            3 => 3,   // 3/0
            4 => 3,   // 2/1
            5 => 4,   // 3/1
            6 => 4,   // 2/2
            7 => 5,   // 3/2
            _ => 0
        };

        private static string Ac3AcmodName(int acmod) => acmod switch
        {
            0 => "1+1 (dual mono)",
            1 => "1/0 (mono)",
            2 => "2/0 (stereo)",
            3 => "3/0",
            4 => "2/1",
            5 => "3/1",
            6 => "2/2 (quad)",
            7 => "3/2",
            _ => "unknown"
        };

        private static string Ac3ChannelLayout(int acmod, bool lfe)
        {
            string s = acmod switch
            {
                0 => "Ch1, Ch2",
                1 => "C",
                2 => "L, R",
                3 => "L, C, R",
                4 => "L, R, S",
                5 => "L, C, R, S",
                6 => "L, R, Ls, Rs",
                7 => "L, C, R, Ls, Rs",
                _ => "?"
            };
            return lfe ? s + ", LFE" : s;
        }

        /// <summary>ETSI TS 102 366 Table 4.10 — bsmod also depends on acmod for value 7.</summary>
        private static string Ac3BsmodName(int bsmod, int acmod) => bsmod switch
        {
            0 => "main: complete main (CM)",
            1 => "main: music and effects (ME)",
            2 => "associated: visually impaired (VI)",
            3 => "associated: hearing impaired (HI)",
            4 => "associated: dialogue (D)",
            5 => "associated: commentary (C)",
            6 => "associated: emergency (E)",
            7 => acmod == 1 ? "associated: voice over (VO)" : "main: karaoke",
            _ => "unknown"
        };

        // ETSI TS 102 366 Table F.6.1 — chan_loc bit assignments
        private static readonly (int Mask, string Name, int Count)[] Eac3ChanLoc =
        {
            (0x001, "Lc/Rc",    2),
            (0x002, "Lrs/Rrs",  2),
            (0x004, "Cs",       1),
            (0x008, "Ts",       1),
            (0x010, "Lsd/Rsd",  2),
            (0x020, "Lw/Rw",    2),
            (0x040, "Lvh/Rvh",  2),
            (0x080, "Cvh",      1),
            (0x100, "LFE2",     1),
        };

        private static int Eac3ChanLocChannels(int chanLoc)
        {
            int n = 0;
            foreach (var e in Eac3ChanLoc)
                if ((chanLoc & e.Mask) != 0) n += e.Count;
            return n;
        }

        private static string Eac3ChanLocNames(int chanLoc)
        {
            var list = new List<string>();
            foreach (var e in Eac3ChanLoc)
                if ((chanLoc & e.Mask) != 0) list.Add(e.Name);
            return list.Count == 0 ? "(none)" : string.Join(", ", list);
        }
    }
}