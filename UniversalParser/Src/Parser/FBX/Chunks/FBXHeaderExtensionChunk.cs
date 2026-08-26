using System;
using System.Collections.Generic;
using System.Globalization;

namespace UniversalParser.Src.Parser.FBX.Chunks
{
    /// <summary>
    /// FBXHeaderExtension 节点解析器。
    ///
    /// 当前节点采用双层解析：
    ///
    /// 1. 解析 FBXHeaderExtension 自身，并聚合显示标准子节点；
    /// 2. 子节点仍然可以通过 Dispatcher 独立解析。
    /// </summary>
    internal static class FBXHeaderExtensionChunk
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

            long unparsedLength =
                header.ActualPropertyLength;

            if (header.PropertyCount > 0)
            {
                FBXPropertyReader.TryReadProperties(
                    parser,
                    header,
                    out _,
                    out unparsedLength);
            }
            else
            {
                unparsedLength = 0;
            }

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
                    case "FBXHeaderVersion":
                        AddSingleInt32(
                            parser,
                            childHeader,
                            "FBXHeaderVersion",
                            dataLines);
                        break;

                    case "FBXVersion":
                        AddFbxVersion(
                            parser,
                            childHeader,
                            dataLines);
                        break;

                    case "EncryptionType":
                        AddEncryptionType(
                            parser,
                            childHeader,
                            dataLines);
                        break;

                    case "CreationTimeStamp":
                        AddCreationTimeStampSummary(
                            parser,
                            child,
                            dataLines);
                        break;

                    case "Creator":
                        AddSingleString(
                            parser,
                            childHeader,
                            "Creator",
                            dataLines);
                        break;

                    case "SceneInfo":
                        AddSceneInfoSummary(
                            parser,
                            child,
                            dataLines);
                        break;

