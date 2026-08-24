using System;
using System.Buffers;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// ANI 的两个 DWORD 数组块：
    /// 'rate' 每个动画步的显示时长（jiffy，1/60 秒）；'seq ' 每个动画步引用的帧序号。
    /// 单列数组，故首行的值直接是第 0 项，不设列名行。
    /// </summary>
    internal static class AniStepArrayChunk
    {
        private const int ElementSize = 4;

        public static ParseResult ParseRate(RIFFParser parser, Node node, RIFFChunkHeader header) =>
            Parse(parser, node, header, "AniRate", "rate");

        public static ParseResult ParseSequence(RIFFParser parser, Node node, RIFFChunkHeader header) =>
            Parse(parser, node, header, "AniSequence", "seq");

        private static ParseResult Parse(
            RIFFParser parser, Node node, RIFFChunkHeader header, string readableName, string arrayName)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            long payloadLength = Math.Max(0, header.PayloadLength);
            long elementCount = payloadLength / ElementSize;
            long remainderBytes = payloadLength % ElementSize;

            var dataLines = new List<(string K, string V)>((int)Math.Min(elementCount, int.MaxValue - 8) + 4);

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", $"The '{arrayName}' chunk is truncated."));

            if (remainderBytes != 0)
            {
                dataLines.Add(("<Warning>",
                    $"Payload is not a multiple of {ElementSize} bytes; the trailing {remainderBytes} byte(s) do not "
                    + "form a complete element."));
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(remainderBytes)));
            }

            if (elementCount == 0)
            {
                dataLines.Add(("<Note>", "The array contains no elements."));
                return ChunkUtil.Build(parser, node, header, readableName, dataLines);
            }

            AppendElements(parser, header, dataLines, elementCount, arrayName);
            return ChunkUtil.Build(parser, node, header, readableName, dataLines);
        }

        private static void AppendElements(
            RIFFParser parser,
            in RIFFChunkHeader header,
            List<(string K, string V)> dataLines,
            long elementCount,
            string arrayName)
        {
            bool be = parser.IsBigEndian;
            string arrayKey = $"{arrayName}[{elementCount}]";
            bool first = true;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(ElementSize * 8192);
            try
            {
                int blockSize = buffer.Length - buffer.Length % ElementSize;
                long position = header.PayloadStart;
                long remaining = elementCount * ElementSize;

                while (remaining > 0)
                {
                    int want = (int)Math.Min(blockSize, remaining);
                    int read = parser.ReadAt(position, buffer.AsSpan(0, want));
                    read -= read % ElementSize;

                    if (read <= 0)
                    {
                        dataLines.Add(("<Warning>", "Unable to read the remaining array elements."));
                        break;
                    }

                    var block = new ReadOnlySpan<byte>(buffer, 0, read);
                    for (int offset = 0; offset + ElementSize <= read; offset += ElementSize)
                    {
                        uint value = RIFFUtil.ReadUInt32(block.Slice(offset, ElementSize), be);
                        dataLines.Add((first ? arrayKey : string.Empty, value.ToString()));
                        first = false;
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
    }
}