namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class PHYs
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

            if (type != "pHYs")
                throw new InvalidDataException($"Expected pHYs but got '{type}'");

            if (length != 9)
                throw new InvalidDataException("pHYs chunk must be 9 bytes");

            uint pixelsPerUnitX = reader.ReadUInt32BE();
            uint pixelsPerUnitY = reader.ReadUInt32BE();
            byte unitSpecifier = reader.ReadByte();

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            string unit = unitSpecifier switch
            {
                0 => "unknown",
                1 => "meter",
                _ => $"invalid({unitSpecifier})"
            };

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("PixelsPerUnitX", pixelsPerUnitX.ToString()),
                ("PixelsPerUnitY", pixelsPerUnitY.ToString()),
                ("Unit", unit),
                ("DPI_X", (pixelsPerUnitX * 0.0254).ToString("F2")),
                ("DPI_Y", (pixelsPerUnitY * 0.0254).ToString("F2"))
            };

            return new ParseResult
            {
                Title = "PhysicalPixelDimensions 'pHYs'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}