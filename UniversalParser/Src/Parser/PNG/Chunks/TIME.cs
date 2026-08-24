namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class TIME
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

            if (type != "tIME")
                throw new InvalidDataException($"Expected tIME but got '{type}'");

            if (length != 7)
                throw new InvalidDataException("tIME must be 7 bytes");

            ushort year = reader.ReadUInt16BE();
            byte month = reader.ReadByte();
            byte day = reader.ReadByte();
            byte hour = reader.ReadByte();
            byte minute = reader.ReadByte();
            byte second = reader.ReadByte();

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            var dt = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("TimestampUTC", dt.ToString("yyyy-MM-dd HH:mm:ss")),
                ("Year", year.ToString()),
                ("Month", month.ToString()),
                ("Day", day.ToString()),
                ("Time", $"{hour}:{minute}:{second}")
            };

            return new ParseResult
            {
                Title = "ModificationTime 'tIME'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}