//TODO:unchecked
namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class OFFs
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

            if (type != "oFFs")
                throw new InvalidDataException($"Expected oFFs but got '{type}'");

            if (length != 9)
                throw new InvalidDataException("oFFs must be 9 bytes");

            int x = reader.ReadInt32BE();
            int y = reader.ReadInt32BE();
            byte unit = reader.ReadByte();

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            string unitStr = unit switch
            {
                0 => "pixels",
                1 => "micrometers",
                _ => $"unknown({unit})"
            };

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("X", x.ToString()),
                ("Y", y.ToString()),
                ("Unit", unitStr)
            };

            return new ParseResult
            {
                Title = "ImageOffset 'oFFs'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}