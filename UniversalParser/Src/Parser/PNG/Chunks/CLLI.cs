namespace UniversalParser.Src.Parser.PNG.Chunks
{
    /// <summary>
    /// cLLI - Content Light Level Information (MaxCLL / MaxFALL)
    /// PNG 3rd ed. 11.3.2.8 | type = 63 4C 4C 49 | payload 固定 8 字节
    /// </summary>
    internal static class CLLI
    {
        private const double LumScale = 0.0001; // raw * 0.0001 = cd/m2

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

            if (type != "cLLI")
                throw new InvalidDataException($"Expected cLLI but got '{type}'");

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult)
            };

            if (length != 8)
            {
                dataLines.Add(("<Error>", $"cLLI payload must be exactly 8 bytes, got {length}"));
            }
            else
            {
                reader.Seek(pos + 8);

                uint maxCll  = reader.ReadUInt32BE();
                uint maxFall = reader.ReadUInt32BE();

                dataLines.Add(("MaxCLL",  Format(maxCll)));
                dataLines.Add(("MaxFALL", Format(maxFall)));

                if (maxCll != 0 && maxFall != 0 && maxFall > maxCll)
                    dataLines.Add(("<Warning>", "MaxFALL should not exceed MaxCLL"));
                if (maxCll == 0 && maxFall == 0)
                    dataLines.Add(("<Warning>", "Both values are 0 - chunk carries no information"));
            }

            return new ParseResult
            {
                Title = "ColorSpace 'cLLI'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }

        // 0 按 CTA-861.3 惯例表示"未知 / 未计算"
        private static string Format(uint raw)
            => raw == 0
                ? "0 (unknown / not calculated)"
                : $"{raw * LumScale:0.####} cd/m2 (raw {raw})";
    }
}