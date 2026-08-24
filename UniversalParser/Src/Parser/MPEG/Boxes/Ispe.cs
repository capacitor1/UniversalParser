namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Ispe
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

            uint imageWidth = reader.ReadUInt32BE();
            uint imageHeight = reader.ReadUInt32BE();

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{flags:X6}"),
                ("image_width", imageWidth.ToString()),
                ("image_height", imageHeight.ToString())
            };

            return new ParseResult
            {
                
                Title = $"ImageSpatialExtents '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}