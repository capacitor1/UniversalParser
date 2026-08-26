using System;
using System.Globalization;

namespace UniversalParser.Src.Parser.EBML.Elements
{
    /// <summary>
    /// Matroska Segment Information Elements。
    ///
    /// 所有方法只解析当前 Element 自身的数据，不读取其他 Info 子 Element。
    /// 因此 Duration 不会与 TimestampScale 组合计算实际时长。
    /// </summary>
    internal static class SegmentInfo
    {
        // ============================================================
        // Master Elements
        // ============================================================

        public static ParseResult ParseInfo(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(parser, node, header, "Info");

        public static ParseResult ParseChapterTranslate(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(parser, node, header, "ChapterTranslate");

        // ============================================================
        // Segment identifiers
        // ============================================================

        public static ParseResult ParseSegmentUUID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseBinary(
                parser,
                node,
                header,
                "SegmentUUID",
                FormatUuid,
                requiredLength: 16);

        public static ParseResult ParseSegmentFilename(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "SegmentFilename");

        public static ParseResult ParsePrevUUID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseBinary(
                parser,
                node,
                header,
                "PrevUUID",
                FormatUuid,
                requiredLength: 16);

        public static ParseResult ParsePrevFilename(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "PrevFilename");

        public static ParseResult ParseNextUUID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseBinary(
                parser,
                node,
                header,
                "NextUUID",
                FormatUuid,
                requiredLength: 16);

        public static ParseResult ParseNextFilename(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "NextFilename");

        public static ParseResult ParseSegmentFamily(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseBinary(
                parser,
                node,
                header,
                "SegmentFamily",
                FormatUuid,
                requiredLength: 16);

        // ============================================================
        // Chapter translation
        // ============================================================

        public static ParseResult ParseChapterTranslateID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseBinary(
                parser,
                node,
                header,
                "ChapterTranslateID");

        public static ParseResult ParseChapterTranslateCodec(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "ChapterTranslateCodec",
                FormatChapterTranslateCodec);

        public static ParseResult ParseChapterTranslateEditionUID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "ChapterTranslateEditionUID");

        // ============================================================
        // Timing and general information
        // ============================================================

        public static ParseResult ParseTimestampScale(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TimestampScale");

        public static ParseResult ParseDuration(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "Duration");

        public static ParseResult ParseDateUTC(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseDate(
                parser,
                node,
                header,
                "DateUTC");

        public static ParseResult ParseTitle(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "Title");

        public static ParseResult ParseMuxingApp(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "MuxingApp");

        public static ParseResult ParseWritingApp(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "WritingApp");

        // ============================================================
        // Readable formatters
        // ============================================================

        /// <summary>
        /// Matroska UUID 使用网络字节顺序显示，不能直接用 Guid(ReadOnlySpan&lt;byte&gt;)，
        /// 因为 Guid 构造函数会对前三个字段采用 Windows 混合字节序。
        /// </summary>
        private static string? FormatUuid(ReadOnlySpan<byte> value)
        {
            if (value.Length != 16)
                return null;

            return string.Create(
                36,
                value.ToArray(),
                static (destination, bytes) =>
                {
                    const string hex = "0123456789ABCDEF";

                    int source = 0;
                    int target = 0;

                    for (int i = 0; i < 16; i++)
                    {
                        if (i is 4 or 6 or 8 or 10)
                            destination[target++] = '-';

                        byte current = bytes[source++];
                        destination[target++] = hex[current >> 4];
                        destination[target++] = hex[current & 0x0F];
                    }
                });
        }

        private static string? FormatChapterTranslateCodec(ulong value) =>
            value switch
            {
                0 => "Matroska Script",
                1 => "DVD-menu",
                _ => null,
            };
    }
}