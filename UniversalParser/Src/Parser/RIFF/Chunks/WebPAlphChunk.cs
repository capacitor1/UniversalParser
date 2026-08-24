using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WebP 透明通道 'ALPH'。自有头部为 1 字节位域，其后的 Alpha 位流按设计不解析。
    /// 位域按 MSB-0 编号：Rsv(2) | P(2) | F(2) | C(2)。
    /// 字段名沿用 WebP Container Specification 的原文命名。
    /// </summary>
    internal static class WebPAlphChunk
    {
        private const int HeaderSize = 1;

        private const byte MaskReserved = 0xC0;      // bits 0-1
        private const byte MaskPreprocessing = 0x30; // bits 2-3
        private const byte MaskFiltering = 0x0C;     // bits 4-5
        private const byte MaskCompression = 0x03;   // bits 6-7

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            Span<byte> buffer = stackalloc byte[HeaderSize];
            int read = header.PayloadLength >= HeaderSize
                ? parser.ReadAt(header.PayloadStart, buffer)
                : 0;

            if (read < HeaderSize)
            {
                dataLines.Add(("<Error>",
                    $"'ALPH' requires at least {HeaderSize} byte for its bit field, {read} available."));
                WebPUtil.AddUnparsedLength(dataLines, header, 0);
                return WebPUtil.Build(parser, node, header, "AlphaChannel", dataLines);
            }

            byte flags = buffer[0];

            int reserved = (flags & MaskReserved) >> 6;
            int preprocessing = (flags & MaskPreprocessing) >> 4;
            int filtering = (flags & MaskFiltering) >> 2;
            int compression = flags & MaskCompression;

            dataLines.Add(("Reserved (Rsv)", reserved.ToString()));
            dataLines.Add(("Preprocessing (P)", preprocessing.ToString()));
            dataLines.Add(("Filtering method (F)", filtering.ToString()));
            dataLines.Add(("Compression method (C)", compression.ToString()));

            dataLines.Add(("<Preprocessing (P)>", preprocessing switch
            {
                0 => "0: none",
                1 => "1: level reduction",
                _ => $"{preprocessing}: undefined",
            }));

            dataLines.Add(("<Filtering method (F)>", filtering switch
            {
                0 => "0: none",
                1 => "1: horizontal filter",
                2 => "2: vertical filter",
                3 => "3: gradient filter",
                _ => filtering.ToString(),
            }));

            dataLines.Add(("<Compression method (C)>", compression switch
            {
                0 => "0: no compression; the alpha plane is stored as raw 8-bit samples",
                1 => "1: compressed using the WebP lossless format",
                _ => $"{compression}: undefined",
            }));

            if (reserved != 0)
                dataLines.Add(("<Warning>", "Reserved (Rsv) must be 0."));

            if (compression > 1)
            {
                dataLines.Add(("<Warning>",
                    "Only compression methods 0 and 1 are defined; the alpha bitstream cannot be interpreted."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'ALPH' chunk is truncated."));

            WebPUtil.AddUnparsedLength(dataLines, header, HeaderSize);
            return WebPUtil.Build(parser, node, header, "AlphaChannel", dataLines);
        }
    }
}