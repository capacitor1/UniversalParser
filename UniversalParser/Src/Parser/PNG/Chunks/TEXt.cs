namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class TEXt
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

            if (type != "tEXt")
                throw new InvalidDataException($"Expected tEXt but got '{type}'");

            byte[] data = reader.ReadBytes((int)length);

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            int sepIndex = Array.IndexOf(data, (byte)0x00);
            if (sepIndex <= 0 || sepIndex >= data.Length - 1)
                throw new InvalidDataException("Invalid tEXt format (missing keyword separator)");

            string keyword = System.Text.Encoding.ASCII.GetString(data, 0, sepIndex);
            string text = System.Text.Encoding.Latin1.GetString(data, sepIndex + 1, data.Length - sepIndex - 1);

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("Keyword", keyword),
                ("Text", text)
            };

            return new ParseResult
            {
                Title = "UncompressedText 'tEXt'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}