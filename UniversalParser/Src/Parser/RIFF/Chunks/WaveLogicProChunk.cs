using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WAVE 的 'LGWV' 块：Apple Logic Pro 私有块，无公开规范。
    /// 负载呈规律的二进制结构（形似波形概览缓存），但布局无从确证，故整块记为未解析。
    /// </summary>
    internal static class WaveLogicProChunk
    {
        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>(2)
            {
                ("<Note>",
                    "Apple Logic Pro private chunk. No published specification exists."),
            };

            ChunkUtil.AddUnparsedLength(dataLines, header, 0);
            return ChunkUtil.Build(parser, node, header, "WaveLogicPro", dataLines);
        }
    }
}