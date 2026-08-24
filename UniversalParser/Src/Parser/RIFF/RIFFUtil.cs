using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.RIFF
{
    /// <summary>RIFF 相关的公共常量与工具方法。</summary>
    internal static class RIFFUtil
    {
        public const int FourCCSize = 4;
        public const int ChunkHeaderSize = 8;           // ckID(4) + ckSize(4)
        public const int TypedContainerHeaderSize = 12; // + formType/listType(4)

        /// <summary>合法的根签名。</summary>
        public static readonly HashSet<string> RootSignatures = new(StringComparer.Ordinal)
        {
            "RIFF", // 标准（小端）
            "RIFX", // 大端变体
            "RF64", // >4GB WAV（ckSize 为 0xFFFFFFFF，真实大小在 ds64 中）
            "BW64", // EBU BW64
        };

        /// <summary>
        /// “带类型码的容器”：负载的前 4 字节是 form/list 类型码，之后才是子块序列。
        /// 注意：movi / strl / INFO 等是 LIST 的**类型码**，不是块 ID，不能放进来。
        /// </summary>
        public static readonly HashSet<string> TypedContainers = new(StringComparer.Ordinal)
        {
            "RIFF", "RIFX", "RF64", "BW64", "LIST",
        };

        /// <summary>RIFF 要求块之间按 2 字节对齐（填充字节不计入块自身 ckSize）。</summary>
        public static long Align2(long value) => value + (value & 1L);

        /// <summary>4CC 必须是可打印 ASCII（0x20-0x7E），用于识别损坏数据。</summary>
        public static bool IsPrintableFourCC(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != FourCCSize) return false;
            for (int i = 0; i < FourCCSize; i++)
            {
                byte c = bytes[i];
                if (c < 0x20 || c > 0x7E) return false;
            }
            return true;
        }

        /// <summary>用 Latin1 解码，保证字节与字符一一对应（ASCII 会把非 ASCII 变成 '?'，丢信息）。</summary>
        public static string DecodeFourCC(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);

        /// <summary>成功返回 4CC，非可打印返回 null。</summary>
        public static string? TryDecodeFourCC(byte[] buffer, int offset)
        {
            if (buffer is null || offset < 0 || offset + FourCCSize > buffer.Length) return null;
            var span = new ReadOnlySpan<byte>(buffer, offset, FourCCSize);
            return IsPrintableFourCC(span) ? DecodeFourCC(span) : null;
        }

        /// <summary>把不可打印字符替换掉，避免污染 UI 的节点名 / 标题。</summary>
        public static string Sanitize(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            Span<char> buffer = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                buffer[i] = c is >= ' ' and <= '~' || c > 0x9F ? c : '.';
            }
            return new string(buffer);
        }

        /// <summary>
        /// 统一生成 ParseResult.Title，强制固定格式：无空格英文可读名 + 空格 + 单引号包裹的原始 4CC。
        /// 例：MakeTitle("Software", "ISFT") → Software 'ISFT'
        /// </summary>
        public static string MakeTitle(string readableName, string fourCC) =>
            $"{readableName} '{Sanitize(fourCC)}'";

        public static uint ReadUInt32(ReadOnlySpan<byte> span, bool bigEndian) =>
            bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(span) : BinaryPrimitives.ReadUInt32LittleEndian(span);

        public static uint ReadUInt32(byte[] buffer, int offset, bool bigEndian) =>
            ReadUInt32(new ReadOnlySpan<byte>(buffer, offset, 4), bigEndian);

        public static ushort ReadUInt16(ReadOnlySpan<byte> span, bool bigEndian) =>
            bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(span) : BinaryPrimitives.ReadUInt16LittleEndian(span);

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) return bytes.ToString();
            if (bytes < 1024) return $"{bytes} B";

            string[] units = ["KiB", "MiB", "GiB", "TiB", "PiB"];
            double value = bytes / 1024.0;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return $"{value:0.##} {units[unit]} ({bytes:N0} B)";
        }

        /// <summary>RIFF 文本约定是 CP1252，但现实中大量文件写的是 UTF-8：先试 UTF-8 严格解码，失败退回 Latin1。</summary>
        public static string DecodeText(ReadOnlySpan<byte> data)
        {
            int end = data.IndexOf((byte)0);
            if (end >= 0) data = data[..end];
            if (data.IsEmpty) return string.Empty;

            try
            {
                return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data);
            }
            catch (ArgumentException)
            {
                return Encoding.Latin1.GetString(data);
            }
        }
    }
}