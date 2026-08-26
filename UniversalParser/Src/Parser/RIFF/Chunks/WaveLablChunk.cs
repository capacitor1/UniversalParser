using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// LIST 'adtl' 下的 'labl' 块：某个 cue point 的标签文本。
    /// 结构为 dwName(DWORD) + ZSTR；'note' 块字段完全相同，仅语义不同（注释而非标签）。
    /// </summary>
    internal static class WaveLablChunk
    {
        private const int NameSize = 4;
        private const int MaxTextBytes = 64 * 1024;

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>(12);
            int read = ChunkUtil.ReadPayload(parser, header, NameSize + MaxTextBytes, out byte[] payload);

            if (read < NameSize)
            {
                dataLines.Add(("<Error>",
                    $"'labl' requires at least {NameSize} bytes for dwName, {read} available."));
                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "AdtlLabel", dataLines);
            }

            uint name = RIFFUtil.ReadUInt32(payload.AsSpan(0, NameSize), parser.IsBigEndian);
            dataLines.Add(("dwName", name.ToString()));
            dataLines.Add(("<Note>",
                "dwName is the cue point identifier; it must match an entry in the 'cue ' table. "
                + "Some documents call this field dwIdentifier."));

            ReadOnlySpan<byte> tail = payload.AsSpan(NameSize, read - NameSize);
            int nul = tail.IndexOf((byte)0);
            bool terminated = nul >= 0;
            ReadOnlySpan<byte> textBytes = terminated ? tail[..nul] : tail;
            long parsed = terminated ? NameSize + nul + 1 : NameSize + tail.Length;

            if (textBytes.IsEmpty)
            {
                dataLines.Add(("data", string.Empty));
                dataLines.Add(("<Note>", "The label text is empty."));
            }
            else
            {
                // Latin-1 逐字节映射，保证呈现与文件字节一一对应、不丢高位字节。
                string raw = ChunkUtil.DecodeUnknownCodePage(textBytes);
                dataLines.Add(("data", ChunkUtil.CsvSafe(RIFFUtil.Sanitize(raw))));

                int nonAscii = ChunkUtil.CountNonAscii(textBytes);
                if (nonAscii > 0)
                {
                    dataLines.Add(("<NonAsciiBytes>", nonAscii.ToString()));

                    if (ChunkUtil.LooksLikeUtf8(textBytes))
                    {
                        string utf8 = Encoding.UTF8.GetString(textBytes);
                        dataLines.Add(("<data>", ChunkUtil.CsvSafe(RIFFUtil.Sanitize(utf8))));
                        //dataLines.Add(("<Note>",
                        //    "The text is valid UTF-8 and is also shown decoded as such. 'labl' declares no "
                        //    + "code page of its own, so the interpretation rests on that test alone."));
                    }
                    else
                    {
                        //dataLines.Add(("<Note>",
                        //    "The text is not valid UTF-8 and 'labl' declares no code page; the bytes are "
                        //    + "shown one-to-one as Latin-1. A 'CSET' chunk, if present, governs the charset."));
                    }
                }
            }

            if (!terminated)
            {
                dataLines.Add(("<Warning>",
                    "The label text is not NUL-terminated; the spec defines this field as a ZSTR."));
            }
            else if (ChunkUtil.HasDataAfterTerminator(tail))
            {
                dataLines.Add(("<Warning>",
                    "Non-zero bytes follow the terminator; these are residual data from earlier content."));
            }
            else if (parsed < header.PayloadLength)
            {
                dataLines.Add(("<Note>", "Zero bytes follow the terminator; the tail is padding."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'labl' chunk is truncated."));

            ChunkUtil.AddUnparsedLength(dataLines, header, parsed);
            return ChunkUtil.Build(parser, node, header, "AdtlLabel", dataLines);
        }
    }
}