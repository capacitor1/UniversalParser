using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// AVI 1.0 索引块 'idx1'（AVIOLDINDEX）。负载是 aIndex 数组，每项 16 字节。
    /// 数组以 CSV 呈现：首行键为 aIndex[N]、值为列名，其后每行一个条目、键为空。
    /// 原始值原样输出，不做基准换算；可读文本以括号紧随原始值。
    /// </summary>
    internal static class AviOldIndexChunk
    {
        private const int EntrySize = 16;
        private const string Columns = "dwChunkId,dwFlags,dwOffset,dwSize";

        /// <summary>aviriff.h 中的 AVIIF_COMPRESSOR 掩码（vfw.h 里同一掩码名为 AVIIF_COMPUSE）。</summary>
        private const uint AviifCompressor = 0x0FFF0000;

        /// <summary>
        /// 结构性硬上限，非策略截断：dataLines 以 int 索引，且 List&lt;(string, string)&gt; 的
        /// backing array 每项 16 字节，默认 2 GB 单数组限制换算得 134,217,728 项。
        /// 超过此值无法完整生成，按错误报告而不是静默丢数据。
        /// </summary>
        private const long MaxEntries = 134_217_728 - 16;

        private static readonly (uint Mask, string Name)[] SingleBitFlags =
        [
            (0x00000001, "AVIIF_LIST"),
            (0x00000010, "AVIIF_KEYFRAME"),
            (0x00000020, "AVIIF_FIRSTPART"),
            (0x00000040, "AVIIF_LASTPART"),
            (0x00000100, "AVIIF_NO_TIME"),
        ];

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            long payloadLength = Math.Max(0, header.PayloadLength);
            long entryCount = payloadLength / EntrySize;
            long remainderBytes = payloadLength % EntrySize;

            if (entryCount > MaxEntries)
            {
                return AviUtil.Build(parser, node, header, "AviOldIndex",
                [
                    ("<Error>",
                        $"The index declares {entryCount:N0} entries, which exceeds the {MaxEntries:N0} rows that can "
                        + "be materialised in a single result. The payload is left undecoded rather than truncated."),
                    ("<PayloadLength>", RIFFUtil.FormatBytes(payloadLength)),
                ]);
            }

            // 说明性条目一律排在数组之前 —— 数组可能有数十万行，排在其后的内容没人看得见。
            var dataLines = new List<(string K, string V)>((int)entryCount + 4);

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'idx1' chunk is truncated."));

            if (remainderBytes != 0)
            {
                dataLines.Add(("<Warning>",
                    $"Payload is not a multiple of {EntrySize} bytes; the trailing {remainderBytes} byte(s) do not "
                    + "form a complete entry."));
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(remainderBytes)));
            }

            if (entryCount == 0)
                dataLines.Add(("<Note>", "The index contains no entries."));
            else
                AppendEntries(parser, header, dataLines, entryCount);

            return AviUtil.Build(parser, node, header, "AviOldIndex", dataLines);
        }

        private static void AppendEntries(
            RIFFParser parser,
            in RIFFChunkHeader header,
            List<(string K, string V)> dataLines,
            long entryCount)
        {
            bool be = parser.IsBigEndian;

            dataLines.Add(($"aIndex[{entryCount}]", Columns));

            // dwChunkId 与 dwFlags 的组合在整个索引中通常不超过五六种，
            // 缓存前两列可免去逐行重复解码 FourCC 与展开标志位。
            var prefixCache = new Dictionary<ulong, string>();
            var flagsBuilder = new StringBuilder(64);

            byte[] buffer = ArrayPool<byte>.Shared.Rent(EntrySize * 4096);
            try
            {
                int blockSize = buffer.Length - buffer.Length % EntrySize;
                long position = header.PayloadStart;
                long remaining = entryCount * EntrySize;

                while (remaining > 0)
                {
                    int want = (int)Math.Min(blockSize, remaining);
                    int read = parser.ReadAt(position, buffer.AsSpan(0, want));
                    read -= read % EntrySize;

                    if (read <= 0)
                    {
                        dataLines.Add(("<Warning>", "Unable to read the remaining index entries."));
                        break;
                    }

                    var block = new ReadOnlySpan<byte>(buffer, 0, read);
                    for (int offset = 0; offset + EntrySize <= read; offset += EntrySize)
                    {
                        ReadOnlySpan<byte> entry = block.Slice(offset, EntrySize);

                        uint flags = RIFFUtil.ReadUInt32(entry.Slice(4, 4), be);
                        uint dwOffset = RIFFUtil.ReadUInt32(entry.Slice(8, 4), be);
                        uint dwSize = RIFFUtil.ReadUInt32(entry.Slice(12, 4), be);

                        // 仅作缓存键，字节序无关
                        ulong cacheKey = ((ulong)BinaryPrimitives.ReadUInt32LittleEndian(entry[..4]) << 32) | flags;
                        if (!prefixCache.TryGetValue(cacheKey, out string? prefix))
                        {
                            prefix = BuildPrefix(entry[..4], flags, be, flagsBuilder);
                            prefixCache[cacheKey] = prefix;
                        }

                        dataLines.Add((string.Empty, $"{prefix}{dwOffset},{dwSize}"));
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

        /// <summary>构建 "dwChunkId,dwFlags (readable)," 两列前缀，含结尾逗号。</summary>
        private static string BuildPrefix(
            ReadOnlySpan<byte> chunkId, uint flags, bool bigEndian, StringBuilder flagsBuilder)
        {
            string id = FormatChunkId(chunkId, bigEndian);
            return $"{id},0x{flags:X8} ({DescribeIndexFlags(flags, flagsBuilder)}),";
        }

        /// <summary>
        /// dwChunkId 通常是 FourCC。含逗号或引号的 FourCC 会破坏 CSV 列对齐，
        /// 这类值（只可能来自损坏数据）改用十六进制原始值呈现。
        /// </summary>
        private static string FormatChunkId(ReadOnlySpan<byte> chunkId, bool bigEndian)
        {
            string? fourCC = AviUtil.TryReadFourCCField(chunkId);
            if (fourCC is not null && fourCC.IndexOfAny([',', '"']) < 0)
                return RIFFUtil.Sanitize(fourCC);

            return $"0x{RIFFUtil.ReadUInt32(chunkId, bigEndian):X8}";
        }

        private static string DescribeIndexFlags(uint value, StringBuilder builder)
        {
            if (value == 0) return "None";

            builder.Clear();
            uint known = AviifCompressor;

            foreach ((uint mask, string name) in SingleBitFlags)
            {
                known |= mask;
                if ((value & mask) == 0) continue;
                if (builder.Length > 0) builder.Append(" | ");
                builder.Append(name);
            }

            uint compressor = value & AviifCompressor;
            if (compressor != 0)
            {
                if (builder.Length > 0) builder.Append(" | ");
                builder.Append("AVIIF_COMPRESSOR(0x").Append((compressor >> 16).ToString("X3")).Append(')');
            }

            uint unknown = value & ~known;
            if (unknown != 0)
            {
                if (builder.Length > 0) builder.Append(" | ");
                builder.Append("reserved 0x").Append(unknown.ToString("X8"));
            }

            return builder.ToString();
        }
    }
}