using System;
using System.Collections.Generic;
using System.Globalization;

namespace UniversalParser.Src.Parser.FBX.Chunks
{
    /// <summary>
    /// GlobalSettings 节点。
    ///
    /// 该节点自身通常没有属性，其内容由两个子节点承载：
    ///
    /// Version
    /// Properties70
    ///
    /// Properties70 由 Properties70Chunk 独立解析，
    /// 此处不再重复聚合，避免信息重复与额外读取开销。
    /// </summary>
    internal static class GlobalSettingsChunk
    {
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

            bool versionFound = false;
            bool propertyTableFound = false;

            foreach (Node child in node.SubNodes)
            {
                if (!parser.TryGetNodeHeader(
                        child,
                        out FBXNodeHeader childHeader))
                {
                    continue;
                }

                switch (childHeader.Name)
                {
                    case "Version":
                    {
                        versionFound = true;

                        if (FBXNodeValueReader.TryGetSingleInt32(
                                parser,
                                childHeader,
                                out int version))
                        {
                            dataLines.Add(
                                (
                                    "Version",
                                    version.ToString(
                                        CultureInfo.InvariantCulture)
                                ));
                        }
                        else
                        {
                            dataLines.Add(
                                (
                                    "<Warning>",
                                    "The Version node does not contain a single integer property."
                                ));
                        }

                        break;
                    }

                    case "Properties70":
                        propertyTableFound = true;
                        break;
                }
            }

            if (!versionFound)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The mandatory Version node is missing."
                    ));
            }

            if (!propertyTableFound)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The mandatory Properties70 node is missing."
                    ));
            }

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The GlobalSettings node is truncated."
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
                    "GlobalSettings",
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