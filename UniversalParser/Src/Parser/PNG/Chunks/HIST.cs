namespace UniversalParser.Src.Parser.PNG.Chunks
{
    /// <summary>
    /// hIST - Image Histogram
    /// PNG 3rd ed. 11.3.4.2 | type = 68 49 53 54
    /// payload = N * u16，N 必须等于 PLTE 条目数（≤ 256）
    /// </summary>
    internal static class HIST
    {
        private const int MaxEntries  = 256;
        private const int PreviewTop  = 8;

        public static ParseResult Parse(PNGParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var fs = parser.FileStream;
            var reader = new PngReader(fs);

            long pos = (long)node.Position;
            reader.Seek(pos);

            uint length = reader.ReadUInt32BE();
            string type = reader.ReadFourCC();

            if (type != "hIST")
                throw new InvalidDataException($"Expected hIST but got '{type}'");

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult)
            };

            if (length == 0)
            {
                dataLines.Add(("<Error>", "hIST is empty"));
            }
            else if (length % 2 != 0)
            {
                dataLines.Add(("<Error>", $"hIST length {length} is not divisible by 2"));
            }
            else if (length > MaxEntries * 2)
            {
                dataLines.Add(("<Error>",
                    $"{length / 2} entries exceeds the {MaxEntries}-entry palette maximum"));
            }
            else
            {
                reader.Seek(pos + 8);

                int count = (int)(length / 2);
                var freq = new ushort[count];
                for (int i = 0; i < count; i++)
                    freq[i] = reader.ReadUInt16BE();

                long total = 0;
                int zeros = 0;
                foreach (var f in freq)
                {
                    total += f;
                    if (f == 0) zeros++;
                }

                dataLines.Add(("EntryCount",  count.ToString()));
                dataLines.Add(("Sum",         total.ToString()));
                dataLines.Add(("ZeroEntries", $"{zeros} (unused palette entries)"));

                // 若 PNGParser 已缓存 PLTE 条目数，可在此交叉校验：
                // if (parser.PaletteEntryCount is int n && n != count)
                //     dataLines.Add(("<Error>", $"hIST has {count} entries but PLTE has {n}"));

                var top = freq
                    .Select((f, i) => (Index: i, Freq: f))
                    .Where(e => e.Freq > 0)
                    .OrderByDescending(e => e.Freq)
                    .Take(PreviewTop);

                int rank = 0;
                foreach (var e in top)
                {
                    double pct = total > 0 ? e.Freq * 100.0 / total : 0;
                    dataLines.Add(($"Top[{rank++}]", $"palette[{e.Index}] freq={e.Freq} ({pct:0.##}%)"));
                }
            }

            return new ParseResult
            {
                Title = "Histogram 'hIST'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}