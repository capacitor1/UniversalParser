using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.ASF.Chunks
{
    /// <summary>
    /// 未识别对象的兜底解析器。
    /// 规范：未解析对象只给出 &lt;PayloadLength&gt;，不写任何其他内容。
    /// </summary>
    internal static class Default
    {
        public static ParseResult Parse(ASFParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            return parser.TryGetObjectHeader(node, out ASFObjectHeader header)
                ? Parse(parser, node, header)
                : ParseRaw(parser, node);
        }

        public static ParseResult Parse(ASFParser parser, Node node, ASFObjectHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            return new ParseResult
            {
                Title = ASFUtil.MakeTitle(
                    header.Name ?? "Unknown", ASFUtil.GuidDisplay(header.Guid)),
                Position = node.Position,
                Length = node.Length,
                DataLines = [("<PayloadLength>", ASFUtil.FormatBytes(header.PayloadLength))],
                RawData = parser.CreateRawStream(header.ObjectStart, (long)node.Length),
            };
        }

        /// <summary>
        /// 没有合法对象头的区域：尾部数据、损坏数据、递归深度截断占位。
        /// 这类区域不是对象，没有 GUID，因此标题不带引号部分。
        /// </summary>
        public static ParseResult ParseRaw(ASFParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            return new ParseResult
            {
                Title = "UnparsedData",
                Position = node.Position,
                Length = node.Length,
                DataLines =
                [
                    ("<PayloadLength>", ASFUtil.FormatBytes((long)node.Length)),
                    ("<Note>", "Not a valid ASF object (trailing data, padding or damaged region)."),
                ],
                RawData = parser.CreateRawStream((long)node.Position, (long)node.Length),
            };
        }
    }
}