namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Pixi
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

            byte numChannels = reader.ReadByte();

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{flags:X6}"),
                ("num_channels", numChannels.ToString())
            };
            if(numChannels > 0)
            {
                byte bits = reader.ReadByte();
                dataLines.Add(($"bits_per_channel[{numChannels}]", bits.ToString()));
                for (int i = 1; i < numChannels; i++)
                {
                    bits = reader.ReadByte();
                    dataLines.Add((string.Empty, bits.ToString()));
                }
            }

            return new ParseResult
            {
                
                Title = $"PixelInformation '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}