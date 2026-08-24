using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WAVE 的 'cue ' 块：dwCuePoints 后跟等长的 CuePoint 数组，每项 24 字节。
    /// </summary>
    internal static class WaveCueChunk
    {
        private const int HeaderSize = 4;
        private const int EntrySize = 24;
        private const long MaxEntries = 134_217_728 - 16;

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>(16);
            bool be = parser.IsBigEndian;
            long payloadLength = Math.Max(0, header.PayloadLength);

            Span<byte> head = stackalloc byte[HeaderSize];
            int headRead = payloadLength >= HeaderSize ? parser.ReadAt(header.PayloadStart, head) : 0;

            if (headRead < HeaderSize)
            {
                dataLines.Add(("<Error>", $"'cue ' requires a {HeaderSize}-byte header, {headRead} available."));
                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "CuePoints", dataLines);
            }

            uint declaredCount = RIFFUtil.ReadUInt32(head, be);
            dataLines.Add(("dwCuePoints", declaredCount.ToString()));

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'cue ' chunk is truncated."));

            long arrayBytes = payloadLength - HeaderSize;
            long entryCount = arrayBytes / EntrySize;
            long remainder = arrayBytes % EntrySize;

            if (remainder != 0)
            {
                dataLines.Add(("<Warning>",
                    $"The cue point array is not a multiple of {EntrySize} bytes; "
                    + $"the trailing {remainder} byte(s) do not form a complete entry."));
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(remainder)));
            }

            if (declaredCount != entryCount)
            {
                dataLines.Add(("<Note>",
                    $"dwCuePoints is {declaredCount} while {entryCount} complete entries are physically present; "
                    + "all present entries are listed."));
            }

            if (entryCount > MaxEntries)
            {
                dataLines.Add(("<Error>",
                    $"The array holds {entryCount:N0} entries, which exceeds the {MaxEntries:N0} rows that can be "
                    + "materialised in a single result. The array is left undecoded rather than truncated."));
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(arrayBytes)));
                return ChunkUtil.Build(parser, node, header, "CuePoints", dataLines);
            }

            if (entryCount == 0)
                dataLines.Add(("<Note>", "The chunk contains no cue points."));
            else
                AppendEntries(parser, header, dataLines, entryCount, be);

            return ChunkUtil.Build(parser, node, header, "CuePoints", dataLines);
        }

        private static void AppendEntries(
            RIFFParser parser,
            in RIFFChunkHeader header,
            List<(string K, string V)> dataLines,
            long entryCount,
            bool be)
        {
            dataLines.Add(($"CuePoints[{entryCount}]",
                "dwName,dwPosition,fccChunk,dwChunkStart,dwBlockStart,dwSampleOffset"));

            var builder = new StringBuilder(80);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(EntrySize * 2048);
            try
            {
                int blockSize = buffer.Length - buffer.Length % EntrySize;
                long position = header.PayloadStart + HeaderSize;
                long remaining = entryCount * EntrySize;

                while (remaining > 0)
                {
                    int want = (int)Math.Min(blockSize, remaining);
                    int read = parser.ReadAt(position, buffer.AsSpan(0, want));
                    read -= read % EntrySize;

                    if (read <= 0)
                    {
                        dataLines.Add(("<Warning>", "Unable to read the remaining cue points."));
                        break;
                    }

                    var block = new ReadOnlySpan<byte>(buffer, 0, read);
                    for (int offset = 0; offset + EntrySize <= read; offset += EntrySize)
                    {
                        ReadOnlySpan<byte> entry = block.Slice(offset, EntrySize);

                        builder.Clear();
                        builder.Append(RIFFUtil.ReadUInt32(entry[..4], be))
                               .Append(',').Append(RIFFUtil.ReadUInt32(entry.Slice(4, 4), be))
                               .Append(',').Append(ChunkUtil.CsvSafe(
                                   ChunkUtil.FormatFourCCField(entry.Slice(8, 4), be)))
                               .Append(',').Append(RIFFUtil.ReadUInt32(entry.Slice(12, 4), be))
                               .Append(',').Append(RIFFUtil.ReadUInt32(entry.Slice(16, 4), be))
                               .Append(',').Append(RIFFUtil.ReadUInt32(entry.Slice(20, 4), be));

                        dataLines.Add((string.Empty, builder.ToString()));
                    }

                    position += read;
                    remaining -= read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}