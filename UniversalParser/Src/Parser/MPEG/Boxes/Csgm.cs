namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Csgm
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;

            fs.Position = (long)node.Position;

            long start = (long)node.Position;

            var reader = new MpegReader(fs);

            reader.ReadUInt32BE();
            reader.ReadFourCC(); // csgm

            var dataLines = new List<(string K, string V)>
            {
               ("", $"(Contains unsolved data,please see raw data below.)")//TODO:Solve all data
            };

            long remaining = (long)node.Length - 8;

            return Build(parser, node, start, dataLines);
        }

        private static ParseResult Build(MPEGParser parser, Node node, long start, List<(string K, string V)> dataLines)
        {
            return new ParseResult
            {
                Title = "(QuickTime)CompositionShiftGroupMeta 'csgm'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(parser.FileStream, start, (long)node.Length)
            };
        }
    }
}