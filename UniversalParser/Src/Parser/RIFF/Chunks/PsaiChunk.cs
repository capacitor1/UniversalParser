using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// 'PSAI' 块。该 FourCC 无公开定义（WebP 容器规范未列，ExifTool 的 RIFF 标签表亦未收录），
    /// 但其载荷可由自身内容确证为 Photoshop 图像资源块序列：'8BIM' 签名加上各块长度累加
    /// 与 chunk size 严格吻合，无需借助其他块即可判定。
    /// 只解析资源块的结构，资源数据本身按设计不解码。
    /// </summary>
    internal static class PsaiChunk
    {
        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            return PhotoshopResourceBlocks.Probe(parser, header.PayloadStart, header.PayloadEnd)
                ? ParseResourceBlocks(parser, node, header)
                : ParseUndocumented(parser, node, header);
        }

        private static ParseResult ParseResourceBlocks(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            var dataLines = new List<(string K, string V)>
            {
                //("<Specification>",
                //    "The 'PSAI' FourCC itself is undocumented. Its payload is a sequence of Photoshop image "
                //    + "resource blocks, identified from the payload alone; these blocks are big-endian "
                //    + "irrespective of the RIFF container's byte order and are not RIFF sub-chunks."),
            };

            var rows = new List<string>();
            PhotoshopResourceBlocks.ScanResult scan =
                PhotoshopResourceBlocks.Scan(parser, header.PayloadStart, header.PayloadEnd, rows);

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'PSAI' chunk is truncated."));

            if (scan.NonStandardSignature)
            {
                dataLines.Add(("<Warning>",
                    "At least one block carries a signature other than the standard '8BIM'."));
            }

            if (scan.Malformed)
            {
                dataLines.Add(("<Warning>",
                    "The resource block sequence ended prematurely: a block header was unreadable or a "
                    + "declared size exceeded the chunk boundary."));
            }
            else if (scan.TrailingBytes > 0)
            {
                dataLines.Add(("<Note>",
                    $"{scan.TrailingBytes} byte(s) follow the last complete resource block."));
            }

            // 未解析部分 = 各资源块的数据 + 末尾残留字节
            dataLines.Add(("<PayloadLength>",
                RIFFUtil.FormatBytes(scan.ResourceDataBytes + scan.TrailingBytes)));

            dataLines.Add(($"imageResources[{rows.Count}]", PhotoshopResourceBlocks.Columns));
            dataLines.EnsureCapacity(dataLines.Count + rows.Count);
            foreach (string row in rows)
                dataLines.Add((string.Empty, row));

            return WebPUtil.Build(parser, node, header, "PhotoshopImageResources", dataLines);
        }

        private static ParseResult ParseUndocumented(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            var dataLines = new List<(string K, string V)>
            {
                ("<Specification>",
                    "Undocumented. This FourCC is defined neither by the WebP container specification nor by "
                    + "any other public RIFF reference. The payload does not start with a Photoshop image "
                    + "resource block either, so it is not decoded."),
            };

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'PSAI' chunk is truncated."));

            dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(header.PayloadLength)));
            return WebPUtil.Build(parser, node, header, "UndocumentedData", dataLines);
        }
    }
}