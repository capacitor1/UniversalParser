using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// OpenDML（AVI 2.0）索引块：'indx' 与 'ix##'，即 AVIINDEXCHUNK 家族。
    /// 负载前 24 字节是公共头，其后是 aIndex 数组，步长由 wLongsPerEntry 决定。
    /// 条目布局完全由本块自身的 bIndexType / bIndexSubType / wLongsPerEntry 判定，不依赖其他块。
    /// 无法识别的类型按 adw[] 十六进制完整转储，不丢数据。
    /// </summary>
    internal static class AviIndexChunk
    {
        private const int HeaderSize = 24;

        // bIndexType
        private const byte AviIndexOfIndexes = 0x00;
        private const byte AviIndexOfChunks = 0x01;
        private const byte AviIndexOfTimedChunks = 0x02;
        private const byte AviIndexOfSub2Field = 0x03;
        private const byte AviIndexIsData = 0x80;

        // bIndexSubType
        private const byte AviIndexSubDefault = 0x00;
        private const byte AviIndexSub2Field = 0x01;

        /// <summary>AVISTDINDEX / AVIFIELDINDEX 的 dwSize 最高位：置位表示非关键帧。</summary>
        private const uint DeltaFrameFlag = 0x80000000;

        /// <summary>wLongsPerEntry 的合理上限。真实取值为 2/3/4；超限说明字段已损坏。</summary>
        private const int MaxLongsPerEntry = 32;

        /// <summary>
        /// 结构性硬上限，非策略截断：List&lt;(string, string)&gt; 的 backing array
        /// 每项 16 字节，默认 2 GB 单数组限制换算得 134,217,728 项。
        /// </summary>
        private const long MaxEntries = 134_217_728 - 16;

        private enum Layout
        {
            Generic,
            SuperIndex,
            StandardIndex,
            FieldIndex,
            TimedIndex,
        }

        /// <summary>'ix##' 的回退匹配规则（'indx' 用精确键注册）。</summary>
        public static bool MatchesStandardIndexFourCC(RIFFParser parser, RIFFChunkHeader header)
        {
            if (header.IsContainer) return false;
            if (parser.FormType is not ("AVI " or "AVIX")) return false;

            string id = header.Id;
            return id.Length == 4
                   && id[0] == 'i' && id[1] == 'x'
                   && char.IsAsciiHexDigit(id[2]) && char.IsAsciiHexDigit(id[3]);
        }

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            bool be = parser.IsBigEndian;
            long payloadLength = Math.Max(0, header.PayloadLength);

            Span<byte> head = stackalloc byte[HeaderSize];
            int headRead = payloadLength >= HeaderSize
                ? parser.ReadAt(header.PayloadStart, head)
                : 0;

            if (headRead < HeaderSize)
            {
                return AviUtil.Build(parser, node, header, "AviIndex",
                [
                    ("<Error>", $"AVIINDEXCHUNK requires a {HeaderSize}-byte header, {headRead} available."),
                    ("<PayloadLength>", RIFFUtil.FormatBytes(payloadLength)),
                ]);
            }

            ushort longsPerEntry = RIFFUtil.ReadUInt16(head[..2], be);
            byte subType = head[2];
            byte indexType = head[3];
            uint entriesInUse = RIFFUtil.ReadUInt32(head.Slice(4, 4), be);
            ReadOnlySpan<byte> chunkIdRaw = head.Slice(8, 4);

            (Layout layout, int expectedLongs, bool hasBaseOffset, string readableName) =
                ResolveLayout(indexType, subType);

            var dataLines = new List<(string K, string V)>(64);

            dataLines.Add(("wLongsPerEntry", longsPerEntry.ToString()));
            dataLines.Add(("bIndexSubType", $"0x{subType:X2}"));
            dataLines.Add(("<bIndexSubType>", DescribeSubType(subType)));
            dataLines.Add(("bIndexType", $"0x{indexType:X2}"));
            dataLines.Add(("<bIndexType>", DescribeIndexType(indexType)));
            dataLines.Add(("nEntriesInUse", entriesInUse.ToString()));
            dataLines.Add(("dwChunkId", AviUtil.FormatFourCCField(chunkIdRaw, be)));

            string? indexedId = AviUtil.TryReadFourCCField(chunkIdRaw);
            string? indexedDescription = AviStreamId.Describe(indexedId);
            if (indexedDescription is not null)
                dataLines.Add(("<dwChunkId>", indexedDescription));

            if (hasBaseOffset)
            {
                ulong baseOffset = be
                    ? System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(head.Slice(12, 8))
                    : System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(head.Slice(12, 8));

                dataLines.Add(("qwBaseOffset", baseOffset.ToString()));
                dataLines.Add(("dwReserved3", $"0x{RIFFUtil.ReadUInt32(head.Slice(20, 4), be):X8}"));
            }
            else
            {
                dataLines.Add(("dwReserved",
                    $"0x{RIFFUtil.ReadUInt32(head.Slice(12, 4), be):X8} "
                    + $"0x{RIFFUtil.ReadUInt32(head.Slice(16, 4), be):X8} "
                    + $"0x{RIFFUtil.ReadUInt32(head.Slice(20, 4), be):X8}"));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The index chunk is truncated."));

            // ---- 步长判定：wLongsPerEntry 是权威的物理步长 ----
            if (longsPerEntry == 0 || longsPerEntry > MaxLongsPerEntry)
            {
                dataLines.Add(("<Error>",
                    $"wLongsPerEntry is {longsPerEntry}, which cannot describe a usable entry stride; "
                    + "the entry array is left undecoded."));
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(payloadLength - HeaderSize)));
                return AviUtil.Build(parser, node, header, readableName, dataLines);
            }

            int stride = longsPerEntry * 4;
            if (layout != Layout.Generic && stride != expectedLongs * 4)
            {
                dataLines.Add(("<Warning>",
                    $"{DescribeIndexType(indexType)} expects wLongsPerEntry {expectedLongs}, found {longsPerEntry}; "
                    + "entries are dumped as raw DWORDs instead of typed columns."));
                layout = Layout.Generic;
            }

            long arrayBytes = payloadLength - HeaderSize;
            long entryCount = arrayBytes / stride;
            long remainder = arrayBytes % stride;

            if (remainder != 0)
            {
                dataLines.Add(("<Warning>",
                    $"The entry array is not a multiple of the {stride}-byte stride; "
                    + $"the trailing {remainder} byte(s) do not form a complete entry."));
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(remainder)));
            }

            if (entriesInUse != entryCount)
            {
                dataLines.Add(("<Note>",
                    $"nEntriesInUse is {entriesInUse} while {entryCount} complete entries are physically present; "
                    + "all present entries are listed."));
            }

            if (entryCount > MaxEntries)
            {
                dataLines.Add(("<Error>",
                    $"The array holds {entryCount:N0} entries, which exceeds the {MaxEntries:N0} rows that can be "
                    + "materialised in a single result. The array is left undecoded rather than truncated."));
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(arrayBytes)));
                return AviUtil.Build(parser, node, header, readableName, dataLines);
            }

            if (entryCount == 0)
                dataLines.Add(("<Note>", "The index contains no entries."));
            else
                AppendEntries(parser, header, dataLines, layout, longsPerEntry, stride, entryCount, be);

            return AviUtil.Build(parser, node, header, readableName, dataLines);
        }

        private static (Layout Layout, int ExpectedLongs, bool HasBaseOffset, string ReadableName) ResolveLayout(
            byte indexType, byte subType) => indexType switch
            {
                AviIndexOfIndexes => (Layout.SuperIndex, 4, false, "AviSuperIndex"),
                AviIndexOfChunks when subType == AviIndexSub2Field => (Layout.FieldIndex, 3, true, "AviFieldIndex"),
                AviIndexOfChunks => (Layout.StandardIndex, 2, true, "AviStandardIndex"),
                AviIndexOfTimedChunks => (Layout.TimedIndex, 3, true, "AviTimedIndex"),
                AviIndexOfSub2Field => (Layout.Generic, 0, false, "AviSubFieldIndex"),
                AviIndexIsData => (Layout.Generic, 0, false, "AviDataIndex"),
                _ => (Layout.Generic, 0, false, "AviIndex"),
            };

        private static string DescribeIndexType(byte value) => value switch
        {
            AviIndexOfIndexes => "AVI_INDEX_OF_INDEXES: super index pointing at other index chunks",
            AviIndexOfChunks => "AVI_INDEX_OF_CHUNKS: index of data chunks",
            AviIndexOfTimedChunks => "AVI_INDEX_OF_TIMED_CHUNKS: index of data chunks carrying durations",
            AviIndexOfSub2Field => "AVI_INDEX_OF_SUB_2FIELD: index of field-level sub indexes",
            AviIndexIsData => "AVI_INDEX_IS_DATA: the entries are payload data rather than locations",
            _ => "Unknown index type",
        };

        private static string DescribeSubType(byte value) => value switch
        {
            AviIndexSubDefault => "AVI_INDEX_SUB_DEFAULT",
            AviIndexSub2Field => "AVI_INDEX_SUB_2FIELD: entries describe two interlaced fields",
            _ => "Unknown index subtype",
        };

        private static void AppendEntries(
            RIFFParser parser,
            in RIFFChunkHeader header,
            List<(string K, string V)> dataLines,
            Layout layout,
            ushort longsPerEntry,
            int stride,
            long entryCount,
            bool be)
        {
            dataLines.Add(($"aIndex[{entryCount}]", BuildColumns(layout, longsPerEntry)));

            var builder = new StringBuilder(96);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(stride, 64 * 1024));
            try
            {
                int blockSize = buffer.Length - buffer.Length % stride;
                long position = header.PayloadStart + HeaderSize;
                long remaining = entryCount * stride;

                while (remaining > 0)
                {
                    int want = (int)Math.Min(blockSize, remaining);
                    int read = parser.ReadAt(position, buffer.AsSpan(0, want));
                    read -= read % stride;

                    if (read <= 0)
                    {
                        dataLines.Add(("<Warning>", "Unable to read the remaining index entries."));
                        break;
                    }

                    var block = new ReadOnlySpan<byte>(buffer, 0, read);
                    for (int offset = 0; offset + stride <= read; offset += stride)
                    {
                        dataLines.Add((string.Empty,
                            FormatEntry(block.Slice(offset, stride), layout, longsPerEntry, be, builder)));
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

        private static string BuildColumns(Layout layout, ushort longsPerEntry)
        {
            switch (layout)
            {
                case Layout.SuperIndex: return "qwOffset,dwSize,dwDuration";
                case Layout.StandardIndex: return "dwOffset,dwSize";
                case Layout.FieldIndex: return "dwOffset,dwSize,dwOffsetField2";
                case Layout.TimedIndex: return "dwOffset,dwSize,dwDuration";
                default:
                    var builder = new StringBuilder(longsPerEntry * 8);
                    for (int i = 0; i < longsPerEntry; i++)
                    {
                        if (i > 0) builder.Append(',');
                        builder.Append("adw[").Append(i).Append(']');
                    }
                    return builder.ToString();
            }
        }

        private static string FormatEntry(
            ReadOnlySpan<byte> entry, Layout layout, ushort longsPerEntry, bool be, StringBuilder builder)
        {
            builder.Clear();

            switch (layout)
            {
                case Layout.SuperIndex:
                {
                    ulong qwOffset = be
                        ? System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(entry[..8])
                        : System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(entry[..8]);

                    builder.Append(qwOffset);
                    if (qwOffset == 0) builder.Append(" (unused entry)");

                    builder.Append(',').Append(RIFFUtil.ReadUInt32(entry.Slice(8, 4), be))
                           .Append(',').Append(RIFFUtil.ReadUInt32(entry.Slice(12, 4), be));
                    break;
                }

                case Layout.StandardIndex:
                case Layout.FieldIndex:
                {
                    builder.Append(RIFFUtil.ReadUInt32(entry[..4], be)).Append(',');
                    AppendKeyFrameSize(builder, RIFFUtil.ReadUInt32(entry.Slice(4, 4), be));

                    if (layout == Layout.FieldIndex)
                        builder.Append(',').Append(RIFFUtil.ReadUInt32(entry.Slice(8, 4), be));
                    break;
                }

                case Layout.TimedIndex:
                {
                    builder.Append(RIFFUtil.ReadUInt32(entry[..4], be))
                           .Append(',').Append(RIFFUtil.ReadUInt32(entry.Slice(4, 4), be))
                           .Append(',').Append(RIFFUtil.ReadUInt32(entry.Slice(8, 4), be));
                    break;
                }

                default:
                {
                    for (int i = 0; i < longsPerEntry; i++)
                    {
                        if (i > 0) builder.Append(',');
                        builder.Append("0x").Append(RIFFUtil.ReadUInt32(entry.Slice(i * 4, 4), be).ToString("X8"));
                    }
                    break;
                }
            }

            return builder.ToString();
        }

        /// <summary>dwSize 的最高位是非关键帧标志，低 31 位是长度。括号内不使用逗号以免破坏 CSV 列。</summary>
        private static void AppendKeyFrameSize(StringBuilder builder, uint dwSize)
        {
            builder.Append("0x").Append(dwSize.ToString("X8"))
                   .Append(" (").Append(dwSize & ~DeltaFrameFlag)
                   .Append((dwSize & DeltaFrameFlag) != 0 ? " / delta frame)" : " / key frame)");
        }
    }
}