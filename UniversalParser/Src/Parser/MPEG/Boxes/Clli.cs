namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Clli
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            long start = (long)node.Position;
            long end = start + (long)node.Length;

            reader.ReadUInt32BE();
            string type = reader.ReadFourCC();

            ushort maxCLL = reader.ReadUInt16BE();
            ushort maxPALL = reader.ReadUInt16BE();

            var dataLines = new List<(string K, string V)>
            {
                ("max_content_light_level", maxCLL.ToString()),
                ("max_pic_average_light_level", maxPALL.ToString())
            };

            return new ParseResult
            {
                Title = $"ContentLightLevelInfo '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, start, (long)node.Length)
            };
        }
    }
}