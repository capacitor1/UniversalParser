using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>各 chunk 解析器共用的通用辅助。</summary>
    internal static class ChunkUtil
    {
        /// <summary>把位标志展开为 "NAME_A | NAME_B" 形式，未知位以 reserved 形式列出。</summary>
        public static string DescribeFlags(uint value, (uint Mask, string Name)[] definitions)
        {
            if (value == 0) return "None";

            var builder = new StringBuilder();
            uint known = 0;

            foreach ((uint mask, string name) in definitions)
            {
                known |= mask;
                if ((value & mask) == 0) continue;
                if (builder.Length > 0) builder.Append(" | ");
                builder.Append(name);
            }

            uint unknown = value & ~known;
            if (unknown != 0)
            {
                if (builder.Length > 0) builder.Append(" | ");
                builder.Append($"reserved 0x{unknown:X8}");
            }

            return builder.ToString();
        }

        /// <summary>
        /// 读取一个 FourCC 字段。四字节均为可打印 ASCII 时返回 4CC 文本，
        /// 否则返回 null（表示该字段实际承载的是数值而非 FourCC）。
        /// </summary>
        public static string? TryReadFourCCField(ReadOnlySpan<byte> span) =>
            RIFFUtil.IsPrintableFourCC(span) ? RIFFUtil.DecodeFourCC(span) : null;

        /// <summary>
        /// 格式化一个“既可能是 FourCC 也可能是数值”的字段的原生值。
        /// 可打印时输出 4CC 原文，否则输出十六进制，保证原生项始终能对应文件字节。
        /// </summary>
        public static string FormatFourCCField(ReadOnlySpan<byte> span, bool bigEndian)
        {
            string? fourCC = TryReadFourCCField(span);
            return fourCC is not null
                ? RIFFUtil.Sanitize(fourCC)
                : $"0x{RIFFUtil.ReadUInt32(span, bigEndian):X8}";
        }

        public static int ReadInt32(ReadOnlySpan<byte> span, bool bigEndian) =>
            unchecked((int)RIFFUtil.ReadUInt32(span, bigEndian));

        public static short ReadInt16(ReadOnlySpan<byte> span, bool bigEndian) =>
            unchecked((short)RIFFUtil.ReadUInt16(span, bigEndian));

        /// <summary>把节点负载读入数组，返回实际读到的字节数。</summary>
        public static int ReadPayload(RIFFParser parser, in RIFFChunkHeader header, int maxBytes, out byte[] buffer)
        {
            int want = (int)Math.Min(maxBytes, Math.Max(0, header.PayloadLength));
            buffer = want > 0 ? new byte[want] : [];
            return want > 0 ? parser.ReadAt(header.PayloadStart, buffer) : 0;
        }

        /// <summary>把剩余未解析字节记为 &lt;PayloadLength&gt;（仅在确有剩余时）。</summary>
        public static void AddUnparsedLength(
            List<(string K, string V)> dataLines, in RIFFChunkHeader header, long parsedBytes)
        {
            long unparsed = header.PayloadLength - parsedBytes;
            if (unparsed > 0)
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(unparsed)));
        }

        public static bool IsAllZero(ReadOnlySpan<byte> span)
        {
            foreach (byte b in span)
                if (b != 0) return false;
            return true;
        }

        /// <summary>大写连续十六进制，用于 UMID / 摘要等二进制字段。</summary>
        public static string Hex(ReadOnlySpan<byte> span)
        {
            if (span.IsEmpty) return string.Empty;

            Span<char> chars = span.Length <= 128 ? stackalloc char[span.Length * 2] : new char[span.Length * 2];
            const string digits = "0123456789ABCDEF";
            for (int i = 0; i < span.Length; i++)
            {
                chars[i * 2] = digits[span[i] >> 4];
                chars[i * 2 + 1] = digits[span[i] & 0xF];
            }
            return new string(chars);
        }

        /// <summary>预留/填充字段：全零时给出简短说明，否则完整十六进制（取证需要看到实际字节）。</summary>
        public static string FormatOpaqueField(ReadOnlySpan<byte> span) =>
            IsAllZero(span) ? $"{span.Length} zero bytes" : Hex(span);

        /// <summary>NUL 之后是否还有非零字节 —— 常见的残留数据痕迹。</summary>
        public static bool HasDataAfterTerminator(ReadOnlySpan<byte> span)
        {
            int nul = span.IndexOf((byte)0);
            if (nul < 0) return false;
            return !IsAllZero(span[(nul + 1)..]);
        }

        /// <summary>CSV 单元格内的可读文本不得含逗号或引号，否则破坏列对齐。</summary>
        public static string CsvSafe(string text)
        {
            if (text.IndexOfAny([',', '"']) < 0) return text;
            return text.Replace(',', ';').Replace('"', '\'');
        }

        public static ParseResult Build(
            RIFFParser parser,
            Node node,
            in RIFFChunkHeader header,
            string readableName,
            List<(string K, string V)> dataLines) =>
            new()
            {
                Title = RIFFUtil.MakeTitle(readableName, header.Id),
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = parser.CreateRawStream(header.ChunkStart, (long)node.Length),
            };
    }
}