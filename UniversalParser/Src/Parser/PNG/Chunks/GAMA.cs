namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class GAMA
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

            if (type != "gAMA")
                throw new InvalidDataException($"Expected gAMA but got '{type}'");

            if (length != 4)
                throw new InvalidDataException("gAMA chunk must be 4 bytes");

            uint rawGamma = reader.ReadUInt32BE();
            double gamma = rawGamma / 100000.0;

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            var dataLines = new List<(string K, string V)>
            {("CRC32", crcResult),
                ("Gamma", rawGamma.ToString()),
                ("<Gamma>", gamma.ToString("F5"))
                
            };

            return new ParseResult
            {
                Title = "GammaCorrection 'gAMA'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}