using System;

namespace UniversalParser.Src.Parser.EBML.Elements
{
    /// <summary>
    /// Matroska / WebM Tags 及其子 Element 解析器。
    ///
    /// Tags
    /// └── Tag
    ///     ├── Targets
    ///     │   ├── TargetTypeValue
    ///     │   ├── TargetType
    ///     │   ├── TagTrackUID
    ///     │   ├── TagEditionUID
    ///     │   ├── TagChapterUID
    ///     │   └── TagAttachmentUID
    ///     └── SimpleTag
    ///         ├── TagName
    ///         ├── TagLanguage
    ///         ├── TagLanguageBCP47
    ///         ├── TagDefault
    ///         ├── TagDefaultBogus
    ///         ├── TagString
    ///         └── TagBinary
    ///
    /// 所有方法只读取当前 Element 的自身负载。
    /// </summary>
    internal static class TagElements
    {
        // ============================================================
        // Tags / Tag / Targets / SimpleTag
        // ============================================================

        public static ParseResult ParseTags(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "Tags");

        public static ParseResult ParseTag(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "Tag");

        public static ParseResult ParseTargets(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "Targets");

        public static ParseResult ParseSimpleTag(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "SimpleTag");

        // ============================================================
        // Targets
        // ============================================================

        public static ParseResult ParseTargetTypeValue(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TargetTypeValue",
                FormatTargetTypeValue);

        public static ParseResult ParseTargetType(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "TargetType");

        public static ParseResult ParseTagTrackUID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TagTrackUID");

        public static ParseResult ParseTagEditionUID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TagEditionUID");

        public static ParseResult ParseTagChapterUID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TagChapterUID");

        public static ParseResult ParseTagAttachmentUID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TagAttachmentUID");

        // ============================================================
        // SimpleTag
        // ============================================================

        public static ParseResult ParseTagName(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "TagName");

        /// <summary>
        /// TagLanguage 是 Matroska 中的 UTF-8 字符串。
        /// </summary>
        public static ParseResult ParseTagLanguage(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "TagLanguage");

        public static ParseResult ParseTagLanguageBCP47(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "TagLanguageBCP47");

        public static ParseResult ParseTagDefault(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TagDefault",
                FormatBoolean);

        /// <summary>
        /// 已废弃的 TagDefaultBogus Element。
        /// 仍按官方字段名解析，便于展示旧文件。
        /// </summary>
        public static ParseResult ParseTagDefaultBogus(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TagDefaultBogus",
                FormatBoolean);

        public static ParseResult ParseTagString(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "TagString");

        /// <summary>
        /// TagBinary 的实际内容不解析，仅呈现未解析负载长度。
        /// </summary>
        public static ParseResult ParseTagBinary(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnparsed(
                parser,
                node,
                header,
                "TagBinary");

        // ============================================================
        // Readable formatters
        // ============================================================

        private static string? FormatBoolean(ulong value) =>
            value switch
            {
                0 => "false",
                1 => "true",
                _ => null,
            };

        private static string? FormatTargetTypeValue(ulong value) =>
            value switch
            {
                10 => "COLLECTION",
                20 => "EDITION",
                30 => "ALBUM",
                40 => "OPERATION",
                50 => "CONCERT",
                60 => "MOVIE",
                70 => "PART",
                71 => "SESSION",
                72 => "TRACK",
                73 => "SUBTRACK",
                74 => "SHOT",
                75 => "SUBSHOT",
                _ => null,
            };
    }
}