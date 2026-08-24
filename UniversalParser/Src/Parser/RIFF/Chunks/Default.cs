using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// 未识别块的兜底解析器。
    /// 规范：Unknown 块只给出 &lt;PayloadLength&gt;（未能解析的数据部分长度），不写任何其他内容。
    /// </summary>
    internal static class Default
    {
        public static ParseResult Parse(RIFFParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            return parser.TryGetChunkHeader(node, out RIFFChunkHeader header)
                ? Parse(parser, node, header)
                : ParseRaw(parser, node);
        }

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            return new ParseResult
            {
                Title = RIFFUtil.MakeTitle("Unknown", header.Id),
                Position = node.Position,
                Length = node.Length,
                DataLines = [("<PayloadLength>", RIFFUtil.FormatBytes(header.PayloadLength))],
                RawData = parser.CreateRawStream(header.ChunkStart, (long)node.Length),
            };
        }

        /// <summary>
        /// 没有合法块头的区域：RIFF 块之后的尾部数据、损坏数据、递归深度截断占位。
        /// 这类区域不是 chunk，没有 4CC，因此标题不带引号部分。
        /// </summary>
        public static ParseResult ParseRaw(RIFFParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            return new ParseResult
            {
                Title = "UnparsedData ''",
                Position = node.Position,
                Length = node.Length,
                DataLines =
                [
                    ("<PayloadLength>", RIFFUtil.FormatBytes((long)node.Length)),
                    ("<Note>", "Not a valid RIFF chunk (trailing data, padding or damaged region)."),
                ],
                RawData = parser.CreateRawStream((long)node.Position, (long)node.Length),
            };
        }
    }
}