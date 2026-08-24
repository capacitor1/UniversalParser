using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// “内容不解析”的块的公共构建器：负载整体作为未解析数据交给 GUI 的二进制预览控件。
    /// 输出顺序为 leadingLines → &lt;PayloadLength&gt; → trailingLines。
    /// </summary>
    internal static class OpaqueChunk
    {
        public static ParseResult Build(
            RIFFParser parser,
            Node node,
            in RIFFChunkHeader header,
            string readableName,
            IEnumerable<(string K, string V)>? leadingLines = null,
            IEnumerable<(string K, string V)>? trailingLines = null)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            if (leadingLines is not null)
                dataLines.AddRange(leadingLines);

            dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(header.PayloadLength)));

            if (trailingLines is not null)
                dataLines.AddRange(trailingLines);

            return new ParseResult
            {
                Title = RIFFUtil.MakeTitle(readableName, header.Id),
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = parser.CreateRawStream(header.ChunkStart, (long)node.Length),
            };
        }
    }
}