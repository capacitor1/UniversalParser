namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class EXIf
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

            if (type != "eXIf")
                throw new InvalidDataException($"Expected eXIf but got '{type}'");

            byte[] exifPayload = reader.ReadBytes((int)length);
            var exifresult = EXIF.ExifParser.Parse(exifPayload);

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("ExifData", $"Length  =  {length}"),
            };

            foreach(var i in exifresult)
            {
                dataLines.Add((string.Empty,$"{i.Key}  =  {i.Value}"));
            }

            return new ParseResult
            {
                Title = "EXIFContainer 'eXIf'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}