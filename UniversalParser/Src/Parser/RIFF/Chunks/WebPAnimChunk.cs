using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WebP 动画全局参数 'ANIM'，载荷固定 6 字节。
    /// Background Color 为 uint32，规范按存储顺序将其四个字节标注为 [Blue, Green, Red, Alpha]，
    /// 故此处逐字节取值而非按整数解释，以免字节序引起歧义。
    /// 字段名沿用 WebP Container Specification 的原文命名。
    /// </summary>
    internal static class WebPAnimChunk
    {
        private const int StructSize = 6;

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            Span<byte> buffer = stackalloc byte[StructSize];
            int want = (int)Math.Min(StructSize, Math.Max(0, header.PayloadLength));
            int read = want > 0 ? parser.ReadAt(header.PayloadStart, buffer[..want]) : 0;

            if (read < StructSize)
            {
                dataLines.Add(("<Error>", $"'ANIM' requires {StructSize} bytes, {read} available."));
                WebPUtil.AddUnparsedLength(dataLines, header, 0);
                return WebPUtil.Build(parser, node, header, "AnimationParameters", dataLines);
            }

            byte blue = buffer[0];
            byte green = buffer[1];
            byte red = buffer[2];
            byte alpha = buffer[3];
            uint backgroundColor = WebPUtil.ReadUInt32LE(buffer[..4]);
            ushort loopCount = (ushort)(buffer[4] | (buffer[5] << 8));

            dataLines.Add(("Background Color", $"0x{backgroundColor:X8}"));
            dataLines.Add(("<Background Color>",
                $"blue {blue}, green {green}, red {red}, alpha {alpha}"));
            dataLines.Add(("Loop Count", loopCount.ToString()));

            if (loopCount == 0)
                dataLines.Add(("<Loop Count>", "0: loop indefinitely"));

            dataLines.Add(("<Note>",
                "The background colour applies to frames whose disposal method is 1. Readers may ignore it "
                + "and use their own background instead."));

            if (header.DeclaredPayloadLength != StructSize)
            {
                dataLines.Add(("<Warning>",
                    $"The chunk declares a {header.DeclaredPayloadLength:N0} byte payload; "
                    + $"'ANIM' is defined as exactly {StructSize} bytes."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'ANIM' chunk is truncated."));

            WebPUtil.AddUnparsedLength(dataLines, header, StructSize);
            return WebPUtil.Build(parser, node, header, "AnimationParameters", dataLines);
        }
    }
}