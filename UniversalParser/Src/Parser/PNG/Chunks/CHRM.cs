namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class CHRM
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

            if (type != "cHRM")
                throw new InvalidDataException($"Expected cHRM but got '{type}'");

            if (length != 32)
                throw new InvalidDataException("cHRM must be 32 bytes");

            uint whiteX = reader.ReadUInt32BE();
            uint whiteY = reader.ReadUInt32BE();
            uint redX = reader.ReadUInt32BE();
            uint redY = reader.ReadUInt32BE();
            uint greenX = reader.ReadUInt32BE();
            uint greenY = reader.ReadUInt32BE();
            uint blueX = reader.ReadUInt32BE();
            uint blueY = reader.ReadUInt32BE();

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            var dataLines = new List<(string K, string V)>
            {("CRC32", crcResult),
                ("WhitePoint", $"{whiteX},{whiteY}"),
                ("Red", $"{redX},{redY}"),
                ("Green", $"{greenX},{greenY}"),
                ("Blue", $"{blueX},{blueY}")
                
            };

            return new ParseResult
            {
                Title = "Chromaticity 'cHRM'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}