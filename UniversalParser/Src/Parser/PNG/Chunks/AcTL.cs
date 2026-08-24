namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class AcTL
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

            if (type != "acTL")
                throw new InvalidDataException($"Expected acTL but got '{type}'");

            if (length != 8)
                throw new InvalidDataException("acTL must be 8 bytes");

            uint numFrames = reader.ReadUInt32BE();
            uint numPlays = reader.ReadUInt32BE();

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("FrameCount", numFrames.ToString()),
                ("PlayCount", numPlays == 0 ? "Infinite" : numPlays.ToString())
            };

            return new ParseResult
            {
                Title = "AnimationControl 'acTL'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}