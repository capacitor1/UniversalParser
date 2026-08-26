using System;
using System.Collections.Generic;
using System.Globalization;

namespace UniversalParser.Src.Parser.FBX.Chunks
{
    /// <summary>
    /// Definitions 节点及其子结构。
    ///
    /// 典型结构：
    ///
    /// Definitions
    ///     Version: 100
    ///     Count: 4
    ///     ObjectType: "GlobalSettings"
    ///         Count: 1
    ///     ObjectType: "Model"
    ///         Count: 2
    ///         PropertyTemplate: "FbxNode"
    ///             Properties70
    ///
    /// 注意 Definitions 下的 Count 是所有 ObjectType 内部 Count 的总和，
    /// 而不是 ObjectType 记录的条数。
    ///
    /// Properties70 由 Properties70Chunk 独立解析，此处不重复聚合。
    /// </summary>
    internal static class DefinitionsChunk
    {
        // ============================================================
        // Definitions
        // ============================================================

        /// <summary>
        /// 聚合解析 Definitions。
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

            AddVersion(
                parser,
                node,
                dataLines);

            bool declaredCountValid =
                FBXNodeValueReader.TryGetChildInt32(
                    parser,
                    node,
                    "Count",
                    out int declaredCount);

            if (declaredCountValid)
            {
                dataLines.Add(
                    (
                        "Count",
                        FBXNodeValueReader.FormatInt32(
                            declaredCount)
                    ));
            }
            else if (FBXNodeValueReader.TryFindChild(
                         parser,
                         node,
                         "Count",
                         out _,
                         out _))
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The Count node does not contain a single integer property."
                    ));
            }
            else
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The mandatory Count node is missing."
                    ));
            }

            int objectTypeCount = 0;
            long totalObjectCount = 0;
            bool totalObjectCountValid = true;

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
                        "ObjectType",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                objectTypeCount++;

                bool typeNameValid =
                    FBXNodeValueReader.TryGetSingleString(
                        parser,
                        childHeader,
                        out string typeName);

                bool typeCountValid =
                    FBXNodeValueReader.TryGetChildInt32(
                        parser,
                        child,
                        "Count",
                        out int typeCount);

                if (typeCountValid)
                    totalObjectCount += typeCount;
                else
                    totalObjectCountValid = false;

                string key =
                    typeNameValid && typeName.Length > 0
                        ? typeName
                        : "(unnamed)";

                string value =
                    typeCountValid
                        ? FBXNodeValueReader.FormatInt32(
                            typeCount)
                        : string.Empty;

                dataLines.Add((key, value));
            }

            dataLines.Add(
                (
                    "<ObjectTypeCount>",
                    FBXNodeValueReader.FormatInt32(
                        objectTypeCount)
                ));

            if (totalObjectCountValid)
            {
                dataLines.Add(
                    (
                        "<TotalObjectCount>",
                        FBXNodeValueReader.FormatInt64(
                            totalObjectCount)
                    ));
            }
            else
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "At least one ObjectType record has no readable Count node."
                    ));
            }

            if (objectTypeCount == 0)
            {
                dataLines.Add(
                    (
                        "<Note>",
                        "The definition list contains no ObjectType records."
                    ));
            }

            if (declaredCountValid &&
                totalObjectCountValid &&
                declaredCount != totalObjectCount)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        $"Count declares {declaredCount.ToString(CultureInfo.InvariantCulture)} object(s), " +
                        $"but the ObjectType records sum up to " +
                        $"{totalObjectCount.ToString(CultureInfo.InvariantCulture)}."
                    ));
            }

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The Definitions node is truncated."
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
                "DefinitionList",
                dataLines);
        }

        // ============================================================
        // ObjectType
        // ============================================================

        /// <summary>
        /// 独立解析 ObjectType 记录。
        /// </summary>
        public static ParseResult ParseObjectType(
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
                        "The ObjectType property list could not be decoded."
                    ));
            }

            if (FBXNodeValueReader.TryGetStringAt(
                    properties,
                    0,
                    out string typeName))
            {
                dataLines.Add(
                    (
                        "Value",
                        typeName
                    ));
            }

            if (properties.Count > 1)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        $"An ObjectType record is expected to carry one property, " +
                        $"but {properties.Count.ToString(CultureInfo.InvariantCulture)} were found."
                    ));
            }

            if (FBXNodeValueReader.TryFindChild(
                    parser,
                    node,
                    "Count",
                    out _,
                    out FBXNodeHeader countHeader))
            {
                if (FBXNodeValueReader.TryGetSingleInt32(
                        parser,
                        countHeader,
                        out int count))
                {
                    dataLines.Add(
                        (
                            "Count",
                            FBXNodeValueReader.FormatInt32(
                                count)
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

            int templateCount = 0;

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
                        "PropertyTemplate",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                templateCount++;

                if (FBXNodeValueReader.TryGetSingleString(
                        parser,
                        childHeader,
                        out string templateName))
                {
                    dataLines.Add(
                        (
                            "PropertyTemplate",
                            templateName
                        ));
                }
                else
                {
                    dataLines.Add(
                        (
                            "<Warning>",
                            "A PropertyTemplate record does not contain a single string property."
                        ));
                }
            }

            if (templateCount > 1)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        $"An ObjectType record is expected to carry at most one PropertyTemplate, " +
                        $"but {templateCount.ToString(CultureInfo.InvariantCulture)} were found."
                    ));
            }

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The ObjectType node is truncated."
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
                "ObjectTypeDefinition",
                dataLines);
        }

        // ============================================================
        // PropertyTemplate
        // ============================================================

        /// <summary>
        /// 独立解析 PropertyTemplate 记录。
        /// 模板内容位于 Properties70 中，由其自身的解析器负责。
        /// </summary>
        public static ParseResult ParsePropertyTemplate(
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
                        "The PropertyTemplate property list could not be decoded."
                    ));
            }

            if (FBXNodeValueReader.TryGetStringAt(
                    properties,
                    0,
                    out string templateName))
            {
                dataLines.Add(
                    (
                        "Value",
                        templateName
                    ));
            }

            if (properties.Count > 1)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        $"A PropertyTemplate record is expected to carry one property, " +
                        $"but {properties.Count.ToString(CultureInfo.InvariantCulture)} were found."
                    ));
            }

            if (!FBXNodeValueReader.TryFindChild(
                    parser,
                    node,
                    "Properties70",
                    out _,
                    out _))
            {
                dataLines.Add(
                    (
                        "<Note>",
                        "The template carries no Properties70 table."
                    ));
            }

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The PropertyTemplate node is truncated."
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
                "PropertyTemplate",
                dataLines);
        }

        // ============================================================
        // 内部辅助
        // ============================================================

        private static void AddVersion(
            FBXParser parser,
            Node node,
            List<(string K, string V)> dataLines)
        {
            if (!FBXNodeValueReader.TryFindChild(
                    parser,
                    node,
                    "Version",
                    out _,
                    out FBXNodeHeader versionHeader))
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The mandatory Version node is missing."
                    ));

                return;
            }

            if (FBXNodeValueReader.TryGetSingleInt32(
                    parser,
                    versionHeader,
                    out int version))
            {
                dataLines.Add(
                    (
                        "Version",
                        FBXNodeValueReader.FormatInt32(
                            version)
                    ));

                return;
            }

            dataLines.Add(
                (
                    "<Warning>",
                    "The Version node does not contain a single integer property."
                ));
        }

        private static ParseResult Build(
            FBXParser parser,
            Node node,
            in FBXNodeHeader header,
            string readableName,
            List<(string K, string V)> dataLines)
        {
            return new ParseResult
            {
                Title = FBXUtil.MakeTitle(
                    readableName,
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