using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.EBML.Elements
{
    /// <summary>
    /// EBML Header 及其字段解析器。
    ///
    /// EBML Header 的结构：
    ///
    /// EBML
    /// ├── EBMLVersion
    /// ├── EBMLReadVersion
    /// ├── EBMLMaxIDLength
    /// ├── EBMLMaxSizeLength
    /// ├── DocType
    /// ├── DocTypeVersion
    /// └── DocTypeReadVersion
    ///
    /// Header 本身是 Master Element，子 Element 已由 EBMLParser 展开，
    /// 因此 EBML Header 不显示 PayloadLength。
    /// </summary>
    internal static class EBMLHeader
    {
        private const ulong EbmlId = 0x1A45DFA3;
        private const ulong EbmlVersionId = 0x4286;
        private const ulong EbmlReadVersionId = 0x42F7;
        private const ulong EbmlMaxIdLengthId = 0x42F2;
        private const ulong EbmlMaxSizeLengthId = 0x42F3;
        private const ulong DocTypeId = 0x4282;
        private const ulong DocTypeVersionId = 0x4287;
        private const ulong DocTypeReadVersionId = 0x4285;

        private static readonly HashSet<ulong> RequiredHeaderElements =
        [
            EbmlVersionId,
            EbmlReadVersionId,
            EbmlMaxIdLengthId,
            EbmlMaxSizeLengthId,
            DocTypeId,
            DocTypeVersionId,
            DocTypeReadVersionId,
        ];

        // ============================================================
        // EBML Header Master Element
        // ============================================================

        public static ParseResult ParseHeader(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            if (header.ElementId != EbmlId)
            {
                dataLines.Add((
                    "<Warning>",
                    "The Element ID does not match the EBML Header ID."));
            }

            if (!header.IsMaster)
            {
                dataLines.Add((
                    "<Warning>",
                    "The EBML Header is not recognized as a Master Element."));
            }

            if (header.IsUnknownSize)
            {
                dataLines.Add((
                    "<Warning>",
                    "The EBML Header must not use an unknown Data Size."));
            }

            if (header.IsTruncated)
            {
                dataLines.Add((
                    "<Warning>",
                    "The EBML Header is truncated."));
            }

            AddMissingElementWarning(node, dataLines);

            return new ParseResult
            {
                Title = EBMLUtil.MakeTitle(
                    "EBMLHeader",
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

        private static void AddMissingElementWarning(
            Node node,
            List<(string K, string V)> dataLines)
        {
            var present = new HashSet<ulong>();

            foreach (Node child in node.SubNodes)
            {
                // 子节点的 NodeName 是显示名，不作为可靠 ID。
                // 这里仅用于发现标准 Header 子节点。
                //
                // 实际 Dispatcher 会根据真实文件头分发，
                // 因此通过名称匹配已知显示名即可避免修改 Node 类型。
                switch (child.NodeName)
                {
                    case "EBMLVersion (0x4286)":
                        present.Add(EbmlVersionId);
                        break;

                    case "EBMLReadVersion (0x42F7)":
                        present.Add(EbmlReadVersionId);
                        break;

                    case "EBMLMaxIDLength (0x42F2)":
                        present.Add(EbmlMaxIdLengthId);
                        break;

                    case "EBMLMaxSizeLength (0x42F3)":
                        present.Add(EbmlMaxSizeLengthId);
                        break;

                    case "DocType (0x4282)":
                        present.Add(DocTypeId);
                        break;

                    case "DocTypeVersion (0x4287)":
                        present.Add(DocTypeVersionId);
                        break;

                    case "DocTypeReadVersion (0x4285)":
                        present.Add(DocTypeReadVersionId);
                        break;
                }
            }

            if (present.Count == RequiredHeaderElements.Count)
                return;

            var missing = new List<string>();

            if (!present.Contains(EbmlVersionId))
                missing.Add("EBMLVersion");

            if (!present.Contains(EbmlReadVersionId))
                missing.Add("EBMLReadVersion");

            if (!present.Contains(EbmlMaxIdLengthId))
                missing.Add("EBMLMaxIDLength");

            if (!present.Contains(EbmlMaxSizeLengthId))
                missing.Add("EBMLMaxSizeLength");

            if (!present.Contains(DocTypeId))
                missing.Add("DocType");

            if (!present.Contains(DocTypeVersionId))
                missing.Add("DocTypeVersion");

            if (!present.Contains(DocTypeReadVersionId))
                missing.Add("DocTypeReadVersion");

            if (missing.Count > 0)
            {
                dataLines.Add((
                    "<MissingElements>",
                    string.Join(", ", missing)));
            }
        }

        // ============================================================
        // Header fields
        // ============================================================

        public static ParseResult ParseVersion(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header)
        {
            return ParseUnsignedInteger(
                parser,
                node,
                header,
                "EBMLVersion",
                ValidateVersion);
        }

        public static ParseResult ParseReadVersion(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header)
        {
            return ParseUnsignedInteger(
                parser,
                node,
                header,
                "EBMLReadVersion",
                ValidateVersion);
        }

        public static ParseResult ParseMaxIDLength(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header)
        {
            return ParseUnsignedInteger(
                parser,
                node,
                header,
                "EBMLMaxIDLength",
                ValidateMaxIDLength);
        }

        public static ParseResult ParseMaxSizeLength(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header)
        {
            return ParseUnsignedInteger(
                parser,
                node,
                header,
                "EBMLMaxSizeLength",
                ValidateMaxSizeLength);
        }

        public static ParseResult ParseDocType(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            if (header.PayloadLength <= 0)
            {
                dataLines.Add((
                    "<Error>",
                    "DocType must contain a non-empty UTF-8 string."));

                return BuildError(
                    parser,
                    node,
                    header,
                    dataLines);
            }

            if (header.PayloadLength > int.MaxValue)
            {
                dataLines.Add((
                    "<Error>",
                    "DocType is too large to decode as a single string."));

                dataLines.Add((
                    "<PayloadLength>",
                    EBMLUtil.FormatBytes(header.PayloadLength)));

                return BuildError(
                    parser,
                    node,
                    header,
                    dataLines);
            }

            try
            {
                using EBMLReader reader = parser.CreatePayloadReader(header);

                string docType = reader.ReadUtf8String(
                    (int)header.PayloadLength);

                if (string.IsNullOrWhiteSpace(docType))
                {
                    dataLines.Add((
                        "<Warning>",
                        "DocType is empty."));
                }

                // DocType 本身是 UTF-8 原生字符串，可直接显示。
                dataLines.Add((
                    "DocType",
                    docType));

                if (header.IsTruncated)
                {
                    dataLines.Add((
                        "<Warning>",
                        "DocType is truncated."));
                }

                return Build(
                    parser,
                    node,
                    header,
                    dataLines);
            }
            catch (DecoderFallbackException)
            {
                dataLines.Add((
                    "<Error>",
                    "DocType is not valid UTF-8."));

                dataLines.Add((
                    "<PayloadLength>",
                    EBMLUtil.FormatBytes(header.PayloadLength)));

                return BuildError(
                    parser,
                    node,
                    header,
                    dataLines);
            }
            catch (Exception ex)
            {
                dataLines.Add((
                    "<Error>",
                    ex.Message));

                dataLines.Add((
                    "<PayloadLength>",
                    EBMLUtil.FormatBytes(header.PayloadLength)));

                return BuildError(
                    parser,
                    node,
                    header,
                    dataLines);
            }
        }

        public static ParseResult ParseDocTypeVersion(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header)
        {
            return ParseUnsignedInteger(
                parser,
                node,
                header,
                "DocTypeVersion",
                ValidateVersion);
        }

        public static ParseResult ParseDocTypeReadVersion(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header)
        {
            return ParseUnsignedInteger(
                parser,
                node,
                header,
                "DocTypeReadVersion",
                ValidateVersion);
        }

        // ============================================================
        // Generic Header integer parser
        // ============================================================

        private static ParseResult ParseUnsignedInteger(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header,
            string elementName,
            Action<ulong, List<(string K, string V)>>? validator)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            if (header.PayloadLength is < 1 or > 8)
            {
                dataLines.Add((
                    "<Error>",
                    $"{elementName} must contain between 1 and 8 bytes."));

                if (header.PayloadLength > 0)
                {
                    dataLines.Add((
                        "<PayloadLength>",
                        EBMLUtil.FormatBytes(header.PayloadLength)));
                }

                return BuildError(
                    parser,
                    node,
                    header,
                    dataLines);
            }

            try
            {
                using EBMLReader reader = parser.CreatePayloadReader(header);

                ulong value = reader.ReadUnsignedInteger(
                    (int)header.PayloadLength);

                // EBML Header 的整数原生值用十进制表示。
                dataLines.Add((
                    elementName,
                    value.ToString()));

                validator?.Invoke(value, dataLines);

                if (header.IsTruncated)
                {
                    dataLines.Add((
                        "<Warning>",
                        $"{elementName} is truncated."));
                }

                return Build(
                    parser,
                    node,
                    header,
                    dataLines);
            }
            catch (Exception ex)
            {
                dataLines.Add((
                    "<Error>",
                    ex.Message));

                dataLines.Add((
                    "<PayloadLength>",
                    EBMLUtil.FormatBytes(header.PayloadLength)));

                return BuildError(
                    parser,
                    node,
                    header,
                    dataLines);
            }
        }

        // ============================================================
        // Validation
        // ============================================================

        private static void ValidateVersion(
            ulong value,
            List<(string K, string V)> dataLines)
        {
            if (value == 0)
            {
                dataLines.Add((
                    "<Warning>",
                    "EBML version values must be greater than zero."));
            }
        }

        private static void ValidateMaxIDLength(
            ulong value,
            List<(string K, string V)> dataLines)
        {
            if (value is < 1 or > EBMLUtil.MaxElementIdLength)
            {
                dataLines.Add((
                    "<Warning>",
                    $"EBMLMaxIDLength must be between 1 and {EBMLUtil.MaxElementIdLength}."));
            }
        }

        private static void ValidateMaxSizeLength(
            ulong value,
            List<(string K, string V)> dataLines)
        {
            if (value is < 1 or > EBMLUtil.MaxDataSizeLength)
            {
                dataLines.Add((
                    "<Warning>",
                    $"EBMLMaxSizeLength must be between 1 and {EBMLUtil.MaxDataSizeLength}."));
            }
        }

        // ============================================================
        // Result builders
        // ============================================================

        private static ParseResult Build(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header,
            List<(string K, string V)> dataLines)
        {
            return new ParseResult
            {
                Title = EBMLUtil.MakeTitle(
                    EBMLSchema.GetName(header.ElementId),
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

        private static ParseResult BuildError(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header,
            List<(string K, string V)> dataLines)
        {
            return new ParseResult
            {
                Title = EBMLUtil.MakeTitle(
                    EBMLSchema.GetName(header.ElementId),
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
    }
}