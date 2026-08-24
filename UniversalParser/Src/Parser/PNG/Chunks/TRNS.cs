namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class TRNS
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

            if (type != "tRNS")
                throw new InvalidDataException($"Expected tRNS but got '{type}'");

            byte[] data = reader.ReadBytes((int)length);

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            string desc;

            if (length == 1)
            {
                desc = $"GrayAlphaThreshold={data[0]}";
            }
            else if (length == 2)
            {
                ushort gray = (ushort)((data[0] << 8) | data[1]);
                desc = $"GrayTransparent={gray}";
            }
            else
            {
                desc = $"PaletteAlphaCount={length}";
            }

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("Transparency", desc),
                ("Length", length.ToString())
            };

            return new ParseResult
            {
                Title = "Transparency 'tRNS'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}