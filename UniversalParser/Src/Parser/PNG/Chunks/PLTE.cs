namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class PLTE
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

            if (type != "PLTE")
                throw new InvalidDataException($"Expected PLTE but got '{type}'");

            if (length == 0 || length % 3 != 0)
                throw new InvalidDataException("PLTE length must be a multiple of 3 and > 0");

            int colorCount = (int)length / 3;

            byte[] data = reader.ReadBytes((int)length);

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult), 
                ($"Color[{colorCount}]", colorCount.ToString())
            };

            for (int i = 0; i < colorCount; i++)
            {
                int idx = i * 3;
                byte r = data[idx];
                byte g = data[idx + 1];
                byte b = data[idx + 2];

                dataLines.Add((string.Empty, $"RGB({r},{g},{b})"));
            }

            return new ParseResult
            {
                Title = "Palette 'PLTE'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}