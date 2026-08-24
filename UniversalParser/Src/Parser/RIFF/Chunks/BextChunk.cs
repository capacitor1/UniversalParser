using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// BWF 的 'bext' 块（Broadcast Audio Extension，EBU Tech 3285）。
    /// 固定部分恒为 602 字节，其后是不定长的 CodingHistory。
    /// 偏移 348-601 的布局由块内 Version 字段决定，无需依赖其他块。
    /// </summary>
    internal static class BextChunk
    {
        private const int FixedSize = 602;
        private const int UmidOffset = 348;
        private const int LoudnessOffset = 412;
        private const int ReservedOffsetV0 = 348;
        private const int ReservedOffsetV1 = 412;
        private const int ReservedOffsetV2 = 422;

        /// <summary>EBU Tech 3285 v2：未使用的 loudness 参数写 0x7FFF。</summary>
        private const short LoudnessNotSet = 0x7FFF;

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>(28);
            int read = ChunkUtil.ReadPayload(parser, header, FixedSize, out byte[] payload);

            if (read < FixedSize)
            {
                dataLines.Add(("<Error>",
                    $"BROADCAST_EXT requires a {FixedSize}-byte fixed part, {read} available."));
                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "BroadcastAudioExtension", dataLines);
            }

            bool be = parser.IsBigEndian;
            var span = new ReadOnlySpan<byte>(payload, 0, read);

            ReadOnlySpan<byte> description = span[..256];
            ReadOnlySpan<byte> originator = span.Slice(256, 32);
            ReadOnlySpan<byte> originatorReference = span.Slice(288, 32);

            dataLines.Add(("Description", RIFFUtil.DecodeText(description)));
            dataLines.Add(("Originator", RIFFUtil.DecodeText(originator)));
            dataLines.Add(("OriginatorReference", RIFFUtil.DecodeText(originatorReference)));
            dataLines.Add(("OriginationDate", RIFFUtil.DecodeText(span.Slice(320, 10))));
            dataLines.Add(("OriginationTime", RIFFUtil.DecodeText(span.Slice(330, 8))));

            uint timeRefLow = RIFFUtil.ReadUInt32(span.Slice(338, 4), be);
            uint timeRefHigh = RIFFUtil.ReadUInt32(span.Slice(342, 4), be);
            dataLines.Add(("TimeReferenceLow", timeRefLow.ToString()));
            dataLines.Add(("TimeReferenceHigh", timeRefHigh.ToString()));
            dataLines.Add(("<TimeReference>",
                $"{((ulong)timeRefHigh << 32) | timeRefLow} samples since midnight"));

            ushort version = RIFFUtil.ReadUInt16(span.Slice(346, 2), be);
            dataLines.Add(("Version", version.ToString()));
            dataLines.Add(("<Version>", DescribeVersion(version)));

            int reservedOffset;
            if (version >= 1)
            {
                ReadOnlySpan<byte> umid = span.Slice(UmidOffset, 64);
                dataLines.Add(("UMID", ChunkUtil.FormatOpaqueField(umid)));
                if (!ChunkUtil.IsAllZero(umid))
                    dataLines.Add(("<UMID>", DescribeUmid(umid)));

                reservedOffset = ReservedOffsetV1;
            }
            else
            {
                reservedOffset = ReservedOffsetV0;
            }

            if (version >= 2)
            {
                AddLoudness(dataLines, "LoudnessValue",
                    ChunkUtil.ReadInt16(span.Slice(LoudnessOffset, 2), be), "LUFS");
                AddLoudness(dataLines, "LoudnessRange",
                    ChunkUtil.ReadInt16(span.Slice(LoudnessOffset + 2, 2), be), "LU");
                AddLoudness(dataLines, "MaxTruePeakLevel",
                    ChunkUtil.ReadInt16(span.Slice(LoudnessOffset + 4, 2), be), "dBTP");
                AddLoudness(dataLines, "MaxMomentaryLoudness",
                    ChunkUtil.ReadInt16(span.Slice(LoudnessOffset + 6, 2), be), "LUFS");
                AddLoudness(dataLines, "MaxShortTermLoudness",
                    ChunkUtil.ReadInt16(span.Slice(LoudnessOffset + 8, 2), be), "LUFS");

                reservedOffset = ReservedOffsetV2;
            }

            dataLines.Add(("Reserved", ChunkUtil.FormatOpaqueField(span[reservedOffset..FixedSize])));

            // 残留数据痕迹：字符串字段的 NUL 之后仍有非零字节
            var leftovers = new List<string>(3);
            if (ChunkUtil.HasDataAfterTerminator(description)) leftovers.Add("Description");
            if (ChunkUtil.HasDataAfterTerminator(originator)) leftovers.Add("Originator");
            if (ChunkUtil.HasDataAfterTerminator(originatorReference)) leftovers.Add("OriginatorReference");
            if (leftovers.Count > 0)
            {
                dataLines.Add(("<Note>",
                    $"Non-zero bytes remain after the terminating NUL in: {string.Join(" / ", leftovers)}. "
                    + "This usually indicates leftover data from a previous longer value."));
            }

            // CodingHistory：不定长 ASCII，CR/LF 分隔的行
            long historyLength = header.PayloadLength - FixedSize;
            if (historyLength > 0)
            {
                int want = (int)Math.Min(historyLength, int.MaxValue);
                byte[] history = new byte[want];
                int historyRead = parser.ReadAt(header.PayloadStart + FixedSize, history);

                dataLines.Add(("CodingHistory", historyRead > 0
                    ? RIFFUtil.DecodeText(history.AsSpan(0, historyRead))
                    : string.Empty));

                if (historyRead < want)
                {
                    dataLines.Add(("<Warning>", "CodingHistory could not be fully read."));
                    dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(want - historyRead)));
                }
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'bext' chunk is truncated."));

            return ChunkUtil.Build(parser, node, header, "BroadcastAudioExtension", dataLines);
        }

        private static void AddLoudness(
            List<(string K, string V)> dataLines, string name, short value, string unit)
        {
            dataLines.Add((name, value.ToString()));
            dataLines.Add(($"<{name}>", value == LoudnessNotSet
                ? "0x7FFF: parameter not set"
                : $"{value / 100.0:0.00} {unit}"));
        }

        private static string DescribeVersion(ushort version) => version switch
        {
            0 => "EBU Tech 3285 version 0: no UMID and no loudness metadata",
            1 => "EBU Tech 3285 version 1: UMID present",
            2 => "EBU Tech 3285 version 2: UMID and loudness metadata present",
            _ => "Unknown version; the fixed part is interpreted as version 0",
        };

        /// <summary>SMPTE ST 330 UMID：基本 UMID 为 32 字节，扩展 UMID 为 64 字节。</summary>
        private static string DescribeUmid(ReadOnlySpan<byte> umid) =>
            ChunkUtil.IsAllZero(umid[32..])
                ? "Basic UMID (32 bytes used, remainder zero-filled)"
                : "Extended UMID (all 64 bytes used)";
    }
}