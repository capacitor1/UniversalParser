using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WebP 无损位流 'VP8L'。仅解析 5 字节头部，其后的熵编码数据按设计不解析。
    /// 头部布局：1 字节签名 0x2F，随后一个小端 uint32 按 LSB 优先取位：
    /// 14 位宽减一、14 位高减一、1 位 alpha_is_used、3 位 version_number。
    /// 字段名沿用 WebP Lossless Bitstream Format 伪代码中的命名；该伪代码将 image_width
    /// 定义为 ReadBits(14) + 1，故此处的 image_width 已是解码后的实际尺寸。
    /// </summary>
    internal static class WebPVp8lChunk
    {
        private const int HeaderSize = 5;
        private const byte Signature = 0x2F;
        private const int MaxDimension = 1 << 14;

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            Span<byte> buffer = stackalloc byte[HeaderSize];
            int want = (int)Math.Min(HeaderSize, Math.Max(0, header.PayloadLength));
            int read = want > 0 ? parser.ReadAt(header.PayloadStart, buffer[..want]) : 0;

            if (read < HeaderSize)
            {
                dataLines.Add(("<Error>",
                    $"The 'VP8L' header requires {HeaderSize} bytes, {read} available."));
                WebPUtil.AddUnparsedLength(dataLines, header, 0);
                return WebPUtil.Build(parser, node, header, "LosslessBitstream", dataLines);
            }

            byte signature = buffer[0];
            uint bits = WebPUtil.ReadUInt32LE(buffer.Slice(1, 4));

            int imageWidth = (int)(bits & 0x3FFF) + 1;
            int imageHeight = (int)((bits >> 14) & 0x3FFF) + 1;
            int alphaIsUsed = (int)((bits >> 28) & 0x1);
            int versionNumber = (int)((bits >> 29) & 0x7);

            dataLines.Add(("signature", $"0x{signature:X2}"));
            dataLines.Add(("image_width", imageWidth.ToString()));
            dataLines.Add(("image_height", imageHeight.ToString()));
            dataLines.Add(("alpha_is_used", alphaIsUsed.ToString()));
            dataLines.Add(("version_number", versionNumber.ToString()));

            if (signature != Signature)
            {
                dataLines.Add(("<Warning>",
                    $"The signature must be 0x{Signature:X2}; the remaining header fields are therefore unreliable."));
            }

            if (versionNumber != 0)
                dataLines.Add(("<Warning>", "version_number must be 0 in the current specification."));

            if (imageWidth > MaxDimension || imageHeight > MaxDimension)
            {
                dataLines.Add(("<Warning>",
                    $"The lossless format limits both dimensions to {MaxDimension}."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'VP8L' chunk is truncated."));

            WebPUtil.AddUnparsedLength(dataLines, header, HeaderSize);
            return WebPUtil.Build(parser, node, header, "LosslessBitstream", dataLines);
        }
    }
}