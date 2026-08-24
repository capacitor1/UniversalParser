using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    internal enum AviCompression
    {
        NotApplicable,
        Uncompressed,
        Compressed,
    }

    internal readonly record struct AviStreamDataType(
        string ReadableName,
        string Description,
        AviCompression Compression);

    /// <summary>
    /// AVI 流数据 FourCC（两位流编号 + 两字符类型码）的共享解码。
    /// 'movi' 中的数据块与各索引块的 dwChunkId 使用同一编码。
    /// </summary>
    internal static class AviStreamId
    {
        /// <summary>MS 官方定义的标准类型码；除此之外文本流可使用任意两字符码。</summary>
        public static readonly Dictionary<string, AviStreamDataType> TypeCodes = new(StringComparer.Ordinal)
        {
            ["db"] = new("UncompressedVideoFrame", "uncompressed video frame (DIB bits)", AviCompression.Uncompressed),
            ["dc"] = new("CompressedVideoFrame", "compressed video frame", AviCompression.Compressed),
            ["wb"] = new("AudioData", "audio data (waveform bytes)", AviCompression.NotApplicable),
            ["pc"] = new("PaletteChange", "palette change (AVIPALCHANGE)", AviCompression.NotApplicable),
        };

        /// <summary>
        /// 解析流编号。前两字符为 ASCII 十进制数字时按十进制解释；
        /// 少数非标准封装器写十六进制字符，此时按十六进制解释并置 nonStandard。
        /// </summary>
        public static bool TryGetStreamNumber(string fourCC, out int streamNumber, out bool nonStandard)
        {
            streamNumber = -1;
            nonStandard = false;
            if (fourCC.Length != 4) return false;

            if (char.IsAsciiDigit(fourCC[0]) && char.IsAsciiDigit(fourCC[1]))
            {
                streamNumber = (fourCC[0] - '0') * 10 + (fourCC[1] - '0');
                return true;
            }

            if (char.IsAsciiHexDigit(fourCC[0]) && char.IsAsciiHexDigit(fourCC[1]))
            {
                streamNumber = Convert.ToInt32(fourCC[..2], 16);
                nonStandard = true;
                return true;
            }

            return false;
        }

        /// <summary>返回 "stream 0 / compressed video frame" 形式的说明；无法解读时返回 null。</summary>
        public static string? Describe(string? fourCC)
        {
            if (fourCC is null || fourCC.Length != 4) return null;
            if (!TryGetStreamNumber(fourCC, out int number, out bool nonStandard)) return null;

            string typeCode = fourCC[2..4];
            string typeText = TypeCodes.TryGetValue(typeCode, out AviStreamDataType type)
                ? type.Description
                : $"stream-defined type code '{RIFFUtil.Sanitize(typeCode)}'";

            return nonStandard
                ? $"stream {number} (hexadecimal digits / non-standard) / {typeText}"
                : $"stream {number} / {typeText}";
        }
    }
}