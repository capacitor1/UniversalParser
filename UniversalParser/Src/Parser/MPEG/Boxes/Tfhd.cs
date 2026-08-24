namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Tfhd
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            reader.ReadUInt32BE();
            string t = reader.ReadFourCC();

            byte version = reader.ReadByte();
            byte f1 = reader.ReadByte();
            byte f2 = reader.ReadByte();
            byte f3 = reader.ReadByte();

            uint flags = (uint)((f1 << 16) | (f2 << 8) | f3);

            uint trackId = reader.ReadUInt32BE();

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{flags:X6}"),
                ("track_ID", trackId.ToString())
            };

            // ISO BMFF optional fields based on flags
            if ((flags & 0x000001) != 0)
                dataLines.Add(("base_data_offset", reader.ReadUInt64BE().ToString()));

            if ((flags & 0x000002) != 0)
                dataLines.Add(("sample_description_index", reader.ReadUInt32BE().ToString()));

            if ((flags & 0x000008) != 0)
                dataLines.Add(("default_sample_duration", reader.ReadUInt32BE().ToString()));

            if ((flags & 0x000010) != 0)
                dataLines.Add(("default_sample_size", reader.ReadUInt32BE().ToString()));

            if ((flags & 0x000020) != 0)
                dataLines.Add(("default_sample_flags", $"0x{reader.ReadUInt32BE():X8}"));

            return new ParseResult
            {
                
                Title = $"TrackFragmentHeader '{t}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}