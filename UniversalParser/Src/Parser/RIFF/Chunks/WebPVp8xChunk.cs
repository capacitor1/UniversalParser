using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WebP 扩展格式头 'VP8X'，载荷固定 10 字节。
    /// 位域按 MSB-0 编号：Rsv(2) | I | L | E | X | A | R，随后 24 位保留、两个 uint24 画布尺寸。
    /// 字段名沿用 WebP Container Specification 的原文命名。
    /// </summary>
    internal static class WebPVp8xChunk
    {
        private const int StructSize = 10;

        private const byte MaskRsv = 0xC0;   // bits 0-1
        private const byte MaskIcc = 0x20;   // bit 2
        private const byte MaskAlpha = 0x10; // bit 3
        private const byte MaskExif = 0x08;  // bit 4
        private const byte MaskXmp = 0x04;   // bit 5
        private const byte MaskAnim = 0x02;  // bit 6
        private const byte MaskR = 0x01;     // bit 7

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
                dataLines.Add(("<Error>", $"'VP8X' requires {StructSize} bytes, {read} available."));
                WebPUtil.AddUnparsedLength(dataLines, header, 0);
                return WebPUtil.Build(parser, node, header, "ExtendedFileHeader", dataLines);
            }

            byte flags = buffer[0];
            uint reserved24 = WebPUtil.ReadUInt24LE(buffer.Slice(1, 3));
            uint widthMinusOne = WebPUtil.ReadUInt24LE(buffer.Slice(4, 3));
            uint heightMinusOne = WebPUtil.ReadUInt24LE(buffer.Slice(7, 3));

            dataLines.Add(("Reserved (Rsv)", ((flags & MaskRsv) >> 6).ToString()));
            dataLines.Add(("ICC profile (I)", ((flags & MaskIcc) != 0 ? 1 : 0).ToString()));
            dataLines.Add(("Alpha (L)", ((flags & MaskAlpha) != 0 ? 1 : 0).ToString()));
            dataLines.Add(("Exif metadata (E)", ((flags & MaskExif) != 0 ? 1 : 0).ToString()));
            dataLines.Add(("XMP metadata (X)", ((flags & MaskXmp) != 0 ? 1 : 0).ToString()));
            dataLines.Add(("Animation (A)", ((flags & MaskAnim) != 0 ? 1 : 0).ToString()));
            dataLines.Add(("Reserved (R)", ((flags & MaskR) != 0 ? 1 : 0).ToString()));
            dataLines.Add(("Reserved", reserved24.ToString()));
            dataLines.Add(("Canvas Width Minus One", widthMinusOne.ToString()));
            dataLines.Add(("Canvas Height Minus One", heightMinusOne.ToString()));

            // 规范把这两个字段定义为 1-based（存储值偏移 -1），属于特殊编码，给出解码后的实际值
            long canvasWidth = widthMinusOne + 1L;
            long canvasHeight = heightMinusOne + 1L;
            dataLines.Add(("<CanvasWidth>", canvasWidth.ToString()));
            dataLines.Add(("<CanvasHeight>", canvasHeight.ToString()));

            if ((flags & MaskRsv) != 0)
                dataLines.Add(("<Warning>", "Reserved (Rsv) must be 0."));
            if ((flags & MaskR) != 0)
                dataLines.Add(("<Warning>", "Reserved (R) must be 0."));
            if (reserved24 != 0)
                dataLines.Add(("<Warning>", "The 24-bit Reserved field must be 0."));

            if (canvasWidth * canvasHeight > uint.MaxValue)
            {
                dataLines.Add(("<Warning>",
                    "The product of canvas width and height exceeds 2^32-1, which the specification forbids."));
            }

            if (header.DeclaredPayloadLength != StructSize)
            {
                dataLines.Add(("<Warning>",
                    $"The chunk declares a {header.DeclaredPayloadLength:N0} byte payload; "
                    + $"'VP8X' is defined as exactly {StructSize} bytes."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'VP8X' chunk is truncated."));

            WebPUtil.AddUnparsedLength(dataLines, header, StructSize);
            return WebPUtil.Build(parser, node, header, "ExtendedFileHeader", dataLines);
        }
    }
}