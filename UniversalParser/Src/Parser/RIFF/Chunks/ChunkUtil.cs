using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.Text.Unicode;

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
        
        public static float ReadSingle(ReadOnlySpan<byte> span, bool bigEndian) =>
            BitConverter.Int32BitsToSingle(ReadInt32(span, bigEndian));

        /// <summary>IEEE-754 单精度的原生输出：最短可往返表示，非有限值输出 NaN / Infinity。</summary>
        public static string FormatSingle(float value) =>
            value.ToString("R", CultureInfo.InvariantCulture);

        private static readonly string[] PitchClassNames =
            ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

        /// <summary>MIDI 音高号转科学音名（60 = C4）；越界时如实说明而不编造音名。</summary>
        public static string FormatMidiNote(int note) =>
            note is < 0 or > 127
                ? "out of MIDI range"
                : $"{PitchClassNames[note % 12]}{note / 12 - 1}";

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

        /// <summary>
        /// 按 NUL 切分 ZSTR 序列，返回各字符串的字节范围（不含终止符）。
        /// consumed 为已确定归属的字节数（含终止符）；未终止的尾段不计入，
        /// 故调用方可用 consumed 直接喂给 AddUnparsedLength。
        /// </summary>
        public static List<(int Offset, int Length)> SplitNulTerminated(
            ReadOnlySpan<byte> span, int maxItems, out int consumed)
        {
            var items = new List<(int Offset, int Length)>();
            consumed = 0;

            while (consumed < span.Length && items.Count < maxItems)
            {
                int nul = span[consumed..].IndexOf((byte)0);
                if (nul < 0) break; // 尾段无终止符，留给调用方诊断

                items.Add((consumed, nul));
                consumed += nul + 1;
            }
            return items;
        }

        /// <summary>
        /// 未声明代码页的字节串：按 Latin-1 逐字节映射，保证无损可逆
        /// （不会像 ASCII 解码那样把高位字节吞成 '?'）。
        /// </summary>
        public static string DecodeUnknownCodePage(ReadOnlySpan<byte> span) =>
            Encoding.Latin1.GetString(span);

        /// <summary>高位字节计数。传统日文软件常用 Shift-JIS，此计数用于提示而非解码。</summary>
        public static int CountNonAscii(ReadOnlySpan<byte> span)
        {
            int count = 0;
            foreach (byte b in span)
                if (b >= 0x80) count++;
            return count;
        }
        /// <summary>字节序列是否构成合法 UTF-8。用于代码页未声明的文本字段的编码判定。</summary>
        public static bool LooksLikeUtf8(ReadOnlySpan<byte> span) => Utf8.IsValid(span);

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