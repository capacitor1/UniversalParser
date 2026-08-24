namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class SRGB
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

            if (type != "sRGB")
                throw new InvalidDataException($"Expected sRGB but got '{type}'");

            if (length != 1)
                throw new InvalidDataException("sRGB chunk must be exactly 1 byte");

            byte intent = reader.ReadByte();

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            string intentName = intent switch
            {
                0 => "Perceptual",
                1 => "Relative Colorimetric",
                2 => "Saturation",
                3 => "Absolute Colorimetric",
                _ => $"Invalid ({intent})"
            };

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("RenderingIntent", $"{intent} ({intentName})"),
                ("ColorSpace", "sRGB IEC 61966-2.1")
            };

            return new ParseResult
            {
                Title = "StandardRGBColorSpace 'sRGB'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}