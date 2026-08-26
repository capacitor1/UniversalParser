using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace UniversalParser.Src.Parser.FBX.Chunks
{
    /// <summary>
    /// Objects 节点及其对象记录。
    ///
    /// 聚合视图：按对象类（子节点名）统计数量。
    /// 独立视图：对象记录头部 (Id, Name, Class) + 直接子节点。
    ///
    /// 子节点解析规则（ParseObjectChild）：
    /// - Properties70          跳过（由 Properties70Chunk 独立解析）
    /// - 数组 / Raw 属性       只记 PayloadLength（含 zlib 压缩数组），不读出
    /// - 标量属性              照常解析，多值用 ", " 拼接
    /// - 容器子节点（LayerElement*、PoseNode 等）
    ///                         显示自身标量后，浅递归一层展开其子节点
    /// </summary>
    internal static class ObjectsChunk
    {
        // ============================================================
        // Objects（聚合）
        // ============================================================

        public static ParseResult Parse(
            FBXParser parser,
            Node node,
            FBXNodeHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            long unparsedLength =
                FBXNodeValueReader.GetUnparsedPropertyLength(
                    parser,
                    header);

            var classCounts =
                new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (Node child in node.SubNodes)
            {
                if (!parser.TryGetNodeHeader(
                        child,
                        out FBXNodeHeader childHeader))
                {
                    continue;
                }

                string className =
                    string.IsNullOrEmpty(childHeader.Name)
                        ? "(unnamed)"
                        : childHeader.Name;

                classCounts.TryGetValue(
                    className,
                    out int count);

                classCounts[className] = count + 1;
            }

            if (classCounts.Count == 0)
            {
                dataLines.Add(
                    (
                        "<Note>",
                        "The object collection contains no object records."
                    ));
            }

            foreach (string className in
                     classCounts.Keys.OrderBy(
                         static key => key,
                         StringComparer.Ordinal))
            {
                dataLines.Add(
                    (
                        className,
                        classCounts[className].ToString(
                            CultureInfo.InvariantCulture)
                    ));
            }

            dataLines.Add(
                (
                    "<ObjectClassCount>",
                    classCounts.Count.ToString(
                        CultureInfo.InvariantCulture)
                ));

            dataLines.Add(
                (
                    "<TotalObjectCount>",
                    classCounts.Values.Sum().ToString(
                        CultureInfo.InvariantCulture)
                ));

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The Objects node is truncated."
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
                "ObjectCollection",
                dataLines);
        }

        // ============================================================
        // 对象记录（独立，通用）
        // ============================================================

        public static ParseResult ParseObject(
            FBXParser parser,
            Node node,
            FBXNodeHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            bool complete =
                FBXNodeValueReader.TryReadAll(
                    parser,
                    header,
                    out List<FBXPropertyValue> properties,
                    out long unparsedLength);

            if (FBXNodeValueReader.TryGetInt64At(
                    properties,
                    0,
                    out long id))
            {
                dataLines.Add(
                    (
                        "Id",
                        FBXNodeValueReader.FormatInt64(id)
                    ));
            }

            if (FBXNodeValueReader.TryGetStringAt(
                    properties,
                    1,
                    out string name))
            {
                dataLines.Add(
                    (
                        "Name",
                        name
                    ));
            }

            if (FBXNodeValueReader.TryGetStringAt(
                    properties,
                    2,
                    out string className))
            {
                dataLines.Add(
                    (
                        "Class",
                        className
                    ));
            }

            if (!complete && properties.Count == 0)
            {
                dataLines.Add(
                    (
                        "<Error>",
                        "The object record property list could not be decoded."
                    ));
            }

            if (properties.Count != 3)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        $"An object record is expected to carry three properties " +
                        $"(Id, Name, Class), but " +
                        $"{properties.Count.ToString(CultureInfo.InvariantCulture)} were found."
                    ));
            }

            foreach (Node child in node.SubNodes)
            {
                if (!parser.TryGetNodeHeader(
                        child,
                        out FBXNodeHeader childHeader))
                {
                    continue;
                }

                ParseObjectChild(
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
                        "The object record is truncated."
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
                "ObjectRecord",
                dataLines);
        }

        // ============================================================
        // 内部辅助
        // ============================================================

        /// <summary>
        /// 解析对象记录的一个直接子节点。
        ///
        /// depth 0：对象记录的直接子节点；
        /// depth 1：容器子节点（LayerElement*、PoseNode 等）内部的浅递归展开。
        /// 不进入更深的层级，避免聚合视图无限膨胀。
        /// </summary>
        internal static void ParseObjectChild(
            FBXParser parser,
            Node node,
            in FBXNodeHeader header,
            int depth,
            List<(string K, string V)> dataLines)
        {
            if (string.Equals(
                    header.Name,
                    "Properties70",
                    StringComparison.Ordinal))
            {
                return;
            }

            // 数据载荷：第一个属性是数组 / Raw（含 zlib 压缩数组）→ 只记 PayloadLength
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
                AddDataPayloadLine(
                    header,
                    dataLines);

                return;
            }

            // 标量 / 容器节点
            var scalarParts = new List<string>();
            bool hasBinary = false;
            bool readFailed = false;

            if (header.PropertyCount > 0)
            {
                if (FBXPropertyReader.TryReadProperties(
                        parser,
                        header,
                        out List<FBXPropertyValue> properties,
                        out long remaining))
                {
                    foreach (FBXPropertyValue property in properties)
                    {
                        if (property.IsArray || property.IsRaw)
                        {
                            hasBinary = true;
                        }
                        else
                        {
                            scalarParts.Add(
                                FBXNodeValueReader.FormatScalar(
                                    property));
                        }
                    }

                    if (remaining > 0)
                        hasBinary = true;
                }
                else
                {
                    readFailed = true;
                }
            }

            if (readFailed)
            {
                dataLines.Add(
                    (
                        header.Name,
                        "<unreadable>"
                    ));
            }
            else if (scalarParts.Count > 0)
            {
                dataLines.Add(
                    (
                        header.Name,
                        string.Join(", ", scalarParts)
                    ));
            }

            if (hasBinary)
            {
                dataLines.Add(
                    (
                        $"<{header.Name}>",
                        FBXUtil.FormatBytes(
                            header.ActualPropertyLength)
                    ));
            }

            // 容器子节点：浅递归一层（LayerElement* 的 Normals/UV、PoseNode 的 Matrix 等）
            if (depth < 1 && node.SubNodes.Count > 0)
            {
                foreach (Node sub in node.SubNodes)
                {
                    if (!parser.TryGetNodeHeader(
                            sub,
                            out FBXNodeHeader subHeader))
                    {
                        continue;
                    }

                    ParseObjectChild(
                        parser,
                        sub,
                        subHeader,
                        depth + 1,
                        dataLines);
                }
            }
        }

        private static void AddDataPayloadLine(
            in FBXNodeHeader header,
            List<(string K, string V)> dataLines)
        {
            string key =
                string.IsNullOrEmpty(header.Name)
                    ? "<(unnamed)>"
                    : $"<{header.Name}>";

            dataLines.Add(
                (
                    key,
                    FBXUtil.FormatBytes(
                        header.ActualPropertyLength)
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