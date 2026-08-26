using System;
using System.Collections.Generic;
using System.Globalization;

namespace UniversalParser.Src.Parser.FBX.Chunks
{
    /// <summary>
    /// Documents 节点及其 Document 子节点。
    ///
    /// Documents 结构：
    ///
    /// Documents
    ///     Count: 1
    ///     Document: id, name, type
    ///         Properties70
    ///         RootNode: 0
    ///
    /// Properties70 由 Properties70Chunk 独立解析，此处不重复聚合。
    /// </summary>
    internal static class DocumentsChunk
    {
        /// <summary>
        /// 解析 Documents 节点。
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

            long unparsedLength =
                FBXNodeValueReader.GetUnparsedPropertyLength(
                    parser,
                    header);

            bool countFound =
                FBXNodeValueReader.TryFindChild(
                    parser,
                    node,
                    "Count",
                    out _,
                    out FBXNodeHeader countHeader);

            int declaredCount = 0;
            bool declaredCountValid = false;

            if (countFound)
            {
                if (FBXNodeValueReader.TryGetSingleInt32(
                        parser,
                        countHeader,
                        out declaredCount))
                {
                    declaredCountValid = true;

                    dataLines.Add(
                        (
                            "Count",
                            FBXNodeValueReader.FormatInt32(
                                declaredCount)
                        ));
                }
                else
                {
                    dataLines.Add(
                        (
                            "<Warning>",
                            "The Count node does not contain a single integer property."
                        ));
                }
            }
            else
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The mandatory Count node is missing."
                    ));
            }

            int actualCount =
                FBXNodeValueReader.CountChildren(
                    parser,
                    node,
                    "Document");

            dataLines.Add(
                (
                    "<DocumentCount>",
                    FBXNodeValueReader.FormatInt32(
                        actualCount)
                ));

            if (declaredCountValid &&
                declaredCount != actualCount)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        $"Count declares {declaredCount.ToString(CultureInfo.InvariantCulture)} document(s), " +
                        $"but {actualCount.ToString(CultureInfo.InvariantCulture)} were found."
                    ));
            }

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The Documents node is truncated."
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
                    "DocumentList",
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
        /// 解析单个 Document 节点。
        ///
        /// 属性布局：
        ///
        /// 0: DocumentId (Int64)
        /// 1: Name (String)
        /// 2: ElementType (String)
        /// </summary>
        public static ParseResult ParseDocument(
            FBXParser parser,
            Node node,
            FBXNodeHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines =
                new List<(string K, string V)>();

            bool complete =
                FBXNodeValueReader.TryReadAll(
                    parser,
                    header,
                    out List<FBXPropertyValue> properties,
                    out long unparsedLength);

            if (!complete && properties.Count == 0)
            {
                dataLines.Add(
                    (
                        "<Error>",
                        "The Document property list could not be decoded."
                    ));
            }

            if (FBXNodeValueReader.TryGetInt64At(
                    properties,
                    0,
                    out long documentId))
            {
                dataLines.Add(
                    (
                        "DocumentId",
                        FBXNodeValueReader.FormatInt64(
                            documentId)
                    ));
            }

            if (FBXNodeValueReader.TryGetStringAt(
                    properties,
                    1,
                    out string documentName))
            {
                dataLines.Add(
                    (
                        "Name",
                        documentName
                    ));
            }

            if (FBXNodeValueReader.TryGetStringAt(
                    properties,
                    2,
                    out string elementType))
            {
                dataLines.Add(
                    (
                        "ElementType",
                        elementType
                    ));
            }

            if (properties.Count > 0 &&
                properties.Count != 3)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        $"A Document record is expected to carry three properties, " +
                        $"but {properties.Count.ToString(CultureInfo.InvariantCulture)} were found."
                    ));
            }

            if (FBXNodeValueReader.TryFindChild(
                    parser,
                    node,
                    "RootNode",
                    out _,
                    out FBXNodeHeader rootNodeHeader))
            {
                if (FBXNodeValueReader.TryGetSingleInt64(
                        parser,
                        rootNodeHeader,
                        out long rootNode))
                {
                    dataLines.Add(
                        (
                            "RootNode",
                            FBXNodeValueReader.FormatInt64(
                                rootNode)
                        ));
                }
                else
                {
                    dataLines.Add(
                        (
                            "<Warning>",
                            "The RootNode node does not contain a single integer property."
                        ));
                }
            }

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The Document node is truncated."
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
                    "Document",
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