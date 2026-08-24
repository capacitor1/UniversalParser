using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// AVI 'movi' 中的流数据块：FourCC = 两位流编号 + 两字符类型码，例如 '00dc' / '01wb'。
    /// 负载是编解码器私有的媒体数据，按设计不解析；只呈现 FourCC 本身编码的信息。
    /// </summary>
    internal static class AviStreamDataChunk
    {
        /// <summary>
        /// 回退匹配规则。两种情况视为流数据块：
        /// 1) 前两字符是十进制数字 —— 标准写法，类型码可以是任意两字符（文本流）；
        /// 2) 前两字符是十六进制数字且含字母，同时类型码是已知标准码 —— 少数封装器的非标准写法。
        /// 第 2 条附加“类型码已知”的限制，是为了避免把 'dmlh' 这类正常块误判成流数据。
        /// </summary>
        public static bool Matches(RIFFParser parser, RIFFChunkHeader header)
        {
            if (header.IsContainer) return false;
            if (parser.FormType is not ("AVI " or "AVIX")) return false;

            string id = header.Id;
            if (id.Length != 4) return false;

            if (char.IsAsciiDigit(id[0]) && char.IsAsciiDigit(id[1]))
                return true;

            return char.IsAsciiHexDigit(id[0])
                   && char.IsAsciiHexDigit(id[1])
                   && AviStreamId.TypeCodes.ContainsKey(id[2..4]);
        }

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            string id = header.Id;
            string typeCode = id[2..4];

            var leading = new List<(string K, string V)>();
            List<(string K, string V)>? trailing = null;

            AviStreamId.TryGetStreamNumber(id, out int streamNumber, out bool nonStandard);
            leading.Add(("<StreamNumber>", streamNumber.ToString()));

            if (nonStandard)
            {
                (trailing ??= []).Add(("<Warning>",
                    "Stream number is not two ASCII decimal digits; interpreted as hexadecimal (non-standard writer)."));
            }

            if (AviStreamId.TypeCodes.TryGetValue(typeCode, out AviStreamDataType type))
            {
                leading.Add(("<StreamDataType>", type.Description));

                switch (type.Compression)
                {
                    case AviCompression.Compressed:
                        leading.Add(("<Compression>",
                            "Compressed; the codec is defined by biCompression in the stream's 'strf' chunk"));
                        break;
                    case AviCompression.Uncompressed:
                        leading.Add(("<Compression>",
                            "Uncompressed; the payload is raw DIB bits laid out per the stream's 'strf' chunk"));
                        break;
                }

                if (typeCode == "pc")
                {
                    (trailing ??= []).Add(("<Note>",
                        "Payload is an AVIPALCHANGE structure; stream payloads are not decoded by design."));
                }

                return OpaqueChunk.Build(parser, node, header, type.ReadableName, leading, trailing);
            }

            // 未知两字符码：按规范这是合法的（文本流可自定义），不是错误
            leading.Add(("<StreamDataType>",
                $"Stream-defined type code '{RIFFUtil.Sanitize(typeCode)}'; "
                + "text streams may use arbitrary two-character codes"));

            return OpaqueChunk.Build(parser, node, header, "StreamData", leading, trailing);
        }
    }
}