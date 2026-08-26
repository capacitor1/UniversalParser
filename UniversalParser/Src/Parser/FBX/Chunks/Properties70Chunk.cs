using System;
using System.Collections.Generic;
using System.Globalization;

namespace UniversalParser.Src.Parser.FBX.Chunks
{
    /// <summary>
    /// Properties70 属性表及其 P 记录。
    ///
    /// Properties70 提供聚合视图，每条 P 记录同时可以单独解析。
    /// </summary>
    internal static class Properties70Chunk
    {
        /// <summary>
        /// 聚合解析 Properties70。
        /// 每条 P 记录输出一行原始值，必要时补充派生可读行。
        /// </summary>
        public static ParseResult Parse(
            FBXParser parser,
            Node node,
            FBXNodeHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines =
                new List<(string K, string V)>();

            long unparsedLength = 0;

            if (header.PropertyCount > 0)
            {
                FBXPropertyReader.TryReadProperties(
                    parser,
                    header,
                    out _,
                    out unparsedLength);
            }

            int recordCount = 0;
            int failedCount = 0;

            foreach (Node child in node.SubNodes)
            {
                if (!parser.TryGetNodeHeader(
                        child,
                        out FBXNodeHeader childHeader))
                {
                    continue;
                }

                if (!string.Equals(
                        childHeader.Name,
                        "P",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                recordCount++;

                if (!FBXProperty70Reader.TryRead(
                        parser,
                        childHeader,
                        out FBXProperty70 property))
                {
                    failedCount++;
                    continue;
                }

                string propertyName =
                    string.IsNullOrEmpty(property.PropName)
                        ? "(unnamed)"
                        : property.PropName;

                dataLines.Add(
                    (
                        propertyName,
                        FBXProperty70Reader.FormatValues(property)
                    ));

                string? description =
                    FBXProperty70Reader.DescribeValues(property);

                if (!string.IsNullOrEmpty(description))
                {
                    dataLines.Add(
                        (
                            $"<{propertyName}>",
                            description
                        ));
                }
            }

            if (recordCount == 0)
            {
                dataLines.Add(
                    (
                        "<Note>",
                        "The property table contains no P records."
                    ));
            }

            if (failedCount > 0)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        $"{failedCount.ToString(CultureInfo.InvariantCulture)} P record(s) could not be decoded."
                    ));
            }

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The Properties70 node is truncated."
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

            return new ParseResult
            {
                Title = FBXUtil.MakeTitle(
                    "PropertyTable",
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
        /// 单独解析一条 P 记录。
        /// </summary>
        public static ParseResult ParseProperty(
            FBXParser parser,
            Node node,
            FBXNodeHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines =
                new List<(string K, string V)>();

            if (!FBXProperty70Reader.TryRead(
                    parser,
                    header,
                    out FBXProperty70 property))
            {
                dataLines.Add(
                    (
                        "<Error>",
                        "The P record requires at least four string slots."
                    ));

                if (header.ActualPropertyLength > 0)
                {
                    dataLines.Add(
                        (
                            "<PayloadLength>",
                            FBXUtil.FormatBytes(
                                header.ActualPropertyLength)
                        ));
                }

                return Build(
                    parser,
                    node,
                    header,
                    dataLines);
            }

            dataLines.Add(
                (
                    "PropName",
                    property.PropName
                ));

            dataLines.Add(
                (
                    "PropType",
                    property.PropType
                ));

            dataLines.Add(
                (
                    "Label",
                    property.Label
                ));

            dataLines.Add(
                (
                    "Flags",
                    property.Flags
                ));

            dataLines.Add(
                (
                    "<Flags>",
                    FBXProperty70Reader.DescribeFlags(
                        property.Flags)
                ));

            AddValues(
                property,
                dataLines);

            if (property.HasValueCountMismatch)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        $"'{property.PropType}' expects " +
                        $"{property.ExpectedValueCount.ToString(CultureInfo.InvariantCulture)} value(s), " +
                        $"but {property.Values.Count.ToString(CultureInfo.InvariantCulture)} were found."
                    ));
            }

            if (property.Kind == FBXProperty70ValueKind.Unknown)
            {
                dataLines.Add(
                    (
                        "<Note>",
                        "The property type is not registered; values are shown as decoded."
                    ));
            }

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The P node is truncated."
                    ));
            }

            long unparsedLength =
                property.RawByteLength +
                property.UnparsedByteLength;

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

        private static void AddValues(
            in FBXProperty70 property,
            List<(string K, string V)> dataLines)
        {
            if (property.Values.Count == 0)
            {
                string? emptyDescription =
                    FBXProperty70Reader.DescribeValues(property);

                if (!string.IsNullOrEmpty(emptyDescription))
                {
                    dataLines.Add(
                        (
                            "<Value>",
                            emptyDescription
                        ));
                }

                return;
            }

            if (property.Values.Count == 1)
            {
                dataLines.Add(
                    (
                        "Value",
                        FBXProperty70Reader.FormatScalar(
                            property.Values[0])
                    ));
            }
            else
            {
                for (int i = 0; i < property.Values.Count; i++)
                {
                    dataLines.Add(
                        (
                            $"Value{i.ToString(CultureInfo.InvariantCulture)}",
                            FBXProperty70Reader.FormatScalar(
                                property.Values[i])
                        ));
                }
            }

            string? description =
                FBXProperty70Reader.DescribeValues(property);

            if (!string.IsNullOrEmpty(description))
            {
                dataLines.Add(
                    (
                        "<Value>",
                        description
                    ));
            }
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
                    "PropertyRecord",
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