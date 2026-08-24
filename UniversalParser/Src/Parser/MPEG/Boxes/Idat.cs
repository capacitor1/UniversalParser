namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Idat
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            reader.ReadUInt32BE();
            string f = reader.ReadFourCC();

            long payloadOffset = fs.Position;
            long payloadLength = (long)node.Length - (payloadOffset - (long)node.Position);

            var dataLines = new List<(string K, string V)>
            {
                ("<payload_length>", payloadLength.ToString())
            };

            return new ParseResult
            {
                
                Title = $"ItemDataBox '{f}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}