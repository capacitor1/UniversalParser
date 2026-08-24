using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// RF64 / BW64 的 'ds64' 块（DataSize64Chunk）：承载超过 4 GB 的 64 位长度。
    /// 固定部分 28 字节，其后是 tableLength 指示的 ChunkSize64 数组（每项 12 字节）。
    /// 字段名按根签名选择：RF64 用 EBU Tech 3306 的命名，BW64 用 ITU-R BS.2088 的命名
    /// （后者第三对为 dummy，规范要求读取时忽略）。
    /// </summary>
    internal static class Ds64Chunk
    {
        private const int FixedSize = 28;
        private const int TableEntrySize = 12;
        private const long MaxEntries = 134_217_728 - 16;

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            bool be = parser.IsBigEndian;
            bool isBw64 = parser.RootId == "BW64";
            long payloadLength = Math.Max(0, header.PayloadLength);

            Span<byte> head = stackalloc byte[FixedSize];
            int headRead = payloadLength >= FixedSize ? parser.ReadAt(header.PayloadStart, head) : 0;

            if (headRead < FixedSize)
            {
                return AviUtil.Build(parser, node, header, "DataSize64",
                [
                    ("<Error>", $"'ds64' requires a {FixedSize}-byte fixed part, {headRead} available."),
                    ("<PayloadLength>", RIFFUtil.FormatBytes(payloadLength)),
                ]);
            }

            var dataLines = new List<(string K, string V)>(32);

            string riffLowName = isBw64 ? "bw64SizeLow" : "riffSizeLow";
            string riffHighName = isBw64 ? "bw64SizeHigh" : "riffSizeHigh";
            string riffCombinedName = isBw64 ? "<bw64Size>" : "<riffSize>";
            string thirdLowName = isBw64 ? "dummyLow" : "sampleCountLow";
            string thirdHighName = isBw64 ? "dummyHigh" : "sampleCountHigh";

            uint riffLow = RIFFUtil.ReadUInt32(head[..4], be);
            uint riffHigh = RIFFUtil.ReadUInt32(head.Slice(4, 4), be);
            uint dataLow = RIFFUtil.ReadUInt32(head.Slice(8, 4), be);
            uint dataHigh = RIFFUtil.ReadUInt32(head.Slice(12, 4), be);
            uint thirdLow = RIFFUtil.ReadUInt32(head.Slice(16, 4), be);
            uint thirdHigh = RIFFUtil.ReadUInt32(head.Slice(20, 4), be);
            uint tableLength = RIFFUtil.ReadUInt32(head.Slice(24, 4), be);

            dataLines.Add((riffLowName, riffLow.ToString()));
            dataLines.Add((riffHighName, riffHigh.ToString()));
            dataLines.Add((riffCombinedName, FormatSize(Combine(riffLow, riffHigh))));

            dataLines.Add(("dataSizeLow", dataLow.ToString()));
            dataLines.Add(("dataSizeHigh", dataHigh.ToString()));
            dataLines.Add(("<dataSize>", FormatSize(Combine(dataLow, dataHigh))));

            dataLines.Add((thirdLowName, thirdLow.ToString()));
            dataLines.Add((thirdHighName, thirdHigh.ToString()));

            if (isBw64)
            {
                dataLines.Add(("<Note>",
                    "dummyLow and dummyHigh exist only for RF64 cross-compatibility and must be ignored when read."));
            }
            else
            {
                dataLines.Add(("<sampleCount>", Combine(thirdLow, thirdHigh).ToString("N0")));
            }

            dataLines.Add(("tableLength", tableLength.ToString()));

            // ds64 必须紧随 12 字节根头部；此判定只用到本块自身的偏移
            if (header.ChunkStart != RIFFUtil.TypedContainerHeaderSize)
            {
                dataLines.Add(("<Warning>",
                    $"'ds64' must be the first chunk after the {RIFFUtil.TypedContainerHeaderSize}-byte root header, "
                    + $"but it starts at 0x{header.ChunkStart:X}."));
            }

            if (parser.RootId is not ("RF64" or "BW64"))
            {
                dataLines.Add(("<Note>",
                    $"The root signature is '{RIFFUtil.Sanitize(parser.RootId)}'; "
                    + "'ds64' is only defined for RF64 and BW64 containers."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'ds64' chunk is truncated."));

            long tableBytes = payloadLength - FixedSize;
            long entryCount = tableBytes / TableEntrySize;
            long remainder = tableBytes % TableEntrySize;

            if (remainder != 0)
            {
                dataLines.Add(("<Warning>",
                    $"The table is not a multiple of {TableEntrySize} bytes; "
                    + $"the trailing {remainder} byte(s) do not form a complete entry."));
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(remainder)));
            }

            if (tableLength != entryCount)
            {
                dataLines.Add(("<Note>",
                    $"tableLength is {tableLength} while {entryCount} complete entries are physically present; "
                    + "all present entries are listed."));
            }

            if (entryCount > MaxEntries)
            {
                dataLines.Add(("<Error>",
                    $"The table holds {entryCount:N0} entries, which exceeds the {MaxEntries:N0} rows that can be "
                    + "materialised in a single result. The table is left undecoded rather than truncated."));
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(tableBytes)));
                return AviUtil.Build(parser, node, header, "DataSize64", dataLines);
            }

            if (entryCount > 0)
                AppendTable(parser, header, dataLines, entryCount, isBw64, be);

            return AviUtil.Build(parser, node, header, "DataSize64", dataLines);
        }

        private static void AppendTable(
            RIFFParser parser,
            in RIFFChunkHeader header,
            List<(string K, string V)> dataLines,
            long entryCount,
            bool isBw64,
            bool be)
        {
            dataLines.Add(($"table[{entryCount}]",
                isBw64 ? "ckID,ckSizeLow,ckSizeHigh" : "chunkId,chunkSizeLow,chunkSizeHigh"));

            var builder = new StringBuilder(64);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(TableEntrySize * 4096);
            try
            {
                int blockSize = buffer.Length - buffer.Length % TableEntrySize;
                long position = header.PayloadStart + FixedSize;
                long remaining = entryCount * TableEntrySize;

                while (remaining > 0)
                {
                    int want = (int)Math.Min(blockSize, remaining);
                    int read = parser.ReadAt(position, buffer.AsSpan(0, want));
                    read -= read % TableEntrySize;

                    if (read <= 0)
                    {
                        dataLines.Add(("<Warning>", "Unable to read the remaining table entries."));
                        break;
                    }

                    var block = new ReadOnlySpan<byte>(buffer, 0, read);
                    for (int offset = 0; offset + TableEntrySize <= read; offset += TableEntrySize)
                    {
                        ReadOnlySpan<byte> entry = block.Slice(offset, TableEntrySize);

                        uint low = RIFFUtil.ReadUInt32(entry.Slice(4, 4), be);
                        uint high = RIFFUtil.ReadUInt32(entry.Slice(8, 4), be);

                        builder.Clear();
                        builder.Append(FormatChunkId(entry[..4], be))
                               .Append(',').Append(low)
                               .Append(',').Append(high)
                               .Append(" (").Append(Combine(low, high)).Append(')');

                        dataLines.Add((string.Empty, builder.ToString()));
                    }

                    position += read;
                    remaining -= read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static ulong Combine(uint low, uint high) => ((ulong)high << 32) | low;

        private static string FormatSize(ulong value) =>
            value <= long.MaxValue ? RIFFUtil.FormatBytes((long)value) : $"{value:N0} B";

        /// <summary>含逗号或引号的 chunkId（只可能来自损坏数据）会破坏 CSV 列对齐，改用十六进制呈现。</summary>
        private static string FormatChunkId(ReadOnlySpan<byte> raw, bool bigEndian)
        {
            string? fourCC = AviUtil.TryReadFourCCField(raw);
            if (fourCC is not null && fourCC.IndexOfAny([',', '"']) < 0)
                return RIFFUtil.Sanitize(fourCC);

            return $"0x{RIFFUtil.ReadUInt32(raw, bigEndian):X8}";
        }
    }
}