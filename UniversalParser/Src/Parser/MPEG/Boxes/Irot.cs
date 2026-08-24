namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Irot
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            reader.ReadUInt32BE();
            string type = reader.ReadFourCC();

            byte version = reader.ReadByte();
            byte f1 = reader.ReadByte();
            byte f2 = reader.ReadByte();
            byte f3 = reader.ReadByte();

            uint flags = (uint)((f1 << 16) | (f2 << 8) | f3);

            byte value = reader.ReadByte();

            byte rotation = (byte)(value & 0x03);
            byte reserved = (byte)(value >> 2);

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{flags:X6}"),
                ("rotation", rotation.ToString()),
                ("reserved", $"0x{reserved:X2}")
            };

            return new ParseResult
            {
                
                Title = $"ImageRotation '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}