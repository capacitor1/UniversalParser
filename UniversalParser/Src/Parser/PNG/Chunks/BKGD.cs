namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class BKGD
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

            if (type != "bKGD")
                throw new InvalidDataException($"Expected bKGD but got '{type}'");

            byte[] data = reader.ReadBytes((int)length);

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            string desc;

            if (length == 1)
            {
                desc = $"PaletteIndex={data[0]}";
            }
            else if (length == 2)
            {
                ushort gray = (ushort)((data[0] << 8) | data[1]);
                desc = $"Gray={gray}";
            }
            else if (length == 6)
            {
                ushort r = (ushort)((data[0] << 8) | data[1]);
                ushort g = (ushort)((data[2] << 8) | data[3]);
                ushort b = (ushort)((data[4] << 8) | data[5]);
                desc = $"RGB=({r},{g},{b})";
            }
            else
            {
                desc = "Invalid bKGD length";
            }

            var dataLines = new List<(string K, string V)>
            {("CRC32", crcResult),
                ("Background", desc),
                ("Length", length.ToString())
                
            };

            return new ParseResult
            {
                Title = "BackgroundColor 'bKGD'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}