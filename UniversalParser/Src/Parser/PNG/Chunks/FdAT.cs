namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class FdAT
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

            if (type != "fdAT")
                throw new InvalidDataException($"Expected fdAT but got '{type}'");

            if (length < 4)
                throw new InvalidDataException("fdAT must be at least 4 bytes");

            uint seq = reader.ReadUInt32BE();
            byte[] data = reader.ReadBytes((int)length - 4);

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("Sequence", seq.ToString()),
                ("CompressedSize", data.Length.ToString()),
            };

            return new ParseResult
            {
                Title = "FrameData 'fdAT'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}