namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Mehd
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            long start = (long)node.Position;
            long end = start + (long)node.Length;

            reader.ReadUInt32BE();
            reader.ReadFourCC(); // mehd

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

            long duration = 0;

            if (version == 1)
            {
                if (fs.Position + 8 <= end)
                    duration = (long)reader.ReadUInt64BE();
            }
            else
            {
                if (fs.Position + 4 <= end)
                    duration = reader.ReadUInt32BE();
            }

            dataLines.Add(("fragment_duration", duration.ToString()));

            return new ParseResult
            {
                
                Title = "MovieExtendsHeader 'mehd'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, start, (long)node.Length)
            };
        }
    }
}