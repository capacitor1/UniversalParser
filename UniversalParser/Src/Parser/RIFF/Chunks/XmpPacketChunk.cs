using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// Adobe XMP 数据包。
    /// '_PMX' 用于 AVI 与 WAV：Adobe 以 DWORD 常量 'XMP_' 写入 chunk ID，按 RIFF 的小端约定落盘后
    /// 字节顺序反转，因此磁盘上呈现为 '_PMX'。WebP 使用未反转的 'XMP '。
    /// 载荷为 XMP packet（RDF/XML 文本），其 RDF 内容按设计不解析，仅呈现 xpacket 包装与原文。
    /// </summary>
    internal static class XmpPacketChunk
    {
        /// <summary>4CC → 无空格英文可读名（同时作为 Dispatcher 的注册键）。</summary>
        public static readonly Dictionary<string, string> KnownIds = new(StringComparer.Ordinal)
        {
            ["_PMX"] = "XmpPacket",     // AVI / WAV
            ["XMP "] = "XmpPacket",     // WebP
            ["aXML"] = "AxmlPacket",    // BWF：ADM/axml，同为 XML 文本
            ["iXML"] = "IxmlPacket",    // iXML 录音元数据，同为 XML 文本
        };

        private const string PacketHeadMarker = "<?xpacket begin=";
        private const string PacketTailMarker = "<?xpacket end=";

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            string readableName = KnownIds.TryGetValue(header.Id, out string? name) ? name : "XmlPacket";
            var dataLines = new List<(string K, string V)>();

            byte[] payload = TextPayload.Read(
                parser, header, TextPayload.DefaultLimit, out int read, out long unparsedBytes);

            if (header.Id == "_PMX")
            {
                dataLines.Add(("<ChunkIdOrigin>",
                    "Adobe writes the DWORD constant 'XMP_'; RIFF little-endian storage reverses it to '_PMX'."));
            }

            if (read == 0)
            {
                dataLines.Add(("<Note>", "The chunk carries no data."));
                AviUtil.AddUnparsedLength(dataLines, header, 0);
                return AviUtil.Build(parser, node, header, readableName, dataLines);
            }

            var span = new ReadOnlySpan<byte>(payload, 0, read);
            TextPayload.TextEncoding encoding = TextPayload.DetectEncoding(span);
            dataLines.Add(("<Encoding>", TextPayload.DescribeEncoding(encoding)));

            string? text = TextPayload.Decode(span, encoding);
            if (text is null)
            {
                dataLines.Add(("<Warning>",
                    "The payload is not decodable as text; an XMP packet is expected to be XML."));
                AviUtil.AddUnparsedLength(dataLines, header, 0);
                return AviUtil.Build(parser, node, header, readableName, dataLines);
            }

            AppendPacketWrapper(dataLines, text);

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", $"The '{RIFFUtil.Sanitize(header.Id)}' chunk is truncated."));

            if (unparsedBytes > 0)
            {
                dataLines.Add(("<Warning>",
                    $"Only the first {read:N0} bytes were decoded; the payload exceeds the "
                    + $"{TextPayload.DefaultLimit:N0} byte limit for text decoding."));
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(unparsedBytes)));
            }

            TextPayload.AppendText(dataLines, "<Text>", text);
            return AviUtil.Build(parser, node, header, readableName, dataLines);
        }

        /// <summary>解析 xpacket 处理指令包装。这些是包装层的字段，不涉及 RDF 内容。</summary>
        private static void AppendPacketWrapper(List<(string K, string V)> dataLines, string text)
        {
            int headStart = text.IndexOf(PacketHeadMarker, StringComparison.Ordinal);
            if (headStart < 0)
            {
                dataLines.Add(("<Note>",
                    "No '<?xpacket begin=?>' processing instruction found; the payload is bare XML "
                    + "rather than a wrapped XMP packet."));
                return;
            }

            int headEnd = text.IndexOf("?>", headStart, StringComparison.Ordinal);
            if (headEnd < 0)
            {
                dataLines.Add(("<Warning>", "The '<?xpacket begin=?>' processing instruction is unterminated."));
                return;
            }

            string head = text[headStart..(headEnd + 2)];

            string? id = ExtractAttribute(head, "id=");
            if (id is not null)
            {
                dataLines.Add(("<PacketId>", id == "W5M0MpCehiHzreSzNTczkc9d"
                    ? $"{id} (the constant GUID mandated by the XMP specification)"
                    : $"{id} (non-standard; the XMP specification mandates W5M0MpCehiHzreSzNTczkc9d)"));
            }

            int tailStart = text.LastIndexOf(PacketTailMarker, StringComparison.Ordinal);
            if (tailStart < 0)
            {
                dataLines.Add(("<Warning>", "No '<?xpacket end=?>' processing instruction found."));
                return;
            }

            int tailEnd = text.IndexOf("?>", tailStart, StringComparison.Ordinal);
            string tail = tailEnd >= 0 ? text[tailStart..(tailEnd + 2)] : text[tailStart..];

            string? end = ExtractAttribute(tail, "end=");
            if (end is not null)
            {
                dataLines.Add(("<PacketEnd>", end switch
                {
                    "w" => "w: writable, trailing white-space padding is present for in-place updates",
                    "r" => "r: read-only, no padding reserved",
                    _ => $"{end}: unrecognised value",
                }));
            }

            // 可写包尾部保留的空白填充量，是判断该包是否被原地改写过的直接线索
            int paddingEnd = tailStart;
            int paddingStart = paddingEnd;
            while (paddingStart > 0 && char.IsWhiteSpace(text[paddingStart - 1])) paddingStart--;

            int padding = paddingEnd - paddingStart;
            if (padding > 0)
                dataLines.Add(("<PacketPadding>", $"{padding:N0}"));
        }

        /// <summary>从处理指令中取出形如 name="value" 或 name='value' 的属性值。</summary>
        private static string? ExtractAttribute(string instruction, string attributeName)
        {
            int start = instruction.IndexOf(attributeName, StringComparison.Ordinal);
            if (start < 0) return null;

            start += attributeName.Length;
            if (start >= instruction.Length) return null;

            char quote = instruction[start];
            if (quote is not ('"' or '\'')) return null;

            int end = instruction.IndexOf(quote, start + 1);
            return end < 0 ? null : instruction[(start + 1)..end];
        }
    }
}