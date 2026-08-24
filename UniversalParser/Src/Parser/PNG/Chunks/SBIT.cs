//TODO:unchecked
namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class SBIT
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

            if (type != "sBIT")
                throw new InvalidDataException($"Expected sBIT but got '{type}'");

            byte[] data = reader.ReadBytes((int)length);

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            string desc = length switch
            {
                1 => $"Gray={data[0]} bits",
                2 => $"Gray={data[0]}, Alpha={data[1]} bits",
                3 => $"RGB=({data[0]},{data[1]},{data[2]}) bits",
                4 => $"RGBA=({data[0]},{data[1]},{data[2]},{data[3]}) bits",
                _ => "Invalid sBIT"
            };

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("SignificantBits", desc),
                ("Length", length.ToString())
            };

            return new ParseResult
            {
                Title = "SignificantBits 'sBIT'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}