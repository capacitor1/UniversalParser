namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Sbgp
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

            byte version = reader.ReadByte();
            byte f1 = reader.ReadByte();
            byte f2 = reader.ReadByte();
            byte f3 = reader.ReadByte();

            uint flags = (uint)((f1 << 16) | (f2 << 8) | f3);

            string groupingType = reader.ReadFourCC();
            uint grouping_type_parameter = 0;
            if(version == 1)
            {
                grouping_type_parameter = reader.ReadUInt32BE();
            }
            uint entryCount = reader.ReadUInt32BE();

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{flags:X6}"),
                ("grouping_type", groupingType),
                ("entry_count", entryCount.ToString())
            };
            if (version == 1)
            {
                dataLines.Add(("grouping_type_parameter", grouping_type_parameter.ToString()));
            }
            if (entryCount > 0)
            {
                uint sampleCount = reader.ReadUInt32BE();
                uint groupDescriptionIndex = reader.ReadUInt32BE();
                dataLines.Add(($"(sample_count,group_description_index)", $"{sampleCount},{groupDescriptionIndex}"));
                for (int i = 1; i < entryCount; i++)
                {
                    if (fs.Position + 8 > end)
                        break;

                    sampleCount = reader.ReadUInt32BE();
                    groupDescriptionIndex = reader.ReadUInt32BE();

                    dataLines.Add((string.Empty, $"{sampleCount},{groupDescriptionIndex}"));
                }
            }
            

            return new ParseResult
            {
                
                Title = $"SampleToGroupBox '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, start, (long)node.Length)
            };
        }
    }
}