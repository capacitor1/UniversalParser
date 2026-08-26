using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// LIST/INFO 下的 ZSTR 文本块，以及 AVI 常用的 IDIT / ISMP。
    /// 块的语义名放在 Title 中（例：Software 'ISFT'），dataLines 只给出解码后的文本。
    /// </summary>
    internal static class InfoTextChunk
    {
        private const int MaxDecodeBytes = 4096;

        /// <summary>4CC → 无空格英文可读名（同时作为 Dispatcher 的注册键）。</summary>
        public static readonly Dictionary<string, string> KnownTags = new(StringComparer.Ordinal)
        {
            ["IARL"] = "ArchivalLocation",
            ["IART"] = "Artist",
            ["ICMS"] = "Commissioned",
            ["ICMT"] = "Comment",
            ["ICNM"] = "Cinematographer",
            ["ICOP"] = "Copyright",
            ["ICRD"] = "CreationDate",
            ["ICRP"] = "Cropped",
            ["IDIM"] = "Dimensions",
            ["IDIT"] = "DigitizationTime",
            ["IDPI"] = "DotsPerInch",
            ["IENG"] = "Engineer",
            ["IGNR"] = "Genre",
            ["IKEY"] = "Keywords",
            ["ILGT"] = "Lightness",
            ["ILNG"] = "Language",
            ["IMED"] = "Medium",
            ["INAM"] = "Name",
            ["IMUS"] = "Music",
            ["IPLT"] = "PaletteEntryCount",
            ["IPRD"] = "Product",
            ["IPRO"] = "Producer",
            ["IPRT"] = "Part",
            ["ISBJ"] = "Subject",
            ["ISFT"] = "Software",
            ["ISHP"] = "Sharpness",
            ["ISMP"] = "SmpteTimeCode",
            ["ISRC"] = "Source",
            ["ISRF"] = "SourceForm",
            ["ITCH"] = "Technician",
            ["ITRK"] = "TrackNumber",
        };

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            int want = (int)Math.Min(MaxDecodeBytes, Math.Max(0, header.PayloadLength));
            byte[] payload = want > 0 ? new byte[want] : [];
            int read = want > 0 ? parser.ReadAt(header.PayloadStart, payload) : 0;

            string text = read > 0 ? RIFFUtil.DecodeText(payload.AsSpan(0, read)) : string.Empty;

            var dataLines = new List<(string K, string V)>
            {
                ("<Text>", text.Length > 0 ? text : "(empty)"),
            };

            if (read > 0 && payload.AsSpan(0, read).IndexOf((byte)0) < 0 && header.PayloadLength <= MaxDecodeBytes)
                dataLines.Add(("<Warning>", "INFO text should be NUL-terminated; terminator is missing."));

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The text chunk is truncated."));

            // 超长文本未被解码的剩余部分
            long unparsed = header.PayloadLength - read;
            if (unparsed > 0)
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(unparsed)));

            return new ParseResult
            {
                Title = RIFFUtil.MakeTitle(
                    KnownTags.TryGetValue(header.Id, out string? name) ? name : "Text",
                    header.Id),
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = parser.CreateRawStream(header.ChunkStart, (long)node.Length),
            };
        }
    }
}