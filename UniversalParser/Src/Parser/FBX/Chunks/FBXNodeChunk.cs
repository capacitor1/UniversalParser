using System;
using System.Collections.Generic;
using System.Globalization;

namespace UniversalParser.Src.Parser.FBX.Chunks
{
    /// <summary>
    /// 通用 FBX 节点解析器。
    ///
    /// 作为 Dispatcher 的最终兜底，覆盖对象记录的全部子节点：
    /// - 数组 / Raw 属性（含 zlib 压缩数组）：只记 &lt;PayloadLength&gt;，不读出
    /// - 标量属性：原样呈现（单值 Value，多值 Value0..N）
    /// - 未解析尾部字节：&lt;PayloadLength&gt;
    /// - 容器节点：自身标量之后，浅递归一层展开子节点
    ///
    /// 不解释任何值的语义，一切按原始数据呈现。
    /// </summary>
    internal static class FBXNodeChunk
    {
        public static ParseResult Parse(
            FBXParser parser,
            Node node,
            FBXNodeHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            // ---- 数据载荷节点：第一个属性是数组 / Raw ----
            bool isPayload =
                FBXPropertyReader.TryProbeFirstProperty(
                    parser,
                    header,
                    out char typeCode,
                    out _,
                    out _) &&
                (FBXPropertyReader.IsArrayType(typeCode) ||
                 typeCode == 'R');

            if (isPayload)
            {
                dataLines.Add(
                    (
                        "<PayloadLength>",
                        FBXUtil.FormatBytes(
                            header.ActualPropertyLength)
                    ));

                if (header.IsTruncated)
                {
                    dataLines.Add(
                        (
                            "<Warning>",
                            "The node is truncated."
                        ));
                }

                return Build(
                    parser,
                    node,
                    header,
                    dataLines);
            }

            // ---- 标量 / 容器节点 ----
            long unparsedLength = 0;

            if (header.PropertyCount > 0)
            {
                if (FBXPropertyReader.TryReadProperties(
                        parser,
                        header,
                        out List<FBXPropertyValue> properties,
                        out long remaining))
                {
                    var scalars = new List<string>();

                    foreach (FBXPropertyValue property in properties)
                    {
                        if (property.IsArray || property.IsRaw)
                        {
                            unparsedLength += property.EncodedLength;
                        }
                        else
                        {
                            scalars.Add(
                                FBXNodeValueReader.FormatScalar(
                                    property));
                        }
                    }

                    unparsedLength += remaining;

                    if (scalars.Count == 1)
                    {
                        dataLines.Add(
                            ("Value", scalars[0]));
                    }
                    else
                    {
                        for (int i = 0; i < scalars.Count; i++)
                        {
                            dataLines.Add(
                                (
                                    $"Value{i.ToString(CultureInfo.InvariantCulture)}",
                                    scalars[i]
                                ));
                        }
                    }
                }
                else
                {
                    dataLines.Add(
                        (
                            "<Error>",
                            "The property list could not be decoded."
                        ));

                    unparsedLength = header.ActualPropertyLength;
                }
            }

            // ---- 容器子节点：浅递归一层 ----
            foreach (Node child in node.SubNodes)
            {
                if (!parser.TryGetNodeHeader(
                        child,
                        out FBXNodeHeader childHeader))
                {
                    continue;
                }

                ObjectsChunk.ParseObjectChild(
                    parser,
                    child,
                    childHeader,
                    depth: 0,
                    dataLines);
            }

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The node is truncated."
                    ));
            }

            if (unparsedLength > 0)
            {
                dataLines.Add(
                    (
                        "<PayloadLength>",
                        FBXUtil.FormatBytes(unparsedLength)
                    ));
            }

            return Build(
                parser,
                node,
                header,
                dataLines);
        }

        private static ParseResult Build(
            FBXParser parser,
            Node node,
            in FBXNodeHeader header,
            List<(string K, string V)> dataLines)
        {
            return new ParseResult
            {
                Title = FBXUtil.MakeTitle(
                    string.IsNullOrEmpty(header.Name)
                        ? "Node"
                        : header.Name,
                    header.Name),

                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,

                RawData = parser.CreateRawStream(
                    header.NodeStart,
                    (long)node.Length),
            };
        }
    }
}