                    // TODO: Properties70。
                }
            }

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The FBXHeaderExtension node is truncated."
                    ));
            }

            // 这里只代表 HeaderExtension 自身的未解析属性区。
            // 子节点不会计入此项。
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
                    "HeaderExtension",
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
        /// 独立解析 CreationTimeStamp 节点。
        /// </summary>
        public static ParseResult ParseCreationTimeStamp(
            FBXParser parser,
            Node node,
            FBXNodeHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines =
                new List<(string K, string V)>();

            long unparsedLength =
                header.ActualPropertyLength;

            if (header.PropertyCount > 0)
            {
                FBXPropertyReader.TryReadProperties(
                    parser,
                    header,
                    out _,
                    out unparsedLength);
            }
            else
            {
                unparsedLength = 0;
            }

            AddCreationTimeStampFields(
                parser,
                node,
                dataLines);

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The CreationTimeStamp node is truncated."
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
                    "CreationTimeStamp",
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
        /// 独立解析 SceneInfo 节点。
        /// </summary>
        public static ParseResult ParseSceneInfo(
            FBXParser parser,
            Node node,
            FBXNodeHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines =
                new List<(string K, string V)>();

            long unparsedLength =
                header.ActualPropertyLength;

            if (header.PropertyCount > 0)
            {
                if (FBXPropertyReader.TryReadProperties(
                        parser,
                        header,
                        out List<FBXPropertyValue> properties,
                        out long remaining))
                {
                    unparsedLength = remaining;

                    AddSceneInfoProperties(
                        properties,
                        dataLines);
                }
            }
            else
            {
                unparsedLength = 0;
            }

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
                    case "Type":
                        AddSingleString(
                            parser,
                            childHeader,
                            "Type",
                            dataLines);
                        break;

                    case "Version":
                        AddSingleInt32(
                            parser,
                            childHeader,
                            "Version",
                            dataLines);
                        break;

                    case "MetaData":
                        AddMetadataSummary(
                            parser,
                            child,
                            dataLines);
                        break;

                    // TODO: Properties70。
                }
            }

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The SceneInfo node is truncated."
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
                    "SceneInfo",
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
        /// 独立解析 MetaData 节点。
        /// </summary>
        public static ParseResult ParseMetaData(
            FBXParser parser,
            Node node,
            FBXNodeHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines =
                new List<(string K, string V)>();

            long unparsedLength =
                header.ActualPropertyLength;

            if (header.PropertyCount > 0)
            {
                FBXPropertyReader.TryReadProperties(
                    parser,
                    header,
                    out _,
                    out unparsedLength);
            }
            else
            {
                unparsedLength = 0;
            }

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
                    case "Title":
                    case "Subject":
                    case "Author":
                    case "Keywords":
                    case "Revision":
                    case "Comment":
                        AddNodeProperty(
                            parser,
                            childHeader,
                            childHeader.Name,
                            dataLines);
                        break;
                }
            }

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The MetaData node is truncated."
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
                    "MetaData",
                    header.Name),

                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,

                RawData = parser.CreateRawStream(
                    header.NodeStart,
                    (long)node.Length),
            };
        }

        private static void AddFbxVersion(
            FBXParser parser,
            in FBXNodeHeader header,
            List<(string K, string V)> dataLines)
        {
            if (!TryGetSingleInt32(
                    parser,
                    header,
                    out int value))
            {
                return;
            }

            dataLines.Add(
                (
                    "FBXVersion",
                    value.ToString(
                        CultureInfo.InvariantCulture)
                ));

            dataLines.Add(
                (
                    "<FBXVersion>",
                    FormatFbxVersion(value)
                ));
        }

        private static void AddEncryptionType(
            FBXParser parser,
            in FBXNodeHeader header,
            List<(string K, string V)> dataLines)
        {
            if (!TryGetSingleInt32(
                    parser,
                    header,
                    out int value))
            {
                return;
            }

            dataLines.Add(
                (
                    "EncryptionType",
                    value.ToString(
                        CultureInfo.InvariantCulture)
                ));

            dataLines.Add(
                (
                    "<EncryptionType>",
                    DescribeEncryptionType(value)
                ));
        }

        private static void AddCreationTimeStampSummary(
            FBXParser parser,
            Node node,
            List<(string K, string V)> dataLines)
        {
            int? year = null;
            int? month = null;
            int? day = null;
            int? hour = null;
            int? minute = null;
            int? second = null;
            int? millisecond = null;

            foreach (Node child in node.SubNodes)
            {
                if (!parser.TryGetNodeHeader(
                        child,
                        out FBXNodeHeader header))
                {
                    continue;
                }

                if (!TryGetSingleInt32(
                        parser,
                        header,
                        out int value))
                {
                    continue;
                }

                switch (header.Name)
                {
                    case "Year":
                        year = value;
                        break;

                    case "Month":
                        month = value;
                        break;

                    case "Day":
                        day = value;
                        break;

                    case "Hour":
                        hour = value;
                        break;

                    case "Minute":
                        minute = value;
                        break;

                    case "Second":
                        second = value;
                        break;

                    case "Millisecond":
                        millisecond = value;
                        break;
                }
            }

            if (!TryFormatTimestamp(
                    year,
                    month,
                    day,
                    hour,
                    minute,
                    second,
                    millisecond,
                    out string timestamp))
            {
                return;
            }

            dataLines.Add(
                (
                    "<CreationTimeStamp>",
                    timestamp
                ));
        }

        private static void AddSceneInfoSummary(
            FBXParser parser,
            Node node,
            List<(string K, string V)> dataLines)
        {
            if (!parser.TryGetNodeHeader(
                    node,
                    out FBXNodeHeader header))
            {
                return;
            }

            if (FBXPropertyReader.TryReadProperties(
                    parser,
                    header,
                    out List<FBXPropertyValue> properties,
                    out _))
            {
                AddSceneInfoProperties(
                    properties,
                    dataLines);
            }

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
                    case "Type":
                        AddSingleString(
                            parser,
                            childHeader,
                            "Type",
                            dataLines);
                        break;

                    case "Version":
                        AddSingleInt32(
                            parser,
                            childHeader,
                            "Version",
                            dataLines);
                        break;

                    case "MetaData":
                        AddMetadataSummary(
                            parser,
                            child,
                            dataLines);
                        break;
                }
            }
        }

        private static void AddCreationTimeStampFields(
            FBXParser parser,
            Node node,
            List<(string K, string V)> dataLines)
        {
            int? version = null;
            int? year = null;
            int? month = null;
            int? day = null;
            int? hour = null;
            int? minute = null;
            int? second = null;
            int? millisecond = null;

            foreach (Node child in node.SubNodes)
            {
                if (!parser.TryGetNodeHeader(
                        child,
                        out FBXNodeHeader childHeader))
                {
                    continue;
                }

                if (!TryGetSingleInt32(
                        parser,
                        childHeader,
                        out int value))
                {
                    continue;
                }

                switch (childHeader.Name)
                {
                    case "Version":
                        version = value;
                        dataLines.Add(
                            (
                                "Version",
                                value.ToString(
                                    CultureInfo.InvariantCulture)
                            ));
                        break;

                    case "Year":
                        year = value;
                        dataLines.Add(
                            (
                                "Year",
                                value.ToString(
                                    CultureInfo.InvariantCulture)
                            ));
                        break;

                    case "Month":
                        month = value;
                        dataLines.Add(
                            (
                                "Month",
                                value.ToString(
                                    CultureInfo.InvariantCulture)
                            ));
                        break;

                    case "Day":
                        day = value;
                        dataLines.Add(
                            (
                                "Day",
                                value.ToString(
                                    CultureInfo.InvariantCulture)
                            ));
                        break;

                    case "Hour":
                        hour = value;
                        dataLines.Add(
                            (
                                "Hour",
                                value.ToString(
                                    CultureInfo.InvariantCulture)
                            ));
                        break;

                    case "Minute":
                        minute = value;
                        dataLines.Add(
                            (
                                "Minute",
                                value.ToString(
                                    CultureInfo.InvariantCulture)
                            ));
                        break;

                    case "Second":
                        second = value;
                        dataLines.Add(
                            (
                                "Second",
                                value.ToString(
                                    CultureInfo.InvariantCulture)
                            ));
                        break;

                    case "Millisecond":
                        millisecond = value;
                        dataLines.Add(
                            (
                                "Millisecond",
                                value.ToString(
                                    CultureInfo.InvariantCulture)
                            ));
                        break;
                }
            }

            if (TryFormatTimestamp(
                    year,
                    month,
                    day,
                    hour,
                    minute,
                    second,
                    millisecond,
                    out string timestamp))
            {
                dataLines.Add(
                    (
                        "<CreationTimeStamp>",
                        timestamp
                    ));
            }

            _ = version;
        }

        private static void AddMetadataSummary(
            FBXParser parser,
            Node node,
            List<(string K, string V)> dataLines)
        {
            foreach (Node child in node.SubNodes)
            {
                if (!parser.TryGetNodeHeader(
                        child,
                        out FBXNodeHeader header))
                {
                    continue;
                }

                switch (header.Name)
                {
                    case "Version":
                    case "Title":
                    case "Subject":
                    case "Author":
                    case "Keywords":
                    case "Revision":
                    case "Comment":
                        AddNodeProperty(
                            parser,
                            header,
                            header.Name,
                            dataLines);
                        break;
                }
            }
        }

        private static void AddSceneInfoProperties(
            List<FBXPropertyValue> properties,
            List<(string K, string V)> dataLines)
        {
            if (properties.Count >= 1 &&
                properties[0].Value is string sceneName)
            {
                dataLines.Add(
                    (
                        "SceneInfo",
                        sceneName
                    ));
            }

            if (properties.Count >= 2 &&
                properties[1].Value is string sceneClass)
            {
                dataLines.Add(
                    (
                        "<SceneInfo>",
                        sceneClass
                    ));
            }
        }

        private static void AddNodeProperty(
            FBXParser parser,
            in FBXNodeHeader header,
            string fieldName,
            List<(string K, string V)> dataLines)
        {
            if (TryGetSingleString(
                    parser,
                    header,
                    out string stringValue))
            {
                dataLines.Add(
                    (
                        fieldName,
                        stringValue
                    ));

                return;
            }

            if (TryGetSingleInt32(
                    parser,
                    header,
                    out int intValue))
            {
                dataLines.Add(
                    (
                        fieldName,
                        intValue.ToString(
                            CultureInfo.InvariantCulture)
                    ));
            }
        }

        private static void AddSingleInt32(
            FBXParser parser,
            in FBXNodeHeader header,
            string fieldName,
            List<(string K, string V)> dataLines)
        {
            if (TryGetSingleInt32(
                    parser,
                    header,
                    out int value))
            {
                dataLines.Add(
                    (
                        fieldName,
                        value.ToString(
                            CultureInfo.InvariantCulture)
                    ));
            }
        }

        private static void AddSingleString(
            FBXParser parser,
            in FBXNodeHeader header,
            string fieldName,
            List<(string K, string V)> dataLines)
        {
            if (TryGetSingleString(
                    parser,
                    header,
                    out string value))
            {
                dataLines.Add(
                    (
                        fieldName,
                        value
                    ));
            }
        }

        private static bool TryGetSingleInt32(
            FBXParser parser,
            in FBXNodeHeader header,
            out int value)
        {
            value = 0;

            if (!FBXPropertyReader.TryReadProperties(
                    parser,
                    header,
                    out List<FBXPropertyValue> properties,
                    out long remaining) ||
                remaining != 0 ||
                properties.Count != 1)
            {
                return false;
            }

            switch (properties[0].Value)
            {
                case int int32:
                    value = int32;
                    return true;

                case short int16:
                    value = int16;
                    return true;

                case long int64
                    when int64 >= int.MinValue &&
                         int64 <= int.MaxValue:
                    value = (int)int64;
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryGetSingleString(
            FBXParser parser,
            in FBXNodeHeader header,
            out string value)
        {
            value = string.Empty;

            if (!FBXPropertyReader.TryReadProperties(
                    parser,
                    header,
                    out List<FBXPropertyValue> properties,
                    out long remaining) ||
                remaining != 0 ||
                properties.Count != 1 ||
                properties[0].Value is not string text)
            {
                return false;
            }

            value = text;
            return true;
        }

        private static string FormatFbxVersion(
            int version)
        {
            if (version <= 0)
                return "Unknown";

            int major = version / 1000;
            int minor = version % 1000 / 100;

            return $"FBX {major}.{minor}";
        }

        private static string DescribeEncryptionType(
            int value)
        {
            return value switch
            {
                0 => "None",
                _ => "Unknown or unsupported",
            };
        }

        private static bool TryFormatTimestamp(
            int? year,
            int? month,
            int? day,
            int? hour,
            int? minute,
            int? second,
            int? millisecond,
            out string timestamp)
        {
            timestamp = string.Empty;

            if (!year.HasValue ||
                !month.HasValue ||
                !day.HasValue ||
                !hour.HasValue ||
                !minute.HasValue ||
                !second.HasValue ||
                !millisecond.HasValue)
            {
                return false;
            }

            try
            {
                var value = new DateTime(
                    year.Value,
                    month.Value,
                    day.Value,
                    hour.Value,
                    minute.Value,
                    second.Value,
                    DateTimeKind.Unspecified);

                value = value.AddMilliseconds(
                    millisecond.Value);

                timestamp = value.ToString(
                    "yyyy-MM-dd HH:mm:ss.fff",
                    CultureInfo.InvariantCulture);

                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                timestamp =
                    $"{year.Value:D4}-{month.Value:D2}-{day.Value:D2} " +
                    $"{hour.Value:D2}:{minute.Value:D2}:{second.Value:D2}." +
                    $"{millisecond.Value:D3} (invalid date/time)";

                return true;
            }
        }
    }
}