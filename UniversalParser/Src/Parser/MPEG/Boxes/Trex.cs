namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Trex
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            long start = (long)node.Position;

            reader.ReadUInt32BE();
            reader.ReadFourCC(); // trex

            byte version = reader.ReadByte();
            byte f1 = reader.ReadByte();
            byte f2 = reader.ReadByte();
            byte f3 = reader.ReadByte();

            uint flags = (uint)((f1 << 16) | (f2 << 8) | f3);

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{flags:X6}")
            };
            uint trackId = reader.ReadUInt32BE();
            uint defaultSampleDescriptionIndex = reader.ReadUInt32BE();
            uint defaultSampleDuration = reader.ReadUInt32BE();
            uint defaultSampleSize = reader.ReadUInt32BE();
            uint defaultSampleFlags = reader.ReadUInt32BE();

            dataLines.Add(("track_ID", trackId.ToString()));
            dataLines.Add(("default_sample_description_index", defaultSampleDescriptionIndex.ToString()));
            dataLines.Add(("default_sample_duration", defaultSampleDuration.ToString()));
            dataLines.Add(("default_sample_size", defaultSampleSize.ToString()));
            dataLines.Add(("default_sample_flags", $"0x{defaultSampleFlags:X8}"));

            return new ParseResult
            {
                
                Title = "TrackExtends 'trex'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, start, (long)node.Length)
            };
        }
    }
}