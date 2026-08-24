using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// 'ICCP' 内嵌 ICC 色彩配置文件。
    /// 配置文件内容按设计不解析，只读取 ICC 头部偏移 36 处的 4 字节 Profile file signature
    /// 作为身份标识，因此整个载荷都计入未解析长度。
    /// </summary>
    internal static class IccProfileChunk
    {
        /// <summary>ICC 头部中 Profile file signature 字段的偏移与长度。</summary>
        private const int SignatureOffset = 36;

        private const int SignatureLength = 4;

        /// <summary>读取签名所需的最小载荷长度（注意不是 4，字段位于偏移 36 处）。</summary>
        private const int MinimumLength = SignatureOffset + SignatureLength;

        private const string ExpectedSignature = "acsp";

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            if (header.PayloadLength < MinimumLength)
            {
                dataLines.Add(("<Error>",
                    $"The profile signature is located at offset {SignatureOffset}, so at least "
                    + $"{MinimumLength} bytes are required; the payload has {header.PayloadLength:N0}."));
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(header.PayloadLength)));
                return WebPUtil.Build(parser, node, header, "IccProfile", dataLines);
            }

            Span<byte> buffer = stackalloc byte[SignatureLength];
            int read = parser.ReadAt(header.PayloadStart + SignatureOffset, buffer);

            if (read < SignatureLength)
            {
                dataLines.Add(("<Error>", "Unable to read the profile signature field."));
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(header.PayloadLength)));
                return WebPUtil.Build(parser, node, header, "IccProfile", dataLines);
            }

            string signature = RIFFUtil.DecodeFourCC(buffer);
            dataLines.Add(("<ProfileSignature>", RIFFUtil.Sanitize(signature)));

            if (!string.Equals(signature, ExpectedSignature, StringComparison.Ordinal))
            {
                dataLines.Add(("<Warning>",
                    $"The ICC specification requires the signature '{ExpectedSignature}'; "
                    + "this payload may not be an ICC profile."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'ICCP' chunk is truncated."));

            // 配置文件本体整体不解析
            dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(header.PayloadLength)));
            return WebPUtil.Build(parser, node, header, "IccProfile", dataLines);
        }
    }
}