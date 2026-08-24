using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// LIST 'Tdat' 下的子块。这些块没有任何公开规范：ExifTool 将 Tdat 归为 Adobe CS3 Bridge 写入，
    /// 其标签表为空，仅记录曾观察到 tc_O / tc_A / rn_O / rn_A 四个子块。
    /// 下列可读名由 FourCC 推断得出，并非文档定义，故一律附带 &lt;Interpretation&gt; 说明。
    /// </summary>
    internal static class TdatChunk
    {
        public readonly record struct TdatTag(string ReadableName, string InferredMeaning);

        /// <summary>4CC → 推断的含义（同时作为 Dispatcher 的注册键）。</summary>
        public static readonly Dictionary<string, TdatTag> KnownIds = new(StringComparer.Ordinal)
        {
            ["tc_O"] = new("TimecodeOriginal",
                "timecode, original; corresponds to the XMP Dynamic Media property xmpDM:startTimecode"),
            ["tc_A"] = new("TimecodeAlternate",
                "timecode, alternate; corresponds to the XMP Dynamic Media property xmpDM:altTimecode"),
            ["rn_O"] = new("ReelNameOriginal",
                "reel name, original; corresponds to the XMP Dynamic Media property xmpDM:tapeName"),
            ["rn_A"] = new("ReelNameAlternate",
                "reel name, alternate; corresponds to the XMP Dynamic Media property xmpDM:altTapeName"),
        };

        public static IEnumerable<string> RegistrationKeys => KnownIds.Keys;

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            bool known = KnownIds.TryGetValue(header.Id, out TdatTag tag);
            string readableName = known ? tag.ReadableName : "TdatEntry";

            var dataLines = new List<(string K, string V)>
            {
                ("<Specification>",
                    "Undocumented. The 'Tdat' list is written by Adobe applications; no public specification "
                    + "defines its sub-chunks and ExifTool declares no tags for them."),
            };

            if (known)
            {
                dataLines.Add(("<Interpretation>",
                    $"Inferred from the FourCC: {tag.InferredMeaning}. Not verified against any specification."));
            }

            byte[] payload = TextPayload.Read(parser, header, 64 * 1024, out int read, out long unparsedBytes);

            if (read == 0)
            {
                dataLines.Add(("<Note>", "The chunk carries no data."));
                AviUtil.AddUnparsedLength(dataLines, header, 0);
                return AviUtil.Build(parser, node, header, readableName, dataLines);
            }

            var span = new ReadOnlySpan<byte>(payload, 0, read);

            // 观察到的实例均为 ASCII 文本，但由于无规范约束，二进制载荷不作任何推测
            ReadOnlySpan<byte> body = span;
            int nul = body.IndexOf((byte)0);
            bool nulTerminated = nul >= 0;
            if (nulTerminated) body = body[..nul];

            TextPayload.TextEncoding encoding = TextPayload.DetectEncoding(body);
            string? text = TextPayload.Decode(body, encoding);

            if (text is null)
            {
                dataLines.Add(("<Warning>",
                    "The payload is not decodable as text and its binary layout is unspecified; "
                    + "it is left undecoded."));
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(header.PayloadLength)));
                return AviUtil.Build(parser, node, header, readableName, dataLines);
            }

            dataLines.Add(("<Encoding>", TextPayload.DescribeEncoding(encoding)));
            TextPayload.AppendText(dataLines, "<Text>", text);

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", $"The '{RIFFUtil.Sanitize(header.Id)}' chunk is truncated."));

            // NUL 之后若还有非填充字节，属于未解析部分
            long trailing = unparsedBytes;
            if (nulTerminated)
            {
                ReadOnlySpan<byte> rest = span[(nul + 1)..];
                bool allZero = true;
                foreach (byte b in rest)
                {
                    if (b == 0) continue;
                    allZero = false;
                    break;
                }

                if (!allZero) trailing += rest.Length;
            }

            if (trailing > 0)
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(trailing)));

            return AviUtil.Build(parser, node, header, readableName, dataLines);
        }
    }
}