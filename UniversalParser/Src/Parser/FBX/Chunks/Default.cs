using System;

namespace UniversalParser.Src.Parser.FBX.Chunks
{
    /// <summary>
    /// FBX 节点的默认兜底解析器。
    ///
    /// 当前尚未实现具体 FBX 节点和属性解析，因此节点属性区视为
    /// “未解析数据”，只显示其实际可用长度。
    /// </summary>
    internal static class Default
    {
        public static ParseResult Parse(
            FBXParser parser,
            Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            if (parser.TryGetNodeHeader(
                    node,
                    out FBXNodeHeader header))
            {
                return Parse(
                    parser,
                    node,
                    header);
            }

            return ParseRaw(
                parser,
                node);
        }

        public static ParseResult Parse(
            FBXParser parser,
            Node node,
            FBXNodeHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            long unparsedPayloadLength =
                header.ActualPropertyLength;

            var dataLines =
                new System.Collections.Generic.List<(string K, string V)>
                {
                    (
                        "<PayloadLength>",
                        FBXUtil.FormatBytes(
                            unparsedPayloadLength)
                    )
                };

            if (header.PropertyCount > parser.Options.MaxPropertyCount)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "Property count exceeds the configured limit; properties were not expanded."
                    ));
            }

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The FBX node is truncated or its end offset is invalid."
                    ));
            }

            return new ParseResult
            {
                Title = FBXUtil.MakeTitle(
                    "Unknown",
                    header.Name),

                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,

                RawData = parser.CreateRawStream(
                    header.NodeStart,
                    (long)node.Length),
            };
        }

        /// <summary>
        /// 没有合法 FBX 节点头的区域。
        /// 例如：损坏数据、未解析的尾部区域、递归深度限制占位节点。
        /// </summary>
        public static ParseResult ParseRaw(
            FBXParser parser,
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
                    (
                        "<PayloadLength>",
                        FBXUtil.FormatBytes(
                            (long)node.Length)
                    ),
                    (
                        "<Note>",
                        "Not a valid FBX node."
                    )
                ],

                RawData = parser.CreateRawStream(
                    (long)node.Position,
                    (long)node.Length),
            };
        }
    }
}