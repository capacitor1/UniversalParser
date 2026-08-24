using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WAVE 的 'fmt ' 块。字段解析由 WaveFormatEx 共享实现。
    /// &lt;PayloadLength&gt; 仅在存在未解析的格式扩展字节时出现。
    /// </summary>
    internal static class WaveFmtChunk
    {
        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            long parsedBytes = WaveFormatEx.Populate(
                parser, header.PayloadStart, header.PayloadLength, parser.IsBigEndian, dataLines);

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'fmt ' chunk is truncated."));

            AviUtil.AddUnparsedLength(dataLines, header, parsedBytes);
            return AviUtil.Build(parser, node, header, "WaveFormat", dataLines);
        }
    }
}