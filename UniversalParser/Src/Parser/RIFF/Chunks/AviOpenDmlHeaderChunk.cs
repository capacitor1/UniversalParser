using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// OpenDML 扩展 AVI 头部 'dmlh'（ODMLExtendedAVIHeader），位于 LIST 'odml' 中。
    /// 规范只定义 dwTotalFrames 一个字段，其后为保留区，不解析。
    /// </summary>
    internal static class AviOpenDmlHeaderChunk
    {
        private const int FieldSize = 4;

        /// <summary>保留区的非零扫描上限。'dmlh' 实际只有几百字节，此值仅作防御。</summary>
        private const int MaxScanBytes = 64 * 1024;

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();
            int read = ChunkUtil.ReadPayload(parser, header, MaxScanBytes, out byte[] payload);

            if (read < FieldSize)
            {
                dataLines.Add(("<Error>",
                    $"ODMLExtendedAVIHeader requires at least {FieldSize} bytes, {read} available."));
                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "OpenDmlHeader", dataLines);
            }

            var span = new ReadOnlySpan<byte>(payload, 0, read);
            uint totalFrames = RIFFUtil.ReadUInt32(span[..FieldSize], parser.IsBigEndian);

            dataLines.Add(("dwTotalFrames", totalFrames.ToString()));

            if (totalFrames == 0)
            {
                dataLines.Add(("<Note>",
                    "dwTotalFrames is zero; the writer left the total frame count unfilled."));
            }

            if (header.PayloadLength > FieldSize)
            {
                dataLines.Add(("<Note>",
                    "The OpenDML specification defines dwTotalFrames as the only field; the remaining bytes are "
                    + "reserved for future expansion and are not decoded."));

                int nonZero = span[FieldSize..].IndexOfAnyExcept((byte)0);
                if (nonZero >= 0)
                {
                    dataLines.Add(("<Warning>",
                        $"The reserved area is not all zero; the first non-zero byte is at payload offset "
                        + $"{FieldSize + nonZero}."));
                }
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'dmlh' chunk is truncated."));

            ChunkUtil.AddUnparsedLength(dataLines, header, FieldSize);
            return ChunkUtil.Build(parser, node, header, "OpenDmlHeader", dataLines);
        }
    }
}