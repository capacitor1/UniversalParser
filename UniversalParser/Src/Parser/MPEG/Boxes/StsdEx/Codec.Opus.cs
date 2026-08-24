using System;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    internal static partial class Codec
    {
        // =====================================================================
        // dOps : OpusSpecificBox (Encapsulation of Opus in ISOBMFF)
        // NOTE: all fields are BIG-endian here, unlike OpusHead in Ogg.
        // =====================================================================
        public static void ParseDOps(ReadOnlySpan<byte> d, LineWriter w)
        {
            // Some broken muxers copy the Ogg identification header verbatim, magic included.
            if (d.Length >= 8 &&
                d[0] == 'O' && d[1] == 'p' && d[2] == 'u' && d[3] == 's' &&
                d[4] == 'H' && d[5] == 'e' && d[6] == 'a' && d[7] == 'd')
            {
                w.Add("parse", "payload starts with the 'OpusHead' magic: this is an Ogg ID header, "
                             + "not a valid dOps body (and its fields would be little-endian)");
                w.Add("raw", Helper.Hex(d, 32));
                return;
            }

            var c = new Cur(d);
            byte version   = c.U8();
            byte channels  = c.U8();
            ushort preSkip = c.U16();
            uint inputRate = c.U32();
            short gain     = c.S16();
            byte family    = c.U8();

            if (c.Bad) { w.Add("parse", $"dOps needs 11 bytes, got {d.Length}"); return; }

            w.Add("version", version + (version != 0 ? " (expected 0 for dOps)" : ""));
            w.Add("output_channel_count", channels);
            w.Add("pre_skip", $"{preSkip} samples @48kHz ({preSkip / 48.0:0.##} ms)");
            w.Add("input_sample_rate", inputRate == 0
                ? "0 (unspecified)"
                : $"{inputRate} Hz (original rate, informational only)");
            w.Add("output_gain", $"{gain / 256.0:+0.###;-0.###;0} dB (Q7.8, raw {gain})");
            w.Add("channel_mapping_family", $"{OpusFamilyName(family)} ({family})");

            if (version != 0)
            {
                w.Add("note", "unknown Version: readers must not interpret the fields after it");
                w.Add("raw", Helper.Hex(d, 32));
                return;
            }

            if (channels == 0) w.Add("note", "OutputChannelCount must be > 0");

            if (family == 0)
            {
                // ChannelMappingTable absent; values are implicit.
                w.Add("stream_count", "1 (implicit)");
                w.Add("coupled_count", channels == 2 ? "1 (implicit)" : "0 (implicit)");
                w.Add("channel_layout", channels switch
                {
                    1 => "mono (C)",
                    2 => "stereo (L, R)",
                    _ => $"invalid: family 0 allows only 1 or 2 channels, got {channels}"
                });
            }
            else
            {
                byte streamCount  = c.U8();
                byte coupledCount = c.U8();
                if (c.Bad) { w.Add("parse", "ChannelMappingTable truncated"); return; }

                w.Add("stream_count", streamCount);
                w.Add("coupled_count", $"{coupledCount} (uncoupled: {Math.Max(0, streamCount - coupledCount)})");
                if (streamCount == 0) w.Add("note", "StreamCount must be > 0");
                if (coupledCount > streamCount) w.Add("note", "CoupledCount must be <= StreamCount");

                string[] speakers = family == 1 ? OpusVorbisLayout(channels) : null;
                var layout = new StringBuilder();

                for (int i = 0; i < channels; i++)
                {
                    byte idx = c.U8();
                    if (c.Bad) { w.Add("parse", $"ChannelMapping truncated at index {i}"); return; }

                    string name = speakers != null && i < speakers.Length ? speakers[i] : $"ch{i}";
                    string src = idx == 255 ? "silence" : DescribeOpusStream(idx, coupledCount);
                    w.Add($"channel_mapping[{i}]", $"{name} <- {src}");

                    if (layout.Length > 0) layout.Append(", ");
                    layout.Append(name);
                }
                if (speakers != null) w.Add("channel_layout", layout.ToString());
            }

            w.Add("codec_string", "opus");   // RFC 6381: no parameters defined
            if (c.Left > 0)
                w.Add("trailing_bytes", $"{c.Left} ({Helper.Hex(c.D.Slice(c.P), 16)})");
        }

        /// <summary>Maps a ChannelMapping index onto the stream/channel it comes from.</summary>
        private static string DescribeOpusStream(byte idx, byte coupledCount)
        {
            if (idx < coupledCount * 2)
                return $"stream {idx / 2}, {(idx % 2 == 0 ? "left" : "right")} of coupled pair";
            int mono = idx - coupledCount * 2;
            return $"stream {coupledCount + mono} (uncoupled)";
        }

        private static string OpusFamilyName(byte f) => f switch
        {
            0   => "single stream, mono/stereo",
            1   => "Vorbis channel order, 1-8 channels",
            2   => "ambisonics, ACN/SN3D (RFC 8486)",
            3   => "ambisonics + non-diegetic stereo (RFC 8486)",
            255 => "no defined channel meaning",
            _   => "reserved (treat as 255)"
        };

        /// <summary>Vorbis speaker assignment used by channel mapping family 1.</summary>
        private static string[] OpusVorbisLayout(int ch) => ch switch
        {
            1 => new[] { "C" },
            2 => new[] { "L", "R" },
            3 => new[] { "L", "C", "R" },
            4 => new[] { "FL", "FR", "RL", "RR" },
            5 => new[] { "FL", "C", "FR", "RL", "RR" },
            6 => new[] { "FL", "C", "FR", "RL", "RR", "LFE" },
            7 => new[] { "FL", "C", "FR", "SL", "SR", "RC", "LFE" },
            8 => new[] { "FL", "C", "FR", "SL", "SR", "RL", "RR", "LFE" },
            _ => new[] { "reserved" }
        };
    }
}