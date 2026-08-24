namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class FcTL
    {
        public static ParseResult Parse(PNGParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var fs = parser.FileStream;
            var reader = new PngReader(fs);

            long pos = (long)node.Position;
            reader.Seek(pos);

            uint length = reader.ReadUInt32BE();
            string type = reader.ReadFourCC();

            if (type != "fcTL")
                throw new InvalidDataException($"Expected fcTL but got '{type}'");

            if (length != 26)
                throw new InvalidDataException("fcTL must be 26 bytes");

            uint seq = reader.ReadUInt32BE();
            uint w = reader.ReadUInt32BE();
            uint h = reader.ReadUInt32BE();
            uint xOff = reader.ReadUInt32BE();
            uint yOff = reader.ReadUInt32BE();

            ushort delayNum = reader.ReadUInt16BE();
            ushort delayDen = reader.ReadUInt16BE();
            byte disposeOp = reader.ReadByte();
            byte blendOp = reader.ReadByte();

            double delay = delayDen == 0 ? 0 : (double)delayNum / delayDen;

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("Sequence", seq.ToString()),
                ("Size", $"{w}x{h}"),
                ("Offset", $"{xOff},{yOff}"),
                ("Delay", delay.ToString("F4")),
                ("DisposeOp", disposeOp.ToString()),
                ("BlendOp", blendOp.ToString())
            };

            return new ParseResult
            {
                Title = "FrameControl 'fcTL'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}