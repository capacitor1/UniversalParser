namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class Default
    {
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

            // =========================
            // CRC VALIDATION (MANDATORY)
            // =========================
            var crcResult = PNGCRCValidator.Validate(
                fs,
                pos,
                length);

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("<PayloadLength>", length.ToString())
            };

            return new ParseResult
            {
                Title = $"Unknown '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(
                    fs,
                    (long)node.Position,
                    (long)node.Length
                )
            };
        }
    }
}