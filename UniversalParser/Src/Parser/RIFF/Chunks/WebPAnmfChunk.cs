using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WebP 动画帧 'ANMF'。自有头部固定 16 字节，其后为 Frame Data
    /// （可选的 'ALPH' 子块 + 一个 'VP8 ' 或 'VP8L' 位流子块）。
    /// 字段名沿用 WebP Container Specification 的原文命名。
    /// </summary>
    internal static class WebPAnmfChunk
    {
        private const int HeaderSize = 16;

        private const byte MaskReserved = 0xFC;  // bits 0-5
        private const byte MaskBlending = 0x02;  // bit 6
        private const byte MaskDisposal = 0x01;  // bit 7

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
                    $"The 'ANMF' header requires {HeaderSize} bytes, {read} available."));
                WebPUtil.AddUnparsedLength(dataLines, header, 0);
                return WebPUtil.Build(parser, node, header, "AnimationFrame", dataLines);
            }

            uint frameX = WebPUtil.ReadUInt24LE(buffer.Slice(0, 3));
            uint frameY = WebPUtil.ReadUInt24LE(buffer.Slice(3, 3));
            uint widthMinusOne = WebPUtil.ReadUInt24LE(buffer.Slice(6, 3));
            uint heightMinusOne = WebPUtil.ReadUInt24LE(buffer.Slice(9, 3));
            uint duration = WebPUtil.ReadUInt24LE(buffer.Slice(12, 3));
            byte flags = buffer[15];

            int blending = (flags & MaskBlending) != 0 ? 1 : 0;
            int disposal = (flags & MaskDisposal) != 0 ? 1 : 0;

            dataLines.Add(("Frame X", frameX.ToString()));
            dataLines.Add(("Frame Y", frameY.ToString()));
            dataLines.Add(("Frame Width Minus One", widthMinusOne.ToString()));
            dataLines.Add(("Frame Height Minus One", heightMinusOne.ToString()));
            dataLines.Add(("Frame Duration", duration.ToString()));
            dataLines.Add(("Reserved", ((flags & MaskReserved) >> 2).ToString()));
            dataLines.Add(("Blending method (B)", blending.ToString()));
            dataLines.Add(("Disposal method (D)", disposal.ToString()));

            // Frame X / Frame Y 以 2 像素为单位存储；宽高为 1-based，均属特殊编码
            dataLines.Add(("<FrameOffsetX>", (frameX * 2L).ToString()));
            dataLines.Add(("<FrameOffsetY>", (frameY * 2L).ToString()));
            dataLines.Add(("<FrameWidth>", (widthMinusOne + 1L).ToString()));
            dataLines.Add(("<FrameHeight>", (heightMinusOne + 1L).ToString()));

            dataLines.Add(("<Blending method (B)>", blending == 0
                ? "0: use alpha-blending with the corresponding pixels of the previous canvas"
                : "1: do not blend; overwrite the rectangle with the frame's own pixels"));

            dataLines.Add(("<Disposal method (D)>", disposal == 0
                ? "0: do not dispose; leave the canvas as is"
                : "1: dispose to background colour; fill the frame rectangle with the "
                  + "background colour given in the 'ANIM' chunk"));

            if ((flags & MaskReserved) != 0)
                dataLines.Add(("<Warning>", "The 6-bit Reserved field must be 0."));

            if (duration == 0)
            {
                dataLines.Add(("<Note>",
                    "A Frame Duration of 0 has implementation-defined behaviour; many readers substitute "
                    + "a minimum delay."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'ANMF' chunk is truncated."));

            // Frame Data 由子块承载。若解析器已枚举出子节点，则该区间不算未解析数据。
            if (node.SubNodes.Count == 0)
                WebPUtil.AddUnparsedLength(dataLines, header, HeaderSize);

            return WebPUtil.Build(parser, node, header, "AnimationFrame", dataLines);
        }
    }
}