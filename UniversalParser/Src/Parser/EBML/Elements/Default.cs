using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.EBML.Elements
{
    /// <summary>
    /// 未注册 EBML Element 的兜底解析器。
    ///
    /// Master Element 的负载已经作为子节点解析，因此不显示 PayloadLength。
    /// 未知叶子 Element 的整个负载未被解析，因此显示 PayloadLength。
    /// </summary>
    internal static class Default
    {
        public static ParseResult Parse(
            EBMLParser parser,
            Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            return parser.TryGetElementHeader(node, out EBMLElementHeader header)
                ? Parse(parser, node, header)
                : ParseRaw(parser, node);
        }

        public static ParseResult Parse(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            string readableName = EBMLSchema.GetName(header.ElementId);

            var dataLines = new List<(string K, string V)>();

            // Master Element 已被树解析器展开，其 payload 不属于“未解析数据”。
            if (!header.IsMaster)
            {
                dataLines.Add((
                    "<PayloadLength>",
                    EBMLUtil.FormatBytes(header.PayloadLength)));
            }

            if (header.IsUnknownSize)
            {
                dataLines.Add((
                    "<Note>",
                    "The Element uses an unknown Data Size."));
            }

            if (header.IsTruncated)
            {
                dataLines.Add((
                    "<Warning>",
                    "Declared Data Size exceeds the available range."));
            }

            return new ParseResult
            {
                Title = EBMLUtil.MakeTitle(
                    readableName,
                    header.ElementId,
                    header.ElementIdLength),

                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,

                RawData = parser.CreateRawStream(
                    header.ElementStart,
                    header.ElementLength),
            };
        }

        /// <summary>
        /// 没有合法 EBML Element Header 的区域。
        /// 该区域不是 Element，因此没有 Element ID。
        /// </summary>
        public static ParseResult ParseRaw(
            EBMLParser parser,
            Node node)
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
                    ("<PayloadLength>", EBMLUtil.FormatBytes((long)node.Length)),
                    ("<Note>", "Not a valid EBML Element."),
                ],

                RawData = parser.CreateRawStream(
                    (long)node.Position,
                    (long)node.Length),
            };
        }
    }
}