namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Ctts
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            long start = (long)node.Position;
            long end = start + (long)node.Length;

            reader.ReadUInt32BE();
            reader.ReadFourCC();

            byte version = reader.ReadByte();
            byte f1 = reader.ReadByte();
            byte f2 = reader.ReadByte();
            byte f3 = reader.ReadByte();

            uint flags = (uint)((f1 << 16) | (f2 << 8) | f3);

            uint entryCount = reader.ReadUInt32BE();

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{flags:X6}"),
                ("entry_count", entryCount.ToString())
            };

            if(entryCount > 0)
            {
                uint sampleCount = reader.ReadUInt32BE();
                int sampleOffset = (version == 0)
                    ? (int)reader.ReadUInt32BE()
                    : reader.ReadInt32BE();
                dataLines.Add(($"(sample_count,offset)[{entryCount}]", $"{sampleCount},{sampleOffset}"));
                for (int i = 1; i < entryCount; i++)
                {
                    if (fs.Position + 8 > end)
                        break;

                    sampleCount = reader.ReadUInt32BE();
                    sampleOffset = (version == 0)
                        ? (int)reader.ReadUInt32BE()
                        : reader.ReadInt32BE();

                    dataLines.Add((string.Empty, $"{sampleCount},{sampleOffset}"));
                }
            }

            return new ParseResult
            {
                
                Title = "CompositionTimeToSample 'ctts'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, start, (long)node.Length)
            };
        }
    }
}