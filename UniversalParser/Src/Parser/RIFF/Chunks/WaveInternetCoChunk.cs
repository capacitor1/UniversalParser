using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WAVE 的 'iwep' / 'iwem' 块：INTERNET Co., Ltd.（Sound it! 系列）的私有块，无公开规范。
    /// 观察样本可知负载自起点即为单字节 NUL 结尾的字符串序列，故按 ZSTR 数组如实列出；
    /// 由于没有任何已定义字段名，条目一律不命名。
    /// </summary>
    internal static class WaveInternetCoChunk
    {
        private const int MaxScanBytes = 64 * 1024;
        private const int MaxStringsListed = 128;
        private const int MaxTextLength = 256;

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>(40);

            //dataLines.Add(("<Vendor>", "INTERNET Co., Ltd. (Sound it! family)"));
            //dataLines.Add(("<Layout>", "NullTerminatedString[]"));

            dataLines.Add(("<Note>",
                "Vendor-private chunk with no published specification."));
            dataLines.Add(("<Note>",
                "The layout is reverse-engineered from observed samples alone: a bare run of single-byte "
                + "NUL-terminated strings beginning at the payload origin, with no leading header, count "
                + "field or record framing observed. Treat this as a hypothesis, not a specification."));
            //dataLines.Add(("<Note>",
            //    "Single-byte terminators rule out UTF-16, which would show paired zero bytes."));
            //dataLines.Add(("<Note>",
            //    "Entries are listed verbatim and left unnamed, no field name being defined for this chunk; "
            //    + "each is prefixed with its offset from the payload start."));
            //dataLines.Add(("<Note>",
            //    "'iwep' and 'iwem' share this layout; what distinguishes their contents is unknown."));

            int read = ChunkUtil.ReadPayload(parser, header, MaxScanBytes, out byte[] payload);

            if (read <= 0)
            {
                dataLines.Add(("<Note>", "The payload is empty; no string is present."));
                if (header.IsTruncated)
                    dataLines.Add(("<Warning>", $"The '{header.Id}' chunk is truncated."));
                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "WaveInternetCo", dataLines);
            }

            ReadOnlySpan<byte> span = payload.AsSpan(0, read);

            if (read < header.PayloadLength)
            {
                dataLines.Add(("<Note>",
                    $"Scanning is capped at {RIFFUtil.FormatBytes(MaxScanBytes)}; "
                    + "bytes beyond that were not examined."));
            }

            if (ChunkUtil.IsAllZero(span))
            {
                dataLines.Add(("<Note>",
                    "The scanned bytes are entirely zero; the chunk carries only empty strings or padding."));
                if (header.IsTruncated)
                    dataLines.Add(("<Warning>", $"The '{header.Id}' chunk is truncated."));
                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "WaveInternetCo", dataLines);
            }

            var items = ChunkUtil.SplitNulTerminated(span, MaxStringsListed, out int consumed);

            dataLines.Add(("<StringCount>", items.Count.ToString()));

            int emptyCount = 0;
            int assignmentCount = 0;

            for (int i = 0; i < items.Count; i++)
            {
                (int offset, int length) = items[i];

                if (length == 0)
                {
                    emptyCount++;
                    dataLines.Add(($"<String[{i}]>", "<empty>"));
                    continue;
                }

                int shown = Math.Min(length, MaxTextLength);
                string text = ChunkUtil.DecodeUnknownCodePage(span.Slice(offset, shown));
                string safe = ChunkUtil.CsvSafe(RIFFUtil.Sanitize(text));
                if (shown < length) safe += "...";

                dataLines.Add(($"<String[{i}]>", safe));

                if (text.IndexOf('=') > 0) assignmentCount++;
            }

            if (assignmentCount > 0)
            {
                dataLines.Add(("<Note>",
                    $"{assignmentCount} string(s) take the form Name=Value; whether the remaining strings "
                    + "are keys, values or free text is unconfirmed, so no pairing is applied."));
            }

            if (emptyCount > 0)
            {
                bool trailing = items[^1].Length == 0;
                dataLines.Add(("<Note>", trailing
                    ? $"{emptyCount} empty string(s) present, the last entry among them; "
                      + "trailing empties are more likely padding than data."
                    : $"{emptyCount} empty string(s) present; these may be deliberately blank values."));
            }

            int nonAscii = ChunkUtil.CountNonAscii(span);
            if (nonAscii > 0)
            {
                dataLines.Add(("<NonAsciiBytes>", nonAscii.ToString()));
                dataLines.Add(("<Note>",
                    "High bytes are rendered one-to-one as Latin-1 to stay reversible. No code page is "
                    + "declared for this chunk and the writer is a Japanese product, so they may in fact "
                    + "be legacy multi-byte text such as Shift-JIS."));
            }

            if (consumed < read)
            {
                if (items.Count >= MaxStringsListed)
                {
                    dataLines.Add(("<Note>",
                        $"The listing is capped at {MaxStringsListed} strings; the rest are left unparsed."));
                }
                else
                {
                    dataLines.Add(("<Warning>",
                        "The payload does not end on a terminator; the final string is unterminated."));

                    int tailShown = Math.Min(read - consumed, MaxTextLength);
                    string tail = ChunkUtil.DecodeUnknownCodePage(span.Slice(consumed, tailShown));
                    dataLines.Add(("<UnterminatedTail>",
                        $"+0x{consumed:X4} {ChunkUtil.CsvSafe(RIFFUtil.Sanitize(tail))}"
                        + (tailShown < read - consumed ? "..." : string.Empty)));
                }
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", $"The '{header.Id}' chunk is truncated."));

            // 全部字符串均正常终止且已扫完时 unparsed 为 0，helper 自会跳过。
            ChunkUtil.AddUnparsedLength(dataLines, header, consumed);
            return ChunkUtil.Build(parser, node, header, "WaveInternetCo", dataLines);
        }
    }
}