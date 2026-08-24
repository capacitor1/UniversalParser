using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// AVI 主头部 'avih'（AVIMAINHEADER，负载 56 字节）。
    /// 字段名与官方 AVIMAINHEADER 结构一致。
    /// </summary>
    internal static class AviMainHeaderChunk
    {
        private const int StructSize = 56;

        private static readonly (uint Mask, string Name)[] MainHeaderFlags =
        [
            (0x00000010, "AVIF_HASINDEX"),
            (0x00000020, "AVIF_MUSTUSEINDEX"),
            (0x00000100, "AVIF_ISINTERLEAVED"),
            (0x00000800, "AVIF_TRUSTCKTYPE"),
            (0x00010000, "AVIF_WASCAPTUREFILE"),
            (0x00020000, "AVIF_COPYRIGHTED"),
        ];

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();
            int read = AviUtil.ReadPayload(parser, header, StructSize, out byte[] payload);

            if (read < 40)
            {
                dataLines.Add(("<Error>", $"AVIMAINHEADER requires at least 40 bytes, {read} available."));
                AviUtil.AddUnparsedLength(dataLines, header, 0);
                return AviUtil.Build(parser, node, header, "AviMainHeader", dataLines);
            }

            bool be = parser.IsBigEndian;
            var span = new ReadOnlySpan<byte>(payload, 0, read);

            uint flags = RIFFUtil.ReadUInt32(span.Slice(12, 4), be);

            dataLines.Add(("dwMicroSecPerFrame", RIFFUtil.ReadUInt32(span.Slice(0, 4), be).ToString()));
            dataLines.Add(("dwMaxBytesPerSec", RIFFUtil.ReadUInt32(span.Slice(4, 4), be).ToString()));
            dataLines.Add(("dwPaddingGranularity", RIFFUtil.ReadUInt32(span.Slice(8, 4), be).ToString()));
            dataLines.Add(("dwFlags", $"0x{flags:X8}"));
            dataLines.Add(("<dwFlags>", AviUtil.DescribeFlags(flags, MainHeaderFlags)));
            dataLines.Add(("dwTotalFrames", RIFFUtil.ReadUInt32(span.Slice(16, 4), be).ToString()));
            dataLines.Add(("dwInitialFrames", RIFFUtil.ReadUInt32(span.Slice(20, 4), be).ToString()));
            dataLines.Add(("dwStreams", RIFFUtil.ReadUInt32(span.Slice(24, 4), be).ToString()));
            dataLines.Add(("dwSuggestedBufferSize", RIFFUtil.ReadUInt32(span.Slice(28, 4), be).ToString()));
            dataLines.Add(("dwWidth", RIFFUtil.ReadUInt32(span.Slice(32, 4), be).ToString()));
            dataLines.Add(("dwHeight", RIFFUtil.ReadUInt32(span.Slice(36, 4), be).ToString()));

            long parsedBytes = 40;

            if (read >= StructSize)
            {
                uint r0 = RIFFUtil.ReadUInt32(span.Slice(40, 4), be);
                uint r1 = RIFFUtil.ReadUInt32(span.Slice(44, 4), be);
                uint r2 = RIFFUtil.ReadUInt32(span.Slice(48, 4), be);
                uint r3 = RIFFUtil.ReadUInt32(span.Slice(52, 4), be);

                dataLines.Add(("dwReserved", $"0x{r0:X8}, 0x{r1:X8}, 0x{r2:X8}, 0x{r3:X8}"));
                parsedBytes = StructSize;

                if ((r0 | r1 | r2 | r3) != 0)
                {
                    // 1992 年的 MainAVIHeader 把这 16 字节定义为 dwScale / dwRate / dwStart / dwLength，
                    // 现行 AVIMAINHEADER 已改为 dwReserved[4]。旧写入器留下的数据会出现在这里。
                    dataLines.Add(("<Note>",
                        "dwReserved is non-zero; the legacy MainAVIHeader layout defined these fields as "
                        + "dwScale, dwRate, dwStart and dwLength."));
                }
            }
            else
            {
                dataLines.Add(("<Warning>",
                    $"AVIMAINHEADER is {StructSize} bytes; only {read} available (dwReserved is missing)."));
            }

            if (header.PayloadLength > StructSize)
            {
                dataLines.Add(("<Note>",
                    $"Chunk carries {header.PayloadLength - StructSize} byte(s) beyond AVIMAINHEADER."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'avih' chunk is truncated."));

            AviUtil.AddUnparsedLength(dataLines, header, parsedBytes);
            return AviUtil.Build(parser, node, header, "AviMainHeader", dataLines);
        }
    }
}