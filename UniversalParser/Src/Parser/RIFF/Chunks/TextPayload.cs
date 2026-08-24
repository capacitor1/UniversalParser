using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>文本型载荷的读取、编码判定与呈现辅助。</summary>
    internal static class TextPayload
    {
        /// <summary>
        /// 结构性硬上限。文本块正常不会到这个量级，损坏文件声明的巨大长度不应导致一次性巨额分配。
        /// </summary>
        public const int DefaultLimit = 64 * 1024 * 1024;

        internal enum TextEncoding
        {
            None,
            Ascii,
            Utf8,
            Utf8Bom,
            Utf16Be,
            Utf16Le,
            Utf32Be,
            Utf32Le,
        }

        /// <summary>读取载荷，返回实际读到的字节数；unparsedBytes 为因上限或读取失败而未取得的字节数。</summary>
        public static byte[] Read(
            RIFFParser parser, in RIFFChunkHeader header, int limit, out int read, out long unparsedBytes)
        {
            long payloadLength = Math.Max(0, header.PayloadLength);
            int want = (int)Math.Min(limit, payloadLength);

            byte[] buffer = want > 0 ? new byte[want] : [];
            read = want > 0 ? parser.ReadAt(header.PayloadStart, buffer) : 0;
            unparsedBytes = payloadLength - read;
            return buffer;
        }

        /// <summary>依 BOM 判定编码；无 BOM 时按内容判定 ASCII / UTF-8 / 非文本。</summary>
        public static TextEncoding DetectEncoding(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty) return TextEncoding.None;

            if (data.Length >= 4)
            {
                if (data[0] == 0x00 && data[1] == 0x00 && data[2] == 0xFE && data[3] == 0xFF)
                    return TextEncoding.Utf32Be;
                if (data[0] == 0xFF && data[1] == 0xFE && data[2] == 0x00 && data[3] == 0x00)
                    return TextEncoding.Utf32Le;
            }

            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return TextEncoding.Utf8Bom;

            if (data.Length >= 2)
            {
                if (data[0] == 0xFE && data[1] == 0xFF) return TextEncoding.Utf16Be;
                if (data[0] == 0xFF && data[1] == 0xFE) return TextEncoding.Utf16Le;
            }

            bool ascii = true;
            foreach (byte b in data)
            {
                if (b == 0x00) return TextEncoding.None;                 // 内嵌 NUL：按无 BOM 的单字节文本处理不合理
                if (b >= 0x80) { ascii = false; continue; }
                if (b < 0x20 && b is not (0x09 or 0x0A or 0x0D)) return TextEncoding.None;
            }

            if (ascii) return TextEncoding.Ascii;

            // 含高位字节：严格 UTF-8 校验通过才认定为 UTF-8
            try
            {
                _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data);
                return TextEncoding.Utf8;
            }
            catch (ArgumentException)
            {
                return TextEncoding.None;
            }
        }

        public static string DescribeEncoding(TextEncoding encoding) => encoding switch
        {
            TextEncoding.Ascii => "US-ASCII, no byte order mark",
            TextEncoding.Utf8 => "UTF-8, no byte order mark",
            TextEncoding.Utf8Bom => "UTF-8 with byte order mark",
            TextEncoding.Utf16Be => "UTF-16 big endian with byte order mark",
            TextEncoding.Utf16Le => "UTF-16 little endian with byte order mark",
            TextEncoding.Utf32Be => "UTF-32 big endian with byte order mark",
            TextEncoding.Utf32Le => "UTF-32 little endian with byte order mark",
            _ => "Not decodable as text",
        };

        /// <summary>按判定出的编码解码；BOM 会被去除。TextEncoding.None 返回 null。</summary>
        public static string? Decode(ReadOnlySpan<byte> data, TextEncoding encoding)
        {
            return encoding switch
            {
                TextEncoding.Ascii or TextEncoding.Utf8 => Encoding.UTF8.GetString(data),
                TextEncoding.Utf8Bom => Encoding.UTF8.GetString(data[3..]),
                TextEncoding.Utf16Be => new UnicodeEncoding(true, false).GetString(data[2..]),
                TextEncoding.Utf16Le => new UnicodeEncoding(false, false).GetString(data[2..]),
                TextEncoding.Utf32Be => new UTF32Encoding(true, false).GetString(data[4..]),
                TextEncoding.Utf32Le => new UTF32Encoding(false, false).GetString(data[4..]),
                _ => null,
            };
        }

        /// <summary>
        /// 把多行文本按数组约定写入 dataLines：首行键为 name[N] 且值为第 0 行，其后键为空。
        /// 单行文本直接写成一条普通键值对。
        /// </summary>
        public static void AppendText(List<(string K, string V)> dataLines, string name, string text)
        {
            ArgumentNullException.ThrowIfNull(dataLines);

            if (text.Length == 0)
            {
                dataLines.Add((name, "(empty)"));
                return;
            }

            string[] lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
            if (lines.Length == 1)
            {
                dataLines.Add((name, RIFFUtil.Sanitize(lines[0])));
                return;
            }

            dataLines.EnsureCapacity(dataLines.Count + lines.Length);
            dataLines.Add(($"{name}[{lines.Length}]", RIFFUtil.Sanitize(lines[0])));
            for (int i = 1; i < lines.Length; i++)
                dataLines.Add((string.Empty, RIFFUtil.Sanitize(lines[i])));
        }
    }
}