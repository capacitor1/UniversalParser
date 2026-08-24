using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// AVI 专有的解码辅助。通用部分已迁至 ChunkUtil，这里保留转发以免改动既有 AVI 解析器。
    /// </summary>
    internal static class AviUtil
    {
        public static string DescribeFlags(uint value, (uint Mask, string Name)[] definitions) =>
            ChunkUtil.DescribeFlags(value, definitions);

        public static string? TryReadFourCCField(ReadOnlySpan<byte> span) =>
            ChunkUtil.TryReadFourCCField(span);

        public static string FormatFourCCField(ReadOnlySpan<byte> span, bool bigEndian) =>
            ChunkUtil.FormatFourCCField(span, bigEndian);

        public static int ReadInt32(ReadOnlySpan<byte> span, bool bigEndian) =>
            ChunkUtil.ReadInt32(span, bigEndian);

        public static short ReadInt16(ReadOnlySpan<byte> span, bool bigEndian) =>
            ChunkUtil.ReadInt16(span, bigEndian);

        public static int ReadPayload(RIFFParser parser, in RIFFChunkHeader header, int maxBytes, out byte[] buffer) =>
            ChunkUtil.ReadPayload(parser, header, maxBytes, out buffer);

        public static void AddUnparsedLength(
            List<(string K, string V)> dataLines, in RIFFChunkHeader header, long parsedBytes) =>
            ChunkUtil.AddUnparsedLength(dataLines, header, parsedBytes);

        public static ParseResult Build(
            RIFFParser parser,
            Node node,
            in RIFFChunkHeader header,
            string readableName,
            List<(string K, string V)> dataLines) =>
            ChunkUtil.Build(parser, node, header, readableName, dataLines);

        /// <summary>Windows LANGID：低 10 位为主语言，高 6 位为子语言。</summary>
        public static string DescribeLangId(ushort langId)
        {
            if (langId == 0) return "LANG_NEUTRAL";
            return $"primary language 0x{langId & 0x03FF:X3}, sublanguage 0x{(langId >> 10) & 0x3F:X2}";
        }
    }
}