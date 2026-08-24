namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Sdtp
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            reader.ReadUInt32BE();
            reader.ReadFourCC();

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

            long sampleCount = (long)node.Length - (fs.Position - (long)node.Position);

            if(sampleCount > 0)
            {
                byte b = reader.ReadByte();

                var isLeading = ((b >> 6) & 0x03);
                var dependsOn = ((b >> 4) & 0x03);
                var isDependedOn = ((b >> 2) & 0x03);
                var hasRedundancy = (b & 0x03); 
                string csv =
                        $"{isLeading},{dependsOn},{isDependedOn},{hasRedundancy}";
                dataLines.Add(($"(is_leading,depends_on,is_depended_on,redundancy)[{sampleCount}]", csv));
                for (int i = 1; i < sampleCount; i++)
                {
                    b = reader.ReadByte();

                    isLeading = ((b >> 6) & 0x03);
                    dependsOn = ((b >> 4) & 0x03);
                    isDependedOn = ((b >> 2) & 0x03);
                    hasRedundancy = (b & 0x03);

                    csv =
                        $"{isLeading},{dependsOn},{isDependedOn},{hasRedundancy}";

                    dataLines.Add((string.Empty, csv));
                }

            }
            return new ParseResult
            {
                
                Title = "SampleDependencyBox 'sdtp'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